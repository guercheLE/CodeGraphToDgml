using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.VisualBasic;
using Microsoft.CodeAnalysis.VisualBasic.Syntax;

namespace CodeGraphToDgml.Roslyn.CallSites;

/// <summary>
/// Visual Basic call-site discovery, mirroring <see cref="CSharpCallSiteWalker"/>: invocations
/// (with or without parentheses, <c>Call</c> statements, default/indexed properties, delegate
/// <c>Invoke</c>/<c>BeginInvoke</c>), <c>New</c> expressions (including <c>As New</c>),
/// <c>MyBase.New</c>/<c>Me.New</c> chaining, property writes (assignments and
/// <c>With {.P = v}</c> initializers), <c>From {...}</c> collection initializers,
/// <c>RaiseEvent</c>, <c>AddHandler</c>/<c>RemoveHandler</c>, <c>AddressOf</c> method groups
/// passed to delegate parameters, and delegate variables invoked or passed onward within the
/// same body. <c>Handles</c> clauses are declarations, not calls, and are not reported.
/// </summary>
internal sealed class VisualBasicCallSiteWalker : ICallSiteWalker
{
    public static readonly VisualBasicCallSiteWalker Instance = new();

    private VisualBasicCallSiteWalker()
    {
    }

    /// <summary>
    /// A VB symbol's declaring syntax reference is the header statement (<c>Sub Foo()</c>,
    /// <c>Property P As Integer</c>, <c>Custom Event E</c>); the body lives in the parent block.
    /// </summary>
    public SyntaxNode GetBodyNode(SyntaxNode declaringNode)
    {
        if (declaringNode is MethodBaseSyntax
            && declaringNode.Parent is MethodBlockBaseSyntax or PropertyBlockSyntax or EventBlockSyntax)
        {
            return declaringNode.Parent;
        }

        return declaringNode;
    }

    public IEnumerable<RawCallSite> EnumerateCallSites(
        SyntaxNode body,
        SemanticModel semanticModel,
        ISymbol? selfNormalized,
        CancellationToken cancellationToken)
    {
        var localDelegateMap = BuildLocalDelegateMap(body, semanticModel, cancellationToken);

        // Resolved symbol per call expression (parenthesized or implicit), for fluent chains.
        var callSymbolOf = new Dictionary<SyntaxNode, ISymbol>();

        foreach (var site in GetCallSitesPostOrder(body))
        {
            cancellationToken.ThrowIfCancellationRequested();

            switch (site)
            {
                case InvocationExpressionSyntax invocation:
                {
                    foreach (var raw in HandleCall(invocation, invocation.Expression, invocation.ArgumentList, allowProperty: true, semanticModel, selfNormalized, localDelegateMap, callSymbolOf, cancellationToken))
                        yield return raw;
                    break;
                }

                case IdentifierNameSyntax or MemberAccessExpressionSyntax when IsImplicitCallCandidate(site):
                {
                    // Parenless call: `obj.Method` / `Method` as a statement or inside an
                    // expression. Only method symbols count; property reads are not calls.
                    foreach (var raw in HandleCall(site, (ExpressionSyntax)site, argumentList: null, allowProperty: false, semanticModel, selfNormalized, localDelegateMap, callSymbolOf, cancellationToken))
                        yield return raw;
                    break;
                }

                case ObjectCreationExpressionSyntax creation:
                {
                    var creationInfo = semanticModel.GetSymbolInfo(creation, cancellationToken);
                    var ctor = (creationInfo.Symbol ?? (creationInfo.CandidateSymbols.Length == 1 ? creationInfo.CandidateSymbols[0] : null)) as IMethodSymbol;
                    var normalizedCtor = CSharpCallSiteWalker.Accept(ctor, selfNormalized);
                    if (normalizedCtor is not null)
                        yield return new RawCallSite(creation, normalizedCtor);

                    if (ctor is not null && creation.ArgumentList is { } ctorArgs)
                    {
                        foreach (var argCallee in GetMethodGroupArgumentCallees(ctor, ctorArgs.Arguments, semanticModel, selfNormalized, localDelegateMap, cancellationToken))
                            yield return new RawCallSite(creation, argCallee);
                    }

                    break;
                }

                case AssignmentStatementSyntax assignment:
                {
                    var leftInfo = semanticModel.GetSymbolInfo(assignment.Left, cancellationToken);
                    var leftSymbol = leftInfo.Symbol ?? leftInfo.CandidateSymbols.FirstOrDefault();
                    if (leftSymbol is IPropertySymbol)
                    {
                        var normalizedSetter = CSharpCallSiteWalker.Accept(leftSymbol, selfNormalized);
                        if (normalizedSetter is not null)
                            yield return new RawCallSite(assignment, normalizedSetter);
                    }
                    break;
                }

                case NamedFieldInitializerSyntax fieldInitializer:
                {
                    // `New T With {.Prop = value}` — a setter call per initialized member.
                    var nameInfo = semanticModel.GetSymbolInfo(fieldInitializer.Name, cancellationToken);
                    var member = nameInfo.Symbol ?? nameInfo.CandidateSymbols.FirstOrDefault();
                    if (member is IPropertySymbol)
                    {
                        var normalizedSetter = CSharpCallSiteWalker.Accept(member, selfNormalized);
                        if (normalizedSetter is not null)
                            yield return new RawCallSite(fieldInitializer, normalizedSetter);
                    }
                    break;
                }

                case CollectionInitializerSyntax collectionInitializer when collectionInitializer.Parent is ObjectCollectionInitializerSyntax:
                {
                    // `New List(Of Integer) From {1, 2}` — each element binds to an Add overload.
                    foreach (var element in collectionInitializer.Initializers)
                    {
                        var addInfo = semanticModel.GetCollectionInitializerSymbolInfo(element, cancellationToken);
                        var normalizedAdd = CSharpCallSiteWalker.Accept(addInfo.Symbol ?? addInfo.CandidateSymbols.FirstOrDefault(), selfNormalized);
                        if (normalizedAdd is not null)
                            yield return new RawCallSite(element, normalizedAdd);
                    }
                    break;
                }

                case RaiseEventStatementSyntax raiseEvent:
                {
                    // `RaiseEvent Changed(Me, EventArgs.Empty)` — a call on the event itself,
                    // matching how C# `Changed?.Invoke(...)` is reported.
                    var eventInfo = semanticModel.GetSymbolInfo(raiseEvent.Name, cancellationToken);
                    if ((eventInfo.Symbol ?? eventInfo.CandidateSymbols.FirstOrDefault()) is IEventSymbol raisedEvent)
                    {
                        var normalizedEvent = CSharpCallSiteWalker.Accept(raisedEvent, selfNormalized);
                        if (normalizedEvent is not null)
                            yield return new RawCallSite(raiseEvent, normalizedEvent);
                    }
                    break;
                }

                case AddRemoveHandlerStatementSyntax handlerStatement:
                {
                    var handler = ResolveSubscriptionHandler(handlerStatement, semanticModel, selfNormalized, cancellationToken);
                    if (handler is not null)
                        yield return new RawCallSite(handlerStatement, handler, isEventSubscription: true);
                    break;
                }
            }
        }
    }

    public IEnumerable<ISymbol> EnumerateEventSubscriptionHandlers(
        SyntaxNode body,
        SemanticModel semanticModel,
        ISymbol? selfNormalized,
        CancellationToken cancellationToken)
    {
        foreach (var handlerStatement in body.DescendantNodesAndSelf().OfType<AddRemoveHandlerStatementSyntax>())
        {
            var handler = ResolveSubscriptionHandler(handlerStatement, semanticModel, selfNormalized, cancellationToken);
            if (handler is not null)
                yield return handler;
        }
    }

    private static IEnumerable<RawCallSite> HandleCall(
        SyntaxNode site,
        ExpressionSyntax target,
        ArgumentListSyntax? argumentList,
        bool allowProperty,
        SemanticModel semanticModel,
        ISymbol? selfNormalized,
        Dictionary<ISymbol, ISymbol> localDelegateMap,
        Dictionary<SyntaxNode, ISymbol> callSymbolOf,
        CancellationToken cancellationToken)
    {
        var symbolInfo = semanticModel.GetSymbolInfo(site, cancellationToken);
        var resolved = symbolInfo.Symbol ?? (symbolInfo.CandidateSymbols.Length == 1 ? symbolInfo.CandidateSymbols[0] : null);

        // A parenless name is a call only when it binds to an ordinary method; property and
        // event reads, constructors, and delegate members are not calls in that position.
        if (!allowProperty && resolved is not IMethodSymbol { MethodKind: MethodKind.Ordinary or MethodKind.ReducedExtension or MethodKind.DeclareMethod })
            yield break;

        var invokedForArgs = resolved as IMethodSymbol;

        var delegateTarget = TryResolveDelegateCallTarget(site, target, resolved, semanticModel, localDelegateMap, cancellationToken);
        bool isDelegateRedirect = delegateTarget is not null;
        resolved = delegateTarget ?? resolved;

        var normalized = CSharpCallSiteWalker.Accept(resolved, selfNormalized);
        if (normalized is null)
            yield break;

        ISymbol? fluentReceiver = null;
        if (target is MemberAccessExpressionSyntax { Expression: { } receiverExpression }
            && callSymbolOf.TryGetValue(receiverExpression, out var receiverSymbol))
        {
            fluentReceiver = receiverSymbol;
        }

        callSymbolOf[site] = normalized;

        var expr = isDelegateRedirect && target is MemberAccessExpressionSyntax delegateMember
            ? delegateMember.Expression
            : target;

        var chainedBefore = new List<ISymbol>();
        while (expr is MemberAccessExpressionSyntax memberAccess)
        {
            var memberInfo = semanticModel.GetSymbolInfo(memberAccess, cancellationToken);
            var memberResolved = memberInfo.Symbol ?? (memberInfo.CandidateSymbols.Length == 1 ? memberInfo.CandidateSymbols[0] : null);
            var memberNormalized = CSharpCallSiteWalker.Accept(memberResolved, selfNormalized);
            if (memberNormalized is not null && !SymbolEqualityComparer.Default.Equals(memberNormalized, normalized))
                chainedBefore.Add(memberNormalized);

            expr = memberAccess.Expression;
        }

        chainedBefore.Reverse();
        foreach (var chained in chainedBefore)
            yield return new RawCallSite(site, chained);

        yield return new RawCallSite(site, normalized, fluentReceiver);

        if (invokedForArgs is not null && argumentList is not null)
        {
            foreach (var argCallee in GetMethodGroupArgumentCallees(invokedForArgs, argumentList.Arguments, semanticModel, selfNormalized, localDelegateMap, cancellationToken))
                yield return new RawCallSite(site, argCallee);
        }
    }

    /// <summary>
    /// A name expression that may be a parenless method call: not the target of an explicit
    /// invocation, not the member name of an enclosing member access (that access is the site),
    /// not an <c>AddressOf</c> operand, and not part of a declaration clause.
    /// </summary>
    private static bool IsImplicitCallCandidate(SyntaxNode node)
    {
        return node.Parent switch
        {
            InvocationExpressionSyntax invocation when invocation.Expression == node => false,
            MemberAccessExpressionSyntax memberAccess when memberAccess.Name == node => false,
            UnaryExpressionSyntax unary when unary.IsKind(SyntaxKind.AddressOfExpression) => false,
            QualifiedNameSyntax => false,
            NameColonEqualsSyntax => false,
            NamedFieldInitializerSyntax fieldInitializer when fieldInitializer.Name == node => false,
            RaiseEventStatementSyntax => false,
            HandlesClauseItemSyntax or ImplementsClauseSyntax => false,
            AttributeSyntax or NameOfExpressionSyntax or TypeArgumentListSyntax => false,
            SimpleAsClauseSyntax or AsNewClauseSyntax => false,
            _ => true,
        };
    }

    private static ISymbol? ResolveSubscriptionHandler(
        AddRemoveHandlerStatementSyntax handlerStatement,
        SemanticModel semanticModel,
        ISymbol? selfNormalized,
        CancellationToken cancellationToken)
    {
        var eventInfo = semanticModel.GetSymbolInfo(handlerStatement.EventExpression, cancellationToken);
        if ((eventInfo.Symbol ?? eventInfo.CandidateSymbols.FirstOrDefault()) is not IEventSymbol)
            return null;

        var handlerExpr = UnwrapMethodGroupExpression(handlerStatement.DelegateExpression);
        if (handlerExpr is null)
            return null;

        var handlerInfo = semanticModel.GetSymbolInfo(handlerExpr, cancellationToken);
        var handlerSymbol = handlerInfo.Symbol ?? handlerInfo.CandidateSymbols.FirstOrDefault();
        return CSharpCallSiteWalker.Accept(handlerSymbol, selfNormalized) is IMethodSymbol handler ? handler : null;
    }

    /// <summary>
    /// Post-order walk of a member block. Header statements (<c>Sub Foo(...) Implements ...
    /// Handles ...</c>) are skipped except for a property header's initializer / <c>As New</c>
    /// clause; attribute lists are skipped.
    /// </summary>
    private static IEnumerable<SyntaxNode> GetCallSitesPostOrder(SyntaxNode root)
    {
        if (root is AttributeListSyntax)
            yield break;

        if (root is MethodBaseSyntax header)
        {
            if (header is PropertyStatementSyntax propertyHeader)
            {
                if (propertyHeader.AsClause is { } asClause)
                    foreach (var site in GetCallSitesPostOrder(asClause))
                        yield return site;

                if (propertyHeader.Initializer is { } initializer)
                    foreach (var site in GetCallSitesPostOrder(initializer))
                        yield return site;
            }

            yield break;
        }

        foreach (var child in root.ChildNodes())
            foreach (var site in GetCallSitesPostOrder(child))
                yield return site;

        switch (root)
        {
            case InvocationExpressionSyntax:
            case IdentifierNameSyntax:
            case MemberAccessExpressionSyntax:
            case ObjectCreationExpressionSyntax:
            case AssignmentStatementSyntax:
            case NamedFieldInitializerSyntax:
            case CollectionInitializerSyntax:
            case RaiseEventStatementSyntax:
            case AddRemoveHandlerStatementSyntax:
                yield return root;
                break;
        }
    }

    /// <summary>
    /// Peels <c>AddressOf</c>, delegate creation (<c>New EventHandler(AddressOf M)</c>), casts
    /// (<c>CType</c>/<c>DirectCast</c>/<c>TryCast</c>), and parentheses down to the underlying
    /// name expression.
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
                case ObjectCreationExpressionSyntax creation when creation.ArgumentList?.Arguments.Count == 1:
                    expr = creation.ArgumentList.Arguments[0].GetExpression();
                    continue;
                case UnaryExpressionSyntax addressOf when addressOf.IsKind(SyntaxKind.AddressOfExpression):
                    expr = addressOf.Operand;
                    continue;
                case IdentifierNameSyntax or MemberAccessExpressionSyntax:
                    return expr;
                default:
                    return null;
            }
        }
    }

    private static ISymbol? TryResolveDelegateCallTarget(
        SyntaxNode site,
        ExpressionSyntax target,
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
            receiverExpr = target switch
            {
                // `d?.Invoke(...)` or `.Invoke(...)` inside a With block.
                MemberAccessExpressionSyntax { Expression: null } => ImplicitReceiverOf(site),
                MemberAccessExpressionSyntax ma when ma.Name.Identifier.ValueText.Equals("Invoke", System.StringComparison.OrdinalIgnoreCase) => ma.Expression,
                var direct => direct,
            };
        }
        else if (method.Name is "BeginInvoke" or "EndInvoke" or "DynamicInvoke")
        {
            receiverExpr = target switch
            {
                MemberAccessExpressionSyntax { Expression: null } => ImplicitReceiverOf(site),
                MemberAccessExpressionSyntax ma => ma.Expression,
                _ => null,
            };

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
        if (normalizedReceiver != null && localDelegateMap.TryGetValue(normalizedReceiver, out var redirected))
            return redirected;

        return null;
    }

    private static ExpressionSyntax? ImplicitReceiverOf(SyntaxNode site)
    {
        return site.FirstAncestorOrSelf<ConditionalAccessExpressionSyntax>()?.Expression
            ?? site.FirstAncestorOrSelf<WithBlockSyntax>()?.WithStatement.Expression;
    }

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
            var argExpr = UnwrapMethodGroupExpression(args[i].GetExpression());
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
                not null when localDelegateMap.TryGetValue(normalizedArg, out var redirectedTarget) => redirectedTarget,
                _ => null,
            };

            if (target is null || SymbolEqualityComparer.Default.Equals(target, selfNormalized))
                continue;

            yield return target;
        }
    }

    private static Dictionary<ISymbol, ISymbol> BuildLocalDelegateMap(
        SyntaxNode root,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        var map = new Dictionary<ISymbol, ISymbol>(SymbolEqualityComparer.Default);

        foreach (var declarator in root.DescendantNodesAndSelf().OfType<VariableDeclaratorSyntax>())
        {
            ExpressionSyntax? valueSource = declarator.Initializer?.Value
                ?? (declarator.AsClause as AsNewClauseSyntax)?.NewExpression;
            var valueExpr = UnwrapMethodGroupExpression(valueSource);
            if (valueExpr is null)
                continue;

            var valueInfo = semanticModel.GetSymbolInfo(valueExpr, cancellationToken);
            var valueSymbol = valueInfo.Symbol ?? valueInfo.CandidateSymbols.FirstOrDefault();
            if (CallGraphSyntaxWalker.NormalizeSymbol(valueSymbol) is not IMethodSymbol methodGroup || !CallGraphSyntaxWalker.IsSupportedCalleeSymbol(methodGroup))
                continue;

            foreach (var name in declarator.Names)
            {
                var declared = semanticModel.GetDeclaredSymbol(name, cancellationToken);
                ITypeSymbol? declaredType = declared switch
                {
                    ILocalSymbol local => local.Type,
                    IFieldSymbol field => field.Type,
                    _ => null,
                };

                if (declared is not null && declaredType?.TypeKind == TypeKind.Delegate)
                    map[declared] = methodGroup;
            }
        }

        foreach (var assignment in root.DescendantNodesAndSelf().OfType<AssignmentStatementSyntax>())
        {
            if (assignment.Kind() != SyntaxKind.SimpleAssignmentStatement)
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
            if (CallGraphSyntaxWalker.NormalizeSymbol(valueSymbol) is IMethodSymbol methodGroup && CallGraphSyntaxWalker.IsSupportedCalleeSymbol(methodGroup))
                map[leftInfo.Symbol] = methodGroup;
        }

        return map;
    }
}
