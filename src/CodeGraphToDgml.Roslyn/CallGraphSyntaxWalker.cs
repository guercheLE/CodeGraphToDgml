using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CodeGraphToDgml.Roslyn.CallSites;
using Microsoft.CodeAnalysis;
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
/// compilations. Language-specific syntax handling lives in
/// <see cref="CallSites.ICallSiteWalker"/> implementations (C# and Visual Basic).
/// </summary>
public static class CallGraphSyntaxWalker
{
    /// <summary>
    /// If <paramref name="symbol"/> is an accessor (property getter/setter, event add/remove),
    /// returns its associated property/event instead; if it is a reduced extension method
    /// (instance-style call), returns the unreduced static form so both call styles compare
    /// equal. Always returns the original definition.
    /// </summary>
    public static ISymbol? NormalizeSymbol(ISymbol? symbol)
    {
        if (symbol is IMethodSymbol method)
        {
            if (method.ReducedFrom is not null)
            {
                symbol = method.ReducedFrom;
            }
            else if (method.AssociatedSymbol is not null)
            {
                symbol = method.AssociatedSymbol;
            }
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
    /// Discovers every distinct callee reachable from <paramref name="symbol"/>'s declaring
    /// syntax, in first-occurrence order: direct invocations, chained member-access invocations,
    /// constructor calls (including constructor chaining), property writes and indexer accesses,
    /// collection initializer <c>Add</c> calls, event raises, method groups passed to
    /// delegate-typed parameters, event subscriptions, and delegate variables/fields invoked (or
    /// passed onward) within the same member body. Supports C# and Visual Basic.
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
            var walker = CallSiteWalkerFactory.For(syntaxNode.Language);
            if (walker is null)
            {
                continue;
            }

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

            var body = walker.GetBodyNode(syntaxNode);

            foreach (var site in walker.EnumerateCallSites(body, semanticModel, selfNormalized, cancellationToken))
            {
                // Event subscriptions are appended by the trailing pass below so the historical
                // callee ordering (handlers last) is preserved.
                if (site.IsEventSubscription)
                {
                    continue;
                }

                if (seen.Add(site.Symbol))
                {
                    callees.Add(new CalleeInfo(site.Symbol, site.FluentReceiver));
                }
            }

            foreach (var handler in walker.EnumerateEventSubscriptionHandlers(body, semanticModel, selfNormalized, cancellationToken))
            {
                if (seen.Add(handler))
                {
                    callees.Add(new CalleeInfo(handler));
                }
            }
        }

        return callees;
    }

    /// <summary>
    /// Yields every C# <see cref="InvocationExpressionSyntax"/> reachable from <paramref name="root"/>
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
}
