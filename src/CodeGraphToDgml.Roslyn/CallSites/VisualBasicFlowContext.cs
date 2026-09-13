using System.Collections.Generic;
using System.Linq;
using CodeGraphToDgml.Core;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.VisualBasic;
using Microsoft.CodeAnalysis.VisualBasic.Syntax;

namespace CodeGraphToDgml.Roslyn.CallSites;

/// <summary>
/// Visual Basic counterpart of <see cref="CSharpFlowContext"/>: <c>If</c>/<c>ElseIf</c>/<c>Else</c>
/// blocks, single-line <c>If</c>, <c>Select Case</c>, and the ternary <c>If(a, b, c)</c> become
/// <c>alt</c> sections; <c>For</c>, <c>For Each</c>, <c>While</c>, and <c>Do</c> loops become
/// <c>loop</c>; <c>Catch</c> blocks become <c>break</c>. <c>Try</c> bodies, <c>Finally</c>,
/// <c>Using</c>, <c>SyncLock</c>, <c>With</c>, lambdas, and <c>If(a, b)</c> are transparent.
/// </summary>
internal static class VisualBasicFlowContext
{
    public static IReadOnlyList<RawFlowScope> GetFlowContext(SyntaxNode site, SyntaxNode body)
    {
        var scopes = new List<RawFlowScope>();
        var node = site;

        while (node != body && node.Parent is { } parent && parent != body)
        {
            var scope = ScopeFor(parent, node);
            if (scope is not null)
                scopes.Add(scope);

            node = parent;
        }

        scopes.Reverse();
        return scopes;
    }

    private static RawFlowScope? ScopeFor(SyntaxNode parent, SyntaxNode child)
    {
        switch (parent)
        {
            case MultiLineIfBlockSyntax ifBlock when IsIn(ifBlock.Statements, child):
                return new RawFlowScope(ifBlock, SequenceFragmentKind.Alt, 0, Compact(ifBlock.IfStatement.Condition), false, Compact(ifBlock.IfStatement.Condition));

            case ElseIfBlockSyntax elseIf when elseIf.Parent is MultiLineIfBlockSyntax owner && IsIn(elseIf.Statements, child):
                return new RawFlowScope(owner, SequenceFragmentKind.Alt, 1 + owner.ElseIfBlocks.IndexOf(elseIf), Compact(elseIf.ElseIfStatement.Condition), false, Compact(owner.IfStatement.Condition));

            case ElseBlockSyntax elseBlock when elseBlock.Parent is MultiLineIfBlockSyntax owner && IsIn(elseBlock.Statements, child):
                return new RawFlowScope(owner, SequenceFragmentKind.Alt, 1 + owner.ElseIfBlocks.Count, "else", true, Compact(owner.IfStatement.Condition));

            case SingleLineIfStatementSyntax singleIf when IsIn(singleIf.Statements, child):
                return new RawFlowScope(singleIf, SequenceFragmentKind.Alt, 0, Compact(singleIf.Condition), false, Compact(singleIf.Condition));

            case SingleLineElseClauseSyntax singleElse when singleElse.Parent is SingleLineIfStatementSyntax owner && IsIn(singleElse.Statements, child):
                return new RawFlowScope(owner, SequenceFragmentKind.Alt, 1, "else", true, Compact(owner.Condition));

            case CaseBlockSyntax caseBlock when caseBlock.Parent is SelectBlockSyntax select && IsIn(caseBlock.Statements, child):
                return CaseScope(select, caseBlock);

            case TernaryConditionalExpressionSyntax ternary when child == ternary.WhenTrue:
                return new RawFlowScope(ternary, SequenceFragmentKind.Alt, 0, Compact(ternary.Condition), false, Compact(ternary.Condition));

            case TernaryConditionalExpressionSyntax ternary when child == ternary.WhenFalse:
                return new RawFlowScope(ternary, SequenceFragmentKind.Alt, 1, "else", true, Compact(ternary.Condition));

            case ForBlockSyntax forBlock when IsIn(forBlock.Statements, child):
            {
                var header = forBlock.ForStatement;
                var label = "for " + Compact(header.ControlVariable) + " = " + Compact(header.FromValue) + " to " + Compact(header.ToValue);
                return Loop(forBlock, label);
            }

            case ForEachBlockSyntax forEach when IsIn(forEach.Statements, child):
            {
                var header = forEach.ForEachStatement;
                return Loop(forEach, "for each " + Compact(header.ControlVariable) + " in " + Compact(header.Expression));
            }

            case WhileBlockSyntax whileBlock when IsIn(whileBlock.Statements, child):
                return Loop(whileBlock, "while " + Compact(whileBlock.WhileStatement.Condition));

            case DoLoopBlockSyntax doLoop when IsIn(doLoop.Statements, child):
                return Loop(doLoop, DoLoopLabel(doLoop));

            case CatchBlockSyntax catchBlock when IsIn(catchBlock.Statements, child):
            {
                var header = catchBlock.CatchStatement;
                var label = "catch";
                if (header.AsClause is { } asClause)
                    label += " " + Compact(asClause.Type);
                if (header.WhenClause is { } when)
                    label += " when " + Compact(when.Filter);
                return new RawFlowScope(catchBlock, SequenceFragmentKind.Break, 0, label, false, label);
            }

            default:
                return null;
        }
    }

    private static bool IsIn(SyntaxList<StatementSyntax> statements, SyntaxNode child)
        => child is StatementSyntax statement && statements.Contains(statement);

    private static RawFlowScope Loop(SyntaxNode construct, string label)
        => new(construct, SequenceFragmentKind.Loop, 0, label, false, label);

    private static string DoLoopLabel(DoLoopBlockSyntax doLoop)
    {
        var topClause = doLoop.DoStatement.WhileOrUntilClause;
        if (topClause is not null)
            return "do " + topClause.WhileOrUntilKeyword.ValueText.ToLowerInvariant() + " " + Compact(topClause.Condition);

        var bottomClause = doLoop.LoopStatement.WhileOrUntilClause;
        if (bottomClause is not null)
            return "loop " + bottomClause.WhileOrUntilKeyword.ValueText.ToLowerInvariant() + " " + Compact(bottomClause.Condition);

        return "do";
    }

    private static RawFlowScope CaseScope(SelectBlockSyntax select, CaseBlockSyntax caseBlock)
    {
        var expression = Compact(select.SelectStatement.Expression);
        var clauses = caseBlock.CaseStatement.Cases;
        bool isElse = clauses.Count > 0 && clauses.All(clause => clause is ElseCaseClauseSyntax);
        var label = string.Join(" or ", clauses.Select(clause => CaseClauseText(expression, clause)));

        var first = select.CaseBlocks.FirstOrDefault();
        var firstLabel = first is null
            ? expression
            : string.Join(" or ", first.CaseStatement.Cases.Select(clause => CaseClauseText(expression, clause)));

        return new RawFlowScope(select, SequenceFragmentKind.Alt, select.CaseBlocks.IndexOf(caseBlock), label, isElse, firstLabel);
    }

    private static string CaseClauseText(string expression, CaseClauseSyntax clause)
    {
        return clause switch
        {
            SimpleCaseClauseSyntax simple => expression + " = " + Compact(simple.Value),
            RangeCaseClauseSyntax range => expression + " in " + Compact(range.LowerBound) + " To " + Compact(range.UpperBound),
            RelationalCaseClauseSyntax relational => expression + " " + relational.OperatorToken.ValueText + " " + Compact(relational.Value),
            _ => "else",
        };
    }

    private static string Compact(SyntaxNode? node) => CSharpFlowContext.Compact(node);
}
