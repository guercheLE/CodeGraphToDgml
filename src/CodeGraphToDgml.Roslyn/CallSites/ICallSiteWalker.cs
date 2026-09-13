using System.Collections.Generic;
using System.Threading;
using Microsoft.CodeAnalysis;

namespace CodeGraphToDgml.Roslyn.CallSites;

/// <summary>
/// One call site discovered in a member body. <see cref="Symbol"/> is already normalized
/// (see <see cref="CallGraphSyntaxWalker.NormalizeSymbol"/>), passes
/// <see cref="CallGraphSyntaxWalker.IsSupportedCalleeSymbol"/>, and is never the walked member
/// itself. The same symbol may appear at several sites; callers decide how to de-duplicate.
/// </summary>
internal sealed class RawCallSite
{
    public RawCallSite(SyntaxNode site, ISymbol symbol, ISymbol? fluentReceiver = null, bool isEventSubscription = false)
    {
        Site = site;
        Symbol = symbol;
        FluentReceiver = fluentReceiver;
        IsEventSubscription = isEventSubscription;
    }

    /// <summary>The syntax node the callee was discovered at (invocation, creation, statement, ...).</summary>
    public SyntaxNode Site { get; }

    public ISymbol Symbol { get; }

    /// <summary>See <see cref="CalleeInfo.FluentReceiver"/>.</summary>
    public ISymbol? FluentReceiver { get; }

    /// <summary>
    /// True when the site is an event subscription (<c>+=</c>/<c>-=</c>, <c>AddHandler</c>/
    /// <c>RemoveHandler</c>) whose handler is the callee. <see cref="CallGraphSyntaxWalker.FindCalleesAsync"/>
    /// skips these and appends handlers through <see cref="ICallSiteWalker.EnumerateEventSubscriptionHandlers"/>
    /// instead, preserving its historical ordering; per-site consumers use them in place.
    /// </summary>
    public bool IsEventSubscription { get; }
}

/// <summary>
/// Language-specific discovery of call sites in a member body. Implementations exist for C# and
/// Visual Basic; <see cref="CallSiteWalkerFactory"/> picks one from the syntax language.
/// </summary>
internal interface ICallSiteWalker
{
    /// <summary>
    /// Maps a symbol's declaring syntax node to the node whose descendants contain the body.
    /// C# declarations already are the body container; Visual Basic declaring references point
    /// at the header statement (<c>Sub Foo()</c>), so the enclosing block is returned instead.
    /// </summary>
    SyntaxNode GetBodyNode(SyntaxNode declaringNode);

    /// <summary>
    /// Enumerates every call site under <paramref name="body"/> in post-order (evaluation order),
    /// without de-duplication. For an invocation the order is: chained member accesses of the
    /// receiver in execution order, the invoked member, then method groups passed to delegate
    /// parameters. For an object creation: the constructor, then method-group arguments.
    /// </summary>
    IEnumerable<RawCallSite> EnumerateCallSites(
        SyntaxNode body,
        SemanticModel semanticModel,
        ISymbol? selfNormalized,
        CancellationToken cancellationToken);

    /// <summary>
    /// Enumerates the handler methods of every event subscription under <paramref name="body"/>
    /// in source order (a separate pre-order pass, matching the historical callee ordering).
    /// </summary>
    IEnumerable<ISymbol> EnumerateEventSubscriptionHandlers(
        SyntaxNode body,
        SemanticModel semanticModel,
        ISymbol? selfNormalized,
        CancellationToken cancellationToken);
}
