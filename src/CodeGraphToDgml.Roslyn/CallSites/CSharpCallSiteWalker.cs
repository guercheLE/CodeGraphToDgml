using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodeGraphToDgml.Roslyn.CallSites;

/// <summary>
/// C# call-site discovery: direct invocations, chained member-access invocations, constructor
/// calls (including <c>: this/base(...)</c> chaining), property writes and indexer accesses,
/// collection initializer <c>Add</c> calls, event raises, method groups passed to delegate-typed
/// parameters (bare, delegate-creation-wrapped, or cast-wrapped), event subscriptions
/// (<c>+=</c>/<c>-=</c>), and delegate variables/fields invoked (or passed onward) within the
/// same method body.
/// </summary>
internal sealed class CSharpCallSiteWalker : ICallSiteWalker
{
    public static readonly CSharpCallSiteWalker Instance = new();

    private CSharpCallSiteWalker()
    {
    }

    public SyntaxNode GetBodyNode(SyntaxNode declaringNode) => declaringNode;

    public IEnumerable<RawCallSite> EnumerateCallSites(
        SyntaxNode body,
        SemanticModel semanticModel,
        ISymbol? selfNormalized,
        CancellationToken cancellationToken)
    {
        // 1c: locals/fields of delegate type assigned a bare method group, scoped to this
        // declaring syntax (method body) only — no interprocedural dataflow.
        var localDelegateMap = BuildLocalDelegateMap(body, semanticModel, cancellationToken);

        // Tracks which resolved symbol each invocation syntax node produced — used to detect
        // fluent chains (1g), where an outer invocation's receiver is itself a visited invocation.
        var invocationSymbolOf = new Dictionary<InvocationExpressionSyntax, ISymbol>();

        foreach (var site in GetCallSitesPostOrder(body))
        {
            cancellationToken.ThrowIfCancellationRequested();

            switch (site)
            {
                case InvocationExpressionSyntax invocation:
                {
                    var symbolInfo = semanticModel.GetSymbolInfo(invocation, cancellationToken);
                    var resolved = symbolInfo.Symbol ?? (symbolInfo.CandidateSymbols.Length == 1 ? symbolInfo.CandidateSymbols[0] : null);

                    // Parameter matching for method-group arguments uses the method as bound
                    // at the call site, before any delegate redirect below rewrites `resolved`.
                    var invokedForArgs = resolved as IMethodSymbol;

                    // 1c/4/5: a call through a delegate value — `del(...)`, `del.Invoke(...)`,
                    // `del.BeginInvoke(...)`, `MyEvent(...)`, `MyEvent?.Invoke(...)` — maps an
                    // event raise to the event symbol itself, and redirects a tracked
                    // local/field delegate variable to its originally-assigned method group.
                    var delegateTarget = TryResolveDelegateCallTarget(invocation, resolved, semanticModel, localDelegateMap, cancellationToken);
                    bool isDelegateRedirect = delegateTarget is not null;
                    resolved = delegateTarget ?? resolved;

                    // Historical behavior: an unresolvable, unsupported, or self-referential
                    // invocation contributes nothing at all (not even its chained members).
                    var normalized = Accept(resolved, selfNormalized);
                    if (normalized is null)
                        break;

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

                    // Chained member accesses (e.g., obj.Property.Method()) come before the main
                    // invocation so the diagram shows them first. Walking from the invocation's
                    // receiver inward yields innermost-first, so reverse to get execution order.
                    // When the call was resolved as a delegate call, its own member access
                    // (`a.BeginInvoke`, `del.Invoke`) is delegate machinery, not a chained
                    // member — start the walk below it.
                    var expr = isDelegateRedirect && invocation.Expression is MemberAccessExpressionSyntax delegateMember
                        ? delegateMember.Expression
                        : invocation.Expression;

                    var chainedBefore = new List<ISymbol>();
                    while (expr is MemberAccessExpressionSyntax memberAccess)
                    {
                        var memberSymbolInfo = semanticModel.GetSymbolInfo(memberAccess, cancellationToken);
                        var memberResolved = memberSymbolInfo.Symbol ?? (memberSymbolInfo.CandidateSymbols.Length == 1 ? memberSymbolInfo.CandidateSymbols[0] : null);
                        var memberNormalized = Accept(memberResolved, selfNormalized);
                        if (memberNormalized is not null && !SymbolEqualityComparer.Default.Equals(memberNormalized, normalized))
                        {
                            chainedBefore.Add(memberNormalized);
                        }

                        expr = memberAccess.Expression;
                    }

                    chainedBefore.Reverse();
                    foreach (var chained in chainedBefore)
                        yield return new RawCallSite(invocation, chained);

                    yield return new RawCallSite(invocation, normalized, fluentReceiver);

                    // 1a: method groups (and tracked delegate variables) passed as arguments
                    // to delegate-typed parameters.
                    if (invokedForArgs is not null)
                    {
                        foreach (var argCallee in GetMethodGroupArgumentCallees(invokedForArgs, invocation.ArgumentList.Arguments, semanticModel, selfNormalized, localDelegateMap, cancellationToken))
                            yield return new RawCallSite(invocation, argCallee);
                    }

                    break;
                }

                case BaseObjectCreationExpressionSyntax creation:
                {
                    // Item 3: `new Foo(...)` (and target-typed `new(...)`) — the constructor
                    // is a callee like any method call.
                    var creationInfo = semanticModel.GetSymbolInfo(creation, cancellationToken);
                    var ctor = (creationInfo.Symbol ?? (creationInfo.CandidateSymbols.Length == 1 ? creationInfo.CandidateSymbols[0] : null)) as IMethodSymbol;
                    var normalizedCtor = Accept(ctor, selfNormalized);
                    if (normalizedCtor is not null)
                        yield return new RawCallSite(creation, normalizedCtor);

                    // Method groups passed to constructor parameters, e.g. `new Thread(Run)`.
                    if (ctor is not null && creation.ArgumentList is { } ctorArgs)
                    {
                        foreach (var argCallee in GetMethodGroupArgumentCallees(ctor, ctorArgs.Arguments, semanticModel, selfNormalized, localDelegateMap, cancellationToken))
                            yield return new RawCallSite(creation, argCallee);
                    }

                    break;
                }

                case ConstructorInitializerSyntax ctorInitializer:
                {
                    // Item 3: `: this(...)` / `: base(...)` chaining when the traversed
                    // symbol is itself a constructor.
                    var initInfo = semanticModel.GetSymbolInfo(ctorInitializer, cancellationToken);
                    var normalizedInit = Accept(initInfo.Symbol ?? initInfo.CandidateSymbols.FirstOrDefault(), selfNormalized);
                    if (normalizedInit is not null)
                        yield return new RawCallSite(ctorInitializer, normalizedInit);
                    break;
                }

                case AssignmentExpressionSyntax assignment:
                {
                    var leftInfo = semanticModel.GetSymbolInfo(assignment.Left, cancellationToken);
                    var leftSymbol = leftInfo.Symbol ?? leftInfo.CandidateSymbols.FirstOrDefault();

                    if (leftSymbol is IPropertySymbol)
                    {
                        // Item 6: property writes — `x.Prop = v` and compound forms call the
                        // setter; this also covers object-initializer assignments.
                        var normalizedSetter = Accept(leftSymbol, selfNormalized);
                        if (normalizedSetter is not null)
                            yield return new RawCallSite(assignment, normalizedSetter);
                    }
                    else if (leftSymbol is IEventSymbol
                        && assignment.Kind() is SyntaxKind.AddAssignmentExpression or SyntaxKind.SubtractAssignmentExpression)
                    {
                        // 1b, in place: `SomeEvent += obj.Method;` — the handler is a callee at
                        // this site. Flagged so FindCalleesAsync can keep its trailing-pass order.
                        var handler = ResolveEventHandler(assignment.Right, semanticModel, selfNormalized, cancellationToken);
                        if (handler is not null)
                            yield return new RawCallSite(assignment, handler, isEventSubscription: true);
                    }

                    // Delegate variable assignments resolve to locals/fields and stay with BuildLocalDelegateMap.
                    break;
                }

                case ElementAccessExpressionSyntax elementAccess:
                {
                    // Item 6: indexer access resolves to the indexer property. Plain array
                    // element access binds to no symbol and falls through.
                    var elementInfo = semanticModel.GetSymbolInfo(elementAccess, cancellationToken);
                    var elementTarget = elementInfo.Symbol ?? elementInfo.CandidateSymbols.FirstOrDefault();
                    if (elementTarget is IPropertySymbol)
                    {
                        var normalizedIndexer = Accept(elementTarget, selfNormalized);
                        if (normalizedIndexer is not null)
                            yield return new RawCallSite(elementAccess, normalizedIndexer);
                    }
                    break;
                }

                case InitializerExpressionSyntax collectionInitializer:
                {
                    // Item 7: collection initializers — each element binds to an Add overload
                    // on the created collection.
                    foreach (var element in collectionInitializer.Expressions)
                    {
                        var addInfo = semanticModel.GetCollectionInitializerSymbolInfo(element, cancellationToken);
                        var normalizedAdd = Accept(addInfo.Symbol ?? addInfo.CandidateSymbols.FirstOrDefault(), selfNormalized);
                        if (normalizedAdd is not null)
                            yield return new RawCallSite(element, normalizedAdd);
                    }
                    break;
                }
            }
        }
    }

    // 1b: `SomeEvent += obj.Method;` / `-= obj.Method;` — the handler method becomes a callee.
    public IEnumerable<ISymbol> EnumerateEventSubscriptionHandlers(
        SyntaxNode body,
        SemanticModel semanticModel,
        ISymbol? selfNormalized,
        CancellationToken cancellationToken)
    {
        foreach (var assignment in body.DescendantNodesAndSelf().OfType<AssignmentExpressionSyntax>())
        {
            if (assignment.Kind() is not (SyntaxKind.AddAssignmentExpression or SyntaxKind.SubtractAssignmentExpression))
                continue;

            var leftInfo = semanticModel.GetSymbolInfo(assignment.Left, cancellationToken);
            if (leftInfo.Symbol is not IEventSymbol)
                continue;

            var handler = ResolveEventHandler(assignment.Right, semanticModel, selfNormalized, cancellationToken);
            if (handler is not null)
                yield return handler;
        }
    }

    /// <summary>
    /// Normalizes <paramref name="symbol"/> and returns it only when it is a supported callee
    /// and not the walked member itself.
    /// </summary>
    internal static ISymbol? Accept(ISymbol? symbol, ISymbol? selfNormalized)
    {
        var normalized = CallGraphSyntaxWalker.NormalizeSymbol(symbol);
        if (normalized is null || !CallGraphSyntaxWalker.IsSupportedCalleeSymbol(normalized))
            return null;
        if (SymbolEqualityComparer.Default.Equals(normalized, selfNormalized))
            return null;
        return normalized;
    }

    private static ISymbol? ResolveEventHandler(ExpressionSyntax right, SemanticModel semanticModel, ISymbol? selfNormalized, CancellationToken cancellationToken)
    {
        var rightExpr = UnwrapMethodGroupExpression(right);
        if (rightExpr is null)
            return null;

        var rightInfo = semanticModel.GetSymbolInfo(rightExpr, cancellationToken);
        var rightSymbol = rightInfo.Symbol ?? rightInfo.CandidateSymbols.FirstOrDefault();
        return Accept(rightSymbol, selfNormalized) is IMethodSymbol handlerMethod ? handlerMethod : null;
    }

    /// <summary>
    /// Yields every call-like syntax node reachable from <paramref name="root"/> in post-order
    /// (children before parent), matching C# evaluation order: invocations, object creations
    /// (constructor calls), constructor initializers (<c>: this/base(...)</c>), assignments
    /// (property setters and event subscriptions), element accesses (indexers), and collection
    /// initializers (Add calls).
    /// </summary>
    private static IEnumerable<SyntaxNode> GetCallSitesPostOrder(SyntaxNode root)
    {
        foreach (var child in root.ChildNodes())
            foreach (var site in GetCallSitesPostOrder(child))
                yield return site;

        switch (root)
        {
            case InvocationExpressionSyntax:
            case BaseObjectCreationExpressionSyntax:
            case ConstructorInitializerSyntax:
            case AssignmentExpressionSyntax:
            case ElementAccessExpressionSyntax:
                yield return root;
                break;
            case InitializerExpressionSyntax init when init.IsKind(SyntaxKind.CollectionInitializerExpression):
                yield return root;
                break;
        }
    }

    /// <summary>
    /// Peels explicit delegate creation (<c>new EventHandler(M)</c>), casts (<c>(Action)M</c>),
    /// and parentheses down to the underlying method-group expression (an identifier or member
    /// access). Returns null when the expression isn't, or doesn't wrap, a bare method group /
    /// simple member reference.
    /// </summary>
    private static ExpressionSyntax? UnwrapMethodGroupExpression(ExpressionSyntax? expr)
    {
        while (true)
        {
            switch (expr)
            {
                case ParenthesizedExpressionSyntax paren:
                    expr = paren.Expression;
                    continue;
                case CastExpressionSyntax cast:
                    expr = cast.Expression;
                    continue;
                case BaseObjectCreationExpressionSyntax creation when creation.ArgumentList?.Arguments.Count == 1:
                    expr = creation.ArgumentList.Arguments[0].Expression;
                    continue;
                case IdentifierNameSyntax or MemberAccessExpressionSyntax:
                    return expr;
                default:
                    return null;
            }
        }
    }

    // 1c/4/5: resolves a call made through a delegate VALUE rather than a method group:
    // `del(...)`, `del.Invoke(...)`, `del.BeginInvoke(...)`, `MyEvent(...)`, `MyEvent?.Invoke(...)`.
    // Returns the event symbol for raises (a raise is a call on the event itself), the tracked
    // method-group target for local/field delegate variables, or null when the invocation isn't
    // delegate-shaped or nothing better than the delegate's own method is known.
    private static ISymbol? TryResolveDelegateCallTarget(
        InvocationExpressionSyntax invocation,
        ISymbol? resolved,
        SemanticModel semanticModel,
        Dictionary<ISymbol, ISymbol> localDelegateMap,
        CancellationToken cancellationToken)
    {
        if (resolved is not IMethodSymbol method)
            return null;

        ExpressionSyntax? receiverExpr;
        if (method.MethodKind == MethodKind.DelegateInvoke)
        {
            receiverExpr = invocation.Expression switch
            {
                // `MyEvent?.Invoke(...)` — the receiver lives on the enclosing conditional access.
                MemberBindingExpressionSyntax => invocation.FirstAncestorOrSelf<ConditionalAccessExpressionSyntax>()?.Expression,
                // `del.Invoke(...)` / `MyEvent.Invoke(...)`
                MemberAccessExpressionSyntax ma when ma.Name.Identifier.ValueText == "Invoke" => ma.Expression,
                // `del(...)` / `MyEvent(...)` / `Some.Type.field(...)`
                var direct => direct,
            };
        }
        else if (method.Name is "BeginInvoke" or "EndInvoke" or "DynamicInvoke")
        {
            receiverExpr = invocation.Expression switch
            {
                MemberAccessExpressionSyntax ma => ma.Expression,
                MemberBindingExpressionSyntax => invocation.FirstAncestorOrSelf<ConditionalAccessExpressionSyntax>()?.Expression,
                _ => null,
            };

            // Guard against unrelated methods with the same names (e.g. Control.BeginInvoke).
            if (receiverExpr is null || semanticModel.GetTypeInfo(receiverExpr, cancellationToken).Type?.TypeKind != TypeKind.Delegate)
                return null;
        }
        else
        {
            return null;
        }

        if (receiverExpr is null)
            return null;

        var receiverInfo = semanticModel.GetSymbolInfo(receiverExpr, cancellationToken);
        var receiver = receiverInfo.Symbol ?? receiverInfo.CandidateSymbols.FirstOrDefault();

        if (receiver is IEventSymbol)
            return receiver.OriginalDefinition;

        var normalizedReceiver = CallGraphSyntaxWalker.NormalizeSymbol(receiver);
        if (normalizedReceiver != null && localDelegateMap.TryGetValue(normalizedReceiver, out var target))
            return target;

        return null;
    }

    // 1a: for each argument that is a method group — bare, delegate-creation-wrapped
    // (`new ThreadStart(Run)`), or cast-wrapped (`(Action)Run`) — resolve it and, if the
    // corresponding parameter's type is a delegate type, treat it as a deferred call. Delegate
    // variables tracked by BuildLocalDelegateMap redirect to their assigned target the same way.
    // General rule — covers Action<T...>, Func<T...>, EventHandler, ThreadStart, WaitCallback,
    // TimerCallback, and any custom delegate type; not a hardcoded method-name list. Shared by
    // ordinary invocations and constructor calls (e.g. `new Thread(Run)`).
    // Named/reordered arguments are matched positionally — a known, accepted limitation.
    private static IEnumerable<ISymbol> GetMethodGroupArgumentCallees(
        IMethodSymbol invokedMethod,
        SeparatedSyntaxList<ArgumentSyntax> args,
        SemanticModel semanticModel,
        ISymbol? selfNormalized,
        Dictionary<ISymbol, ISymbol> localDelegateMap,
        CancellationToken cancellationToken)
    {
        var parameters = invokedMethod.Parameters;

        for (int i = 0; i < args.Count; i++)
        {
            var argExpr = UnwrapMethodGroupExpression(args[i].Expression);
            if (argExpr is null)
                continue;

            IParameterSymbol? param = i < parameters.Length
                ? parameters[i]
                : (parameters.Length > 0 && parameters[parameters.Length - 1].IsParams ? parameters[parameters.Length - 1] : null);

            if (param is null || param.Type.TypeKind != TypeKind.Delegate)
                continue;

            var argSymbolInfo = semanticModel.GetSymbolInfo(argExpr, cancellationToken);
            var argSymbol = argSymbolInfo.Symbol ?? argSymbolInfo.CandidateSymbols.FirstOrDefault();
            var normalizedArg = CallGraphSyntaxWalker.NormalizeSymbol(argSymbol);

            ISymbol? target = normalizedArg switch
            {
                IMethodSymbol methodGroupTarget when CallGraphSyntaxWalker.IsSupportedCalleeSymbol(methodGroupTarget) => methodGroupTarget,
                not null when localDelegateMap.TryGetValue(normalizedArg, out var redirected) => redirected,
                _ => null,
            };

            if (target is null || SymbolEqualityComparer.Default.Equals(target, selfNormalized))
                continue;

            yield return target;
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
            var valueExpr = UnwrapMethodGroupExpression(declarator.Initializer?.Value);
            if (valueExpr is null)
                continue;

            var declaredSymbol = semanticModel.GetDeclaredSymbol(declarator, cancellationToken) as ILocalSymbol;
            if (declaredSymbol is null || declaredSymbol.Type.TypeKind != TypeKind.Delegate)
                continue;

            var valueInfo = semanticModel.GetSymbolInfo(valueExpr, cancellationToken);
            var valueSymbol = valueInfo.Symbol ?? valueInfo.CandidateSymbols.FirstOrDefault();
            var normalizedValue = CallGraphSyntaxWalker.NormalizeSymbol(valueSymbol);
            if (normalizedValue is IMethodSymbol methodGroup && CallGraphSyntaxWalker.IsSupportedCalleeSymbol(methodGroup))
                map[declaredSymbol] = methodGroup;
        }

        foreach (var assignment in root.DescendantNodesAndSelf().OfType<AssignmentExpressionSyntax>())
        {
            if (assignment.Kind() != SyntaxKind.SimpleAssignmentExpression)
                continue;

            var valueExpr = UnwrapMethodGroupExpression(assignment.Right);
            if (valueExpr is null)
                continue;

            var leftInfo = semanticModel.GetSymbolInfo(assignment.Left, cancellationToken);
            ITypeSymbol? leftType = leftInfo.Symbol switch
            {
                ILocalSymbol local => local.Type,
                IFieldSymbol field => field.Type,
                _ => null,
            };

            if (leftType is null || leftType.TypeKind != TypeKind.Delegate || leftInfo.Symbol is null)
                continue;

            var valueInfo = semanticModel.GetSymbolInfo(valueExpr, cancellationToken);
            var valueSymbol = valueInfo.Symbol ?? valueInfo.CandidateSymbols.FirstOrDefault();
            var normalizedValue = CallGraphSyntaxWalker.NormalizeSymbol(valueSymbol);
            if (normalizedValue is IMethodSymbol methodGroup && CallGraphSyntaxWalker.IsSupportedCalleeSymbol(methodGroup))
                map[leftInfo.Symbol] = methodGroup;
        }

        return map;
    }
}
