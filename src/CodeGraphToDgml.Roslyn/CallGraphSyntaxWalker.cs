using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RoslynSolution = Microsoft.CodeAnalysis.Solution;

namespace CodeGraphToDgml.Roslyn;

/// <summary>
/// A callee discovered while walking a method body, together with optional metadata about
/// how it was reached.
/// </summary>
public sealed class CalleeInfo
{
    public CalleeInfo(ISymbol symbol, ISymbol? fluentReceiver = null)
    {
        Symbol = symbol;
        FluentReceiver = fluentReceiver;
    }

    public ISymbol Symbol { get; }

    /// <summary>
    /// When this callee's invocation receiver is itself another discovered invocation in the same
    /// fluent chain (e.g. <c>obj.GetClient().GetProduct()</c>), holds that receiver's resolved
    /// symbol. This is metadata for sequence-diagram rendering only (nesting the dependent call
    /// under its receiver's activation) — it must never influence the static DGML call graph,
    /// where the enclosing method remains the lexical caller of both invocations.
    /// </summary>
    public ISymbol? FluentReceiver { get; }
}

/// <summary>
/// Pure Roslyn syntax/semantic-model logic for discovering method calls, extracted out of the
/// VS-SDK-laden Vsix project so it can be exercised by ordinary unit tests against in-memory
/// compilations.
/// </summary>
public static class CallGraphSyntaxWalker
{
    /// <summary>
    /// If <paramref name="symbol"/> is an accessor (property getter/setter, event add/remove),
    /// returns its associated property/event instead. Always returns the original definition.
    /// </summary>
    public static ISymbol? NormalizeSymbol(ISymbol? symbol)
    {
        if (symbol is IMethodSymbol method && method.AssociatedSymbol is not null)
        {
            symbol = method.AssociatedSymbol;
        }

        return symbol?.OriginalDefinition;
    }

    /// <summary>
    /// If <paramref name="symbol"/> is a lambda, anonymous method, or local function,
    /// walks up the containing-symbol chain until a real (non-lambda) symbol is found.
    /// </summary>
    public static ISymbol? UnwrapLambdaContainer(ISymbol? symbol)
    {
        while (symbol is IMethodSymbol method
            && (method.MethodKind == MethodKind.AnonymousFunction
                || method.MethodKind == MethodKind.LocalFunction))
        {
            symbol = symbol.ContainingSymbol;
        }

        return symbol;
    }

    /// <summary>
    /// Excludes lambdas, anonymous methods, and local functions from being treated as call-graph
    /// nodes in their own right (they have empty names and synthesised containing types). Their
    /// inner invocations are still discovered because the syntax walker descends through their
    /// bodies like any other syntax.
    /// </summary>
    public static bool IsSupportedCalleeSymbol(ISymbol symbol)
    {
        if (symbol is IMethodSymbol method
            && (method.MethodKind == MethodKind.AnonymousFunction
                || method.MethodKind == MethodKind.LocalFunction))
        {
            return false;
        }

        return symbol.Kind is SymbolKind.Method or SymbolKind.Property or SymbolKind.Event;
    }

    /// <summary>
    /// Discovers every callee reachable from <paramref name="symbol"/>'s declaring syntax:
    /// direct invocations, chained member-access invocations, method groups passed to
    /// delegate-typed parameters, event subscriptions (<c>+=</c>/<c>-=</c>), and delegate
    /// variables/fields invoked indirectly within the same method body.
    /// </summary>
    public static async Task<IReadOnlyList<CalleeInfo>> FindCalleesAsync(
        ISymbol symbol,
        RoslynSolution solution,
        CancellationToken cancellationToken)
    {
        var callees = new List<CalleeInfo>();
        var seen = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
        var selfNormalized = NormalizeSymbol(symbol);

        foreach (var syntaxRef in symbol.DeclaringSyntaxReferences)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var syntaxNode = await syntaxRef.GetSyntaxAsync(cancellationToken).ConfigureAwait(false);
            var document = solution.GetDocument(syntaxRef.SyntaxTree);
            if (document is null)
            {
                continue;
            }

            var semanticModel = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
            if (semanticModel is null)
            {
                continue;
            }

            // 1c: locals/fields of delegate type assigned a bare method group, scoped to this
            // declaring syntax (method body) only — no interprocedural dataflow.
            var localDelegateMap = BuildLocalDelegateMap(syntaxNode, semanticModel, cancellationToken);

            // Tracks, within this declaring syntax, which resolved symbol each invocation syntax
            // node produced — used to detect fluent chains (1g), where an outer invocation's
            // receiver is itself a previously-visited invocation.
            var invocationSymbolOf = new Dictionary<InvocationExpressionSyntax, ISymbol>();

            foreach (var invocation in GetInvocationsPostOrder(syntaxNode))
            {
                var symbolInfo = semanticModel.GetSymbolInfo(invocation, cancellationToken);
                var resolved = symbolInfo.Symbol ?? (symbolInfo.CandidateSymbols.Length == 1 ? symbolInfo.CandidateSymbols[0] : null);

                // 1c: redirect calls through a tracked local/field delegate variable to the
                // originally-assigned method-group target instead of the delegate's Invoke().
                if (resolved is IMethodSymbol { MethodKind: MethodKind.DelegateInvoke })
                {
                    var targetSymbolInfo = semanticModel.GetSymbolInfo(invocation.Expression, cancellationToken);
                    var targetSymbol = targetSymbolInfo.Symbol ?? targetSymbolInfo.CandidateSymbols.FirstOrDefault();
                    var targetNormalized = NormalizeSymbol(targetSymbol);
                    if (targetNormalized != null && localDelegateMap.TryGetValue(targetNormalized, out var redirected))
                    {
                        resolved = redirected;
                    }
                }

                if (resolved is null)
                {
                    continue;
                }

                var normalized = NormalizeSymbol(resolved);
                if (normalized is null || !IsSupportedCalleeSymbol(normalized))
                {
                    continue;
                }

                // Skip self-references.
                if (SymbolEqualityComparer.Default.Equals(normalized, selfNormalized))
                {
                    continue;
                }

                bool isNewInvocation = seen.Add(normalized);

                // 1g: this invocation is a fluent chain step if its receiver is itself a
                // directly-nested invocation already visited (post-order guarantees the receiver
                // was processed first). Metadata only — never affects the flat callee list itself.
                ISymbol? fluentReceiver = null;
                if (invocation.Expression is MemberAccessExpressionSyntax outerMember
                    && outerMember.Expression is InvocationExpressionSyntax receiverInvocation
                    && invocationSymbolOf.TryGetValue(receiverInvocation, out var receiverSymbol))
                {
                    fluentReceiver = receiverSymbol;
                }

                invocationSymbolOf[invocation] = normalized;

                if (isNewInvocation)
                {
                    callees.Add(new CalleeInfo(normalized, fluentReceiver));
                }

                // Collect chained member accesses (e.g., obj.Property.Method()).
                // Walk from the invocation's receiver inward; the walk yields accesses
                // in innermost-first order, so we reverse to get execution order and
                // insert them before the main invocation so the diagram shows them first.
                var chainedBefore = new List<ISymbol>();
                var expr = invocation.Expression;
                while (expr is MemberAccessExpressionSyntax memberAccess)
                {
                    var memberSymbolInfo = semanticModel.GetSymbolInfo(memberAccess, cancellationToken);
                    var memberResolved = memberSymbolInfo.Symbol ?? (memberSymbolInfo.CandidateSymbols.Length == 1 ? memberSymbolInfo.CandidateSymbols[0] : null);
                    var memberNormalized = NormalizeSymbol(memberResolved);
                    if (memberNormalized != null && IsSupportedCalleeSymbol(memberNormalized))
                    {
                        if (!SymbolEqualityComparer.Default.Equals(memberNormalized, selfNormalized) && seen.Add(memberNormalized))
                        {
                            chainedBefore.Add(memberNormalized);
                        }
                    }
                    expr = memberAccess.Expression;
                }

                if (chainedBefore.Count > 0)
                {
                    chainedBefore.Reverse();
                    var infos = chainedBefore.Select(s => new CalleeInfo(s)).ToList();
                    if (isNewInvocation)
                        callees.InsertRange(callees.Count - 1, infos);
                    else
                        callees.AddRange(infos);
                }

                // 1a: method groups passed as arguments to delegate-typed parameters.
                foreach (var argCallee in GetMethodGroupArgumentCallees(invocation, semanticModel, selfNormalized, seen, cancellationToken))
                {
                    callees.Add(new CalleeInfo(argCallee));
                }
            }

            // 1b: event subscriptions (+=/-=) — walked once per declaring syntax; these are
            // independent statements, not nested inside the invocation walk above.
            foreach (var handlerCallee in GetEventSubscriptionCallees(syntaxNode, semanticModel, selfNormalized, seen, cancellationToken))
            {
                callees.Add(new CalleeInfo(handlerCallee));
            }
        }

        return callees;
    }

    /// <summary>
    /// Yields every <see cref="InvocationExpressionSyntax"/> reachable from <paramref name="root"/>
    /// in post-order (children before parent). This matches C# argument-evaluation order: an
    /// invocation used as an argument (or as a fluent-chain receiver) is yielded before the outer
    /// invocation that receives it.
    /// </summary>
    public static IEnumerable<InvocationExpressionSyntax> GetInvocationsPostOrder(SyntaxNode root)
    {
        foreach (var child in root.ChildNodes())
            foreach (var inv in GetInvocationsPostOrder(child))
                yield return inv;

        if (root is InvocationExpressionSyntax invocation)
            yield return invocation;
    }

    // 1a: for each argument that is a bare method group (not itself an invocation or lambda),
    // resolve it and, if the corresponding parameter's type is a delegate type, treat it as a
    // deferred call. General rule — covers Action<T...>, Func<T...>, EventHandler, ThreadStart,
    // WaitCallback, TimerCallback, and any custom delegate type; not a hardcoded method-name list.
    // Named/reordered arguments are matched positionally — a known, accepted limitation.
    private static IEnumerable<ISymbol> GetMethodGroupArgumentCallees(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        ISymbol? selfNormalized,
        HashSet<ISymbol> seen,
        CancellationToken cancellationToken)
    {
        var symbolInfo = semanticModel.GetSymbolInfo(invocation, cancellationToken);
        var invokedMethod = symbolInfo.Symbol as IMethodSymbol
            ?? symbolInfo.CandidateSymbols.OfType<IMethodSymbol>().FirstOrDefault();
        if (invokedMethod is null)
        {
            yield break;
        }

        var parameters = invokedMethod.Parameters;
        var args = invocation.ArgumentList.Arguments;

        for (int i = 0; i < args.Count; i++)
        {
            var argExpr = args[i].Expression;
            if (argExpr is not (IdentifierNameSyntax or MemberAccessExpressionSyntax))
            {
                continue;
            }

            var argSymbolInfo = semanticModel.GetSymbolInfo(argExpr, cancellationToken);
            var argSymbol = argSymbolInfo.Symbol ?? argSymbolInfo.CandidateSymbols.FirstOrDefault();
            var normalizedArg = NormalizeSymbol(argSymbol);
            if (normalizedArg is not IMethodSymbol methodGroupTarget || !IsSupportedCalleeSymbol(methodGroupTarget))
            {
                continue;
            }

            if (SymbolEqualityComparer.Default.Equals(methodGroupTarget, selfNormalized))
            {
                continue;
            }

            IParameterSymbol? param = i < parameters.Length
                ? parameters[i]
                : (parameters.Length > 0 && parameters[parameters.Length - 1].IsParams ? parameters[parameters.Length - 1] : null);

            if (param is null || param.Type.TypeKind != TypeKind.Delegate)
            {
                continue;
            }

            if (seen.Add(methodGroupTarget))
            {
                yield return methodGroupTarget;
            }
        }
    }

    // 1b: `SomeEvent += obj.Method;` / `-= obj.Method;` — the handler method becomes a callee.
    private static IEnumerable<ISymbol> GetEventSubscriptionCallees(
        SyntaxNode root,
        SemanticModel semanticModel,
        ISymbol? selfNormalized,
        HashSet<ISymbol> seen,
        CancellationToken cancellationToken)
    {
        foreach (var assignment in root.DescendantNodesAndSelf().OfType<AssignmentExpressionSyntax>())
        {
            if (assignment.Kind() is not (SyntaxKind.AddAssignmentExpression or SyntaxKind.SubtractAssignmentExpression))
            {
                continue;
            }

            var leftInfo = semanticModel.GetSymbolInfo(assignment.Left, cancellationToken);
            if (leftInfo.Symbol is not IEventSymbol)
            {
                continue;
            }

            if (assignment.Right is not (IdentifierNameSyntax or MemberAccessExpressionSyntax))
            {
                continue;
            }

            var rightInfo = semanticModel.GetSymbolInfo(assignment.Right, cancellationToken);
            var rightSymbol = rightInfo.Symbol ?? rightInfo.CandidateSymbols.FirstOrDefault();
            var normalized = NormalizeSymbol(rightSymbol);
            if (normalized is not IMethodSymbol handlerMethod || !IsSupportedCalleeSymbol(handlerMethod))
            {
                continue;
            }

            if (SymbolEqualityComparer.Default.Equals(handlerMethod, selfNormalized))
            {
                continue;
            }

            if (seen.Add(handlerMethod))
            {
                yield return handlerMethod;
            }
        }
    }

    // 1c: maps locals/fields of delegate type to the method-group symbol they were assigned,
    // scoped to a single declaring syntax (method body). Last-write-wins for reassignment —
    // an accepted limitation since interprocedural/branch-aware dataflow is out of scope.
    private static Dictionary<ISymbol, ISymbol> BuildLocalDelegateMap(
        SyntaxNode root,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        var map = new Dictionary<ISymbol, ISymbol>(SymbolEqualityComparer.Default);

        foreach (var declarator in root.DescendantNodesAndSelf().OfType<VariableDeclaratorSyntax>())
        {
            var valueExpr = declarator.Initializer?.Value;
            if (valueExpr is not (IdentifierNameSyntax or MemberAccessExpressionSyntax))
            {
                continue;
            }

            var declaredSymbol = semanticModel.GetDeclaredSymbol(declarator, cancellationToken) as ILocalSymbol;
            if (declaredSymbol is null || declaredSymbol.Type.TypeKind != TypeKind.Delegate)
            {
                continue;
            }

            var valueInfo = semanticModel.GetSymbolInfo(valueExpr, cancellationToken);
            var valueSymbol = valueInfo.Symbol ?? valueInfo.CandidateSymbols.FirstOrDefault();
            var normalizedValue = NormalizeSymbol(valueSymbol);
            if (normalizedValue is IMethodSymbol methodGroup && IsSupportedCalleeSymbol(methodGroup))
            {
                map[declaredSymbol] = methodGroup;
            }
        }

        foreach (var assignment in root.DescendantNodesAndSelf().OfType<AssignmentExpressionSyntax>())
        {
            if (assignment.Kind() != SyntaxKind.SimpleAssignmentExpression)
            {
                continue;
            }

            if (assignment.Right is not IdentifierNameSyntax and not MemberAccessExpressionSyntax)
            {
                continue;
            }

            var valueExpr = assignment.Right;

            var leftInfo = semanticModel.GetSymbolInfo(assignment.Left, cancellationToken);
            ITypeSymbol? leftType = leftInfo.Symbol switch
            {
                ILocalSymbol local => local.Type,
                IFieldSymbol field => field.Type,
                _ => null,
            };

            if (leftType is null || leftType.TypeKind != TypeKind.Delegate || leftInfo.Symbol is null)
            {
                continue;
            }

            var valueInfo = semanticModel.GetSymbolInfo(valueExpr, cancellationToken);
            var valueSymbol = valueInfo.Symbol ?? valueInfo.CandidateSymbols.FirstOrDefault();
            var normalizedValue = NormalizeSymbol(valueSymbol);
            if (normalizedValue is IMethodSymbol methodGroup && IsSupportedCalleeSymbol(methodGroup))
            {
                map[leftInfo.Symbol] = methodGroup;
            }
        }

        return map;
    }
}
