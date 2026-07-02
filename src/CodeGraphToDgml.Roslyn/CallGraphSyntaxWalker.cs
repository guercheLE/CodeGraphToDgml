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
    /// Peels explicit delegate creation (<c>new EventHandler(M)</c>), casts (<c>(Action)M</c>),
    /// and parentheses down to the underlying method-group expression (an identifier or member
    /// access). Returns null when the expression isn't, or doesn't wrap, a bare method group /
    /// simple member reference. Callers remain responsible for validating that the surrounding
    /// slot (parameter, variable, event) is delegate-typed.
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
    /// direct invocations, chained member-access invocations, constructor calls (including
    /// <c>: this/base(...)</c> chaining), property writes and indexer accesses, collection
    /// initializer <c>Add</c> calls, event raises, method groups passed to delegate-typed
    /// parameters (bare, delegate-creation-wrapped, or cast-wrapped), event subscriptions
    /// (<c>+=</c>/<c>-=</c>), and delegate variables/fields invoked (or passed onward)
    /// within the same method body.
    /// </summary>
    public static async Task<IReadOnlyList<CalleeInfo>> FindCalleesAsync(
        ISymbol symbol,
        RoslynSolution solution,
        CancellationToken cancellationToken)
    {
        var callees = new List<CalleeInfo>();
        var seen = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
        var selfNormalized = NormalizeSymbol(symbol);

        // Shared add path for call sites without fluent-chain metadata (constructors,
        // property writes, indexers, collection-initializer Adds, ctor chaining).
        void AddSimpleCallee(ISymbol? normalized)
        {
            if (normalized is null || !IsSupportedCalleeSymbol(normalized))
                return;
            if (SymbolEqualityComparer.Default.Equals(normalized, selfNormalized))
                return;
            if (seen.Add(normalized))
                callees.Add(new CalleeInfo(normalized));
        }

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

            foreach (var site in GetCallSitesPostOrder(syntaxNode))
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

                        if (resolved is null)
                            break;

                        var normalized = NormalizeSymbol(resolved);
                        if (normalized is null || !IsSupportedCalleeSymbol(normalized))
                            break;

                        // Skip self-references.
                        if (SymbolEqualityComparer.Default.Equals(normalized, selfNormalized))
                            break;

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
                        // When the call was resolved as a delegate call, its own member access
                        // (`a.BeginInvoke`, `del.Invoke`) is delegate machinery, not a chained
                        // member — start the walk below it.
                        var chainedBefore = new List<ISymbol>();
                        var expr = isDelegateRedirect && invocation.Expression is MemberAccessExpressionSyntax delegateMember
                            ? delegateMember.Expression
                            : invocation.Expression;
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

                        // 1a: method groups (and tracked delegate variables) passed as arguments
                        // to delegate-typed parameters.
                        if (invokedForArgs is not null)
                        {
                            foreach (var argCallee in GetMethodGroupArgumentCallees(invokedForArgs, invocation.ArgumentList.Arguments, semanticModel, selfNormalized, seen, localDelegateMap, cancellationToken))
                            {
                                callees.Add(new CalleeInfo(argCallee));
                            }
                        }

                        break;
                    }

                    case BaseObjectCreationExpressionSyntax creation:
                    {
                        // Item 3: `new Foo(...)` (and target-typed `new(...)`) — the constructor
                        // is a callee like any method call.
                        var creationInfo = semanticModel.GetSymbolInfo(creation, cancellationToken);
                        var ctor = (creationInfo.Symbol ?? (creationInfo.CandidateSymbols.Length == 1 ? creationInfo.CandidateSymbols[0] : null)) as IMethodSymbol;
                        AddSimpleCallee(NormalizeSymbol(ctor));

                        // Method groups passed to constructor parameters, e.g. `new Thread(Run)`.
                        if (ctor is not null && creation.ArgumentList is { } ctorArgs)
                        {
                            foreach (var argCallee in GetMethodGroupArgumentCallees(ctor, ctorArgs.Arguments, semanticModel, selfNormalized, seen, localDelegateMap, cancellationToken))
                            {
                                callees.Add(new CalleeInfo(argCallee));
                            }
                        }

                        break;
                    }

                    case ConstructorInitializerSyntax ctorInitializer:
                    {
                        // Item 3: `: this(...)` / `: base(...)` chaining when the traversed
                        // symbol is itself a constructor.
                        var initInfo = semanticModel.GetSymbolInfo(ctorInitializer, cancellationToken);
                        AddSimpleCallee(NormalizeSymbol(initInfo.Symbol ?? initInfo.CandidateSymbols.FirstOrDefault()));
                        break;
                    }

                    case AssignmentExpressionSyntax assignment:
                    {
                        // Item 6: property writes — `x.Prop = v` and compound forms call the
                        // setter; this also covers object-initializer assignments. Event +=/-=
                        // subscriptions resolve to IEventSymbol and stay with their own pass
                        // (GetEventSubscriptionCallees); delegate variable assignments resolve to
                        // locals/fields and stay with BuildLocalDelegateMap.
                        var leftInfo = semanticModel.GetSymbolInfo(assignment.Left, cancellationToken);
                        var leftSymbol = leftInfo.Symbol ?? leftInfo.CandidateSymbols.FirstOrDefault();
                        if (leftSymbol is IPropertySymbol)
                            AddSimpleCallee(NormalizeSymbol(leftSymbol));
                        break;
                    }

                    case ElementAccessExpressionSyntax elementAccess:
                    {
                        // Item 6: indexer access resolves to the indexer property. Plain array
                        // element access binds to no symbol and falls through.
                        var elementInfo = semanticModel.GetSymbolInfo(elementAccess, cancellationToken);
                        var elementTarget = elementInfo.Symbol ?? elementInfo.CandidateSymbols.FirstOrDefault();
                        if (elementTarget is IPropertySymbol)
                            AddSimpleCallee(NormalizeSymbol(elementTarget));
                        break;
                    }

                    case InitializerExpressionSyntax collectionInitializer:
                    {
                        // Item 7: collection initializers — each element binds to an Add overload
                        // on the created collection.
                        foreach (var element in collectionInitializer.Expressions)
                        {
                            var addInfo = semanticModel.GetCollectionInitializerSymbolInfo(element, cancellationToken);
                            AddSimpleCallee(NormalizeSymbol(addInfo.Symbol ?? addInfo.CandidateSymbols.FirstOrDefault()));
                        }
                        break;
                    }
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

    /// <summary>
    /// Yields every call-like syntax node reachable from <paramref name="root"/> in post-order
    /// (children before parent), matching C# evaluation order: invocations, object creations
    /// (constructor calls), constructor initializers (<c>: this/base(...)</c>), assignments
    /// (property setters), element accesses (indexers), and collection initializers (Add calls).
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

        var normalizedReceiver = NormalizeSymbol(receiver);
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
        HashSet<ISymbol> seen,
        Dictionary<ISymbol, ISymbol> localDelegateMap,
        CancellationToken cancellationToken)
    {
        var parameters = invokedMethod.Parameters;

        for (int i = 0; i < args.Count; i++)
        {
            var argExpr = UnwrapMethodGroupExpression(args[i].Expression);
            if (argExpr is null)
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

            var argSymbolInfo = semanticModel.GetSymbolInfo(argExpr, cancellationToken);
            var argSymbol = argSymbolInfo.Symbol ?? argSymbolInfo.CandidateSymbols.FirstOrDefault();
            var normalizedArg = NormalizeSymbol(argSymbol);

            ISymbol? target = normalizedArg switch
            {
                IMethodSymbol methodGroupTarget when IsSupportedCalleeSymbol(methodGroupTarget) => methodGroupTarget,
                not null when localDelegateMap.TryGetValue(normalizedArg, out var redirected) => redirected,
                _ => null,
            };

            if (target is null || SymbolEqualityComparer.Default.Equals(target, selfNormalized))
            {
                continue;
            }

            if (seen.Add(target))
            {
                yield return target;
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

            var rightExpr = UnwrapMethodGroupExpression(assignment.Right);
            if (rightExpr is null)
            {
                continue;
            }

            var rightInfo = semanticModel.GetSymbolInfo(rightExpr, cancellationToken);
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
            var valueExpr = UnwrapMethodGroupExpression(declarator.Initializer?.Value);
            if (valueExpr is null)
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

            var valueExpr = UnwrapMethodGroupExpression(assignment.Right);
            if (valueExpr is null)
            {
                continue;
            }

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
