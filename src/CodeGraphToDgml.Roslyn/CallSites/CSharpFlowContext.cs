using System.Collections.Generic;
using System.Linq;
using System.Text;
using CodeGraphToDgml.Core;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodeGraphToDgml.Roslyn.CallSites;

/// <summary>
/// Maps a C# call site to the control-flow constructs enclosing it: if/else-if/else chains,
/// switch statements and expressions, and conditional expressions become <c>alt</c> sections;
/// for/foreach/while/do become <c>loop</c>; catch clauses become <c>break</c>. Try bodies,
/// finally blocks, lambdas, local functions, <c>using</c>, <c>lock</c>, <c>?.</c> and <c>??</c>
/// are transparent. A call inside a condition (e.g. <c>if (Check())</c>) sits outside the
/// fragment, since it runs before the branch is chosen.
/// </summary>
internal static class CSharpFlowContext
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
            case IfStatementSyntax ifStatement when child == ifStatement.Statement:
                return IfChainScope(ifStatement, isElse: false);

            case ElseClauseSyntax elseClause when child == elseClause.Statement && child is not IfStatementSyntax
                && elseClause.Parent is IfStatementSyntax owner:
                return IfChainScope(owner, isElse: true);

            case SwitchSectionSyntax section when section.Parent is SwitchStatementSyntax switchStatement
                && section.Statements.Contains(child):
                return SwitchSectionScope(switchStatement, section);

            case SwitchExpressionArmSyntax arm when arm.Parent is SwitchExpressionSyntax switchExpression
                && (child == arm.Expression || child == arm.WhenClause):
                return SwitchArmScope(switchExpression, arm);

            case ConditionalExpressionSyntax conditional when child == conditional.WhenTrue:
                return new RawFlowScope(conditional, SequenceFragmentKind.Alt, 0, Compact(conditional.Condition), false, Compact(conditional.Condition));

            case ConditionalExpressionSyntax conditional when child == conditional.WhenFalse:
                return new RawFlowScope(conditional, SequenceFragmentKind.Alt, 1, "else", true, Compact(conditional.Condition));

            case ForStatementSyntax forStatement when child == forStatement.Statement
                || child == forStatement.Condition || forStatement.Incrementors.Contains(child):
                return Loop(forStatement, forStatement.Condition is null ? "for" : "for " + Compact(forStatement.Condition));

            case ForEachStatementSyntax forEach when child == forEach.Statement:
                return Loop(forEach, "foreach " + forEach.Identifier.ValueText + " in " + Compact(forEach.Expression));

            case ForEachVariableStatementSyntax forEachVariable when child == forEachVariable.Statement:
                return Loop(forEachVariable, "foreach " + Compact(forEachVariable.Variable) + " in " + Compact(forEachVariable.Expression));

            case WhileStatementSyntax whileStatement when child == whileStatement.Statement || child == whileStatement.Condition:
                return Loop(whileStatement, "while " + Compact(whileStatement.Condition));

            case DoStatementSyntax doStatement when child == doStatement.Statement || child == doStatement.Condition:
                return Loop(doStatement, "do while " + Compact(doStatement.Condition));

            case CatchClauseSyntax catchClause when child == catchClause.Block || child == catchClause.Filter:
            {
                var label = "catch";
                if (catchClause.Declaration is { } declaration)
                    label += " " + Compact(declaration.Type);
                if (catchClause.Filter is { } filter)
                    label += " when " + Compact(filter.FilterExpression);
                return new RawFlowScope(catchClause, SequenceFragmentKind.Break, 0, label, false, label);
            }

            default:
                return null;
        }
    }

    private static RawFlowScope Loop(SyntaxNode construct, string label)
        => new(construct, SequenceFragmentKind.Loop, 0, label, false, label);

    // An if / else if / else chain is one fragment instance rooted at the outermost `if`;
    // each `if` in the chain is a section, and the trailing plain `else` is the last section.
    private static RawFlowScope IfChainScope(IfStatementSyntax ifStatement, bool isElse)
    {
        var root = ifStatement;
        while (root.Parent is ElseClauseSyntax { Parent: IfStatementSyntax outer })
            root = outer;

        int chainLength = 0;
        int indexOfThis = 0;
        var current = root;
        while (true)
        {
            if (current == ifStatement)
                indexOfThis = chainLength;
            chainLength++;

            if (current.Else?.Statement is IfStatementSyntax next)
                current = next;
            else
                break;
        }

        var firstLabel = Compact(root.Condition);
        return isElse
            ? new RawFlowScope(root, SequenceFragmentKind.Alt, chainLength, "else", true, firstLabel)
            : new RawFlowScope(root, SequenceFragmentKind.Alt, indexOfThis, Compact(ifStatement.Condition), false, firstLabel);
    }

    private static RawFlowScope SwitchSectionScope(SwitchStatementSyntax switchStatement, SwitchSectionSyntax section)
    {
        var expression = Compact(switchStatement.Expression);
        var labels = section.Labels.Select(label => SwitchLabelText(expression, label)).ToList();
        bool isDefault = section.Labels.All(label => label is DefaultSwitchLabelSyntax);
        var text = string.Join(" or ", labels);

        var firstSection = switchStatement.Sections.FirstOrDefault();
        var firstLabel = firstSection is null
            ? expression
            : string.Join(" or ", firstSection.Labels.Select(label => SwitchLabelText(expression, label)));

        return new RawFlowScope(switchStatement, SequenceFragmentKind.Alt, switchStatement.Sections.IndexOf(section), text, isDefault, firstLabel);
    }

    private static string SwitchLabelText(string expression, SwitchLabelSyntax label)
    {
        switch (label)
        {
            case CaseSwitchLabelSyntax caseLabel:
                return expression + " == " + Compact(caseLabel.Value);
            case CasePatternSwitchLabelSyntax patternLabel:
            {
                var text = expression + " is " + Compact(patternLabel.Pattern);
                if (patternLabel.WhenClause is { } when)
                    text += " when " + Compact(when.Condition);
                return text;
            }
            default:
                return "default";
        }
    }

    private static RawFlowScope SwitchArmScope(SwitchExpressionSyntax switchExpression, SwitchExpressionArmSyntax arm)
    {
        var governing = Compact(switchExpression.GoverningExpression);
        var firstArm = switchExpression.Arms.FirstOrDefault();
        return new RawFlowScope(
            switchExpression,
            SequenceFragmentKind.Alt,
            switchExpression.Arms.IndexOf(arm),
            ArmText(governing, arm),
            arm.Pattern is DiscardPatternSyntax,
            firstArm is null ? governing : ArmText(governing, firstArm));
    }

    private static string ArmText(string governing, SwitchExpressionArmSyntax arm)
    {
        if (arm.Pattern is DiscardPatternSyntax)
            return "default";

        var text = governing + " is " + Compact(arm.Pattern);
        if (arm.WhenClause is { } when)
            text += " when " + Compact(when.Condition);
        return text;
    }

    /// <summary>
    /// Source text of a node with comments removed and whitespace collapsed to single spaces,
    /// so a multi-line condition renders as one readable label.
    /// </summary>
    internal static string Compact(SyntaxNode? node)
    {
        if (node is null)
            return string.Empty;

        var sb = new StringBuilder();
        SyntaxToken? previous = null;
        foreach (var token in node.DescendantTokens())
        {
            if (previous is { } prev && sb.Length > 0 && (prev.HasTrailingTrivia || token.HasLeadingTrivia))
                sb.Append(' ');

            sb.Append(token.Text);
            previous = token;
        }

        return sb.ToString().Trim();
    }
}
