using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.CodeAnalysis.Host.Mef;
using CodeGraphToDgml.Core;
using CodeGraphToDgml.Roslyn;
using EnvDTE;
using EnvDTE80;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.VisualStudio.ComponentModelHost;
using RoslynDocument = Microsoft.CodeAnalysis.Document;
using RoslynSolution = Microsoft.CodeAnalysis.Solution;
using static CodeGraphToDgml.Vsix.Providers.RoslynGraphHelpers;

namespace CodeGraphToDgml.Vsix.Providers;

internal sealed class RoslynCallHierarchyProvider : IHierarchyProvider
{
    private static readonly HashSet<string> SupportedLanguages = new(StringComparer.Ordinal)
    {
        LanguageNames.CSharp,
        LanguageNames.VisualBasic,
    };

    private readonly ToolkitPackage _package;
    private readonly OutputWindowLogger _logger;

    public RoslynCallHierarchyProvider(ToolkitPackage package)
    {
        _package = package;
        _logger = new OutputWindowLogger(package);
    }

    public string Id => "Roslyn";

    public bool CouldSupport(string? filePath)
    {
        var extension = Path.GetExtension(filePath ?? string.Empty);
        return extension.Equals(".cs", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".vb", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<HierarchySubject?> TryResolveSubjectAsync(CancellationToken cancellationToken)
    {

        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

        var dte = await _package.GetServiceAsync(typeof(DTE)).ConfigureAwait(true) as DTE2;
        var activeDocument = dte?.ActiveDocument;
        if (activeDocument is null || !CouldSupport(activeDocument.FullName))
        {
            return null;
        }

        if (activeDocument.Selection is not TextSelection selection)
        {
            return null;
        }

        int activeLine = selection.ActivePoint.Line;
        int activeColumn = selection.ActivePoint.LineCharOffset;

        var workspace = await GetWorkspaceAsync(cancellationToken).ConfigureAwait(true);
        if (workspace is null)
        {
            return null;
        }

        var document = GetDocument(workspace.CurrentSolution, activeDocument.FullName);
        if (document is null)
        {
            return null;
        }

        var position = await RoslynGraphHelpers.GetPositionFromLinesAsync(document, activeLine, activeColumn, cancellationToken).ConfigureAwait(false);

        await _logger.WriteLineAsync($"[ResolveSubject] Position={position}, File={activeDocument.FullName}").ConfigureAwait(false);

        var symbol = await ResolveSymbolAsync(document, workspace, position, cancellationToken).ConfigureAwait(false);

        await _logger.WriteLineAsync($"[ResolveSubject] Raw resolved symbol: {symbol?.ToDisplayString() ?? "(null)"}, Kind={symbol?.Kind}").ConfigureAwait(false);

        symbol = CallGraphSyntaxWalker.NormalizeSymbol(symbol);

        await _logger.WriteLineAsync($"[ResolveSubject] Normalized symbol: {symbol?.ToDisplayString() ?? "(null)"}, Kind={symbol?.Kind}").ConfigureAwait(false);

        if (symbol is null || !CallGraphFilters.IsSupportedRootSymbol(symbol))
        {
            await _logger.WriteLineAsync($"[ResolveSubject] Symbol rejected: not a supported root symbol.").ConfigureAwait(false);
            return null;
        }

        await _logger.WriteLineAsync($"[ResolveSubject] Entry point resolved: {symbol.ToDisplayString()}").ConfigureAwait(false);

        return new HierarchySubject(
            Id,
            symbol.ToDisplayString(),
            new RoslynHierarchySubject(symbol, workspace.CurrentSolution));
    }

    public async Task<TraversalGraph> TraverseUpAsync(
        HierarchySubject subject,
        TraversalOptions options,
        IProgress<TraversalProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (subject.Payload is not RoslynHierarchySubject roslynSubject)
        {
            throw new InvalidOperationException("Unexpected hierarchy subject payload.");
        }

        var normalizedOptions = options.Normalize();
        var graph = new TraversalGraph();
        var queuedIds = new HashSet<string>(StringComparer.Ordinal);
        var queue = new Queue<(ISymbol Symbol, int Depth)>();
        var processedComponentTypes = new HashSet<ISymbol>(SymbolEqualityComparer.Default);

        Enqueue(roslynSubject.Symbol, 0);

        if (graph.NodeCount >= normalizedOptions.MaxNodeCount)
        {
            return graph;
        }

        while (queue.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var (current, depth) = queue.Dequeue();
            progress?.Report(new TraversalProgress(
                "Traversing callers",
                depth,
                graph.NodeCount,
                current.ToDisplayString()));

            if (depth >= normalizedOptions.MaxDepth)
            {
                continue;
            }

            var callers = await SymbolFinder.FindCallersAsync(
                current,
                roslynSubject.Solution,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            foreach (var callerInfo in callers)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // If the call site lives inside a lambda or local function, walk up to the
                // enclosing real method/property/event so the graph never contains
                // anonymous/empty-named nodes. Inner calls of the lambda are linked
                // directly from the enclosing member.
                var caller = CallGraphSyntaxWalker.NormalizeSymbol(CallGraphSyntaxWalker.UnwrapLambdaContainer(callerInfo.CallingSymbol));
                if (caller is null || !CallGraphFilters.IsAllowed(caller, normalizedOptions))
                {
                    continue;
                }

                var currentCallee = CallGraphSyntaxWalker.NormalizeSymbol(current);
                var calledSymbol = CallGraphSyntaxWalker.NormalizeSymbol(callerInfo.CalledSymbol);

                var callerProject = caller.ContainingAssembly?.Name;
                AddNodeAndContainers(graph, caller, callerProject, roslynSubject.Solution);

                if (calledSymbol is not null && !SymbolEqualityComparer.Default.Equals(calledSymbol, currentCallee))
                {
                    var calledProject = calledSymbol.ContainingAssembly?.Name;
                    var calleeProject = currentCallee!.ContainingAssembly?.Name;
                    AddNodeAndContainers(graph, calledSymbol, calledProject, roslynSubject.Solution);
                    AddNodeAndContainers(graph, currentCallee, calleeProject, roslynSubject.Solution);

                    graph.AddLink(new GraphLink(GetProjectScopedId(caller, callerProject), GetProjectScopedId(calledSymbol, calledProject), "CodeSchema_Calls"));
                    var relationshipCategory = calledSymbol.ContainingType?.TypeKind == TypeKind.Interface ? "Implements" : "Overrides";
                    graph.AddLink(new GraphLink(GetProjectScopedId(calledSymbol, calledProject), GetProjectScopedId(currentCallee, calleeProject), relationshipCategory));
                }
                else if (currentCallee is not null)
                {
                    var calleeProject = currentCallee.ContainingAssembly?.Name;
                    AddNodeAndContainers(graph, currentCallee, calleeProject, roslynSubject.Solution);
                    graph.AddLink(new GraphLink(GetProjectScopedId(caller, callerProject), GetProjectScopedId(currentCallee, calleeProject), "CodeSchema_Calls"));
                }

                if (graph.NodeCount >= normalizedOptions.MaxNodeCount)
                {
                    return graph;
                }

                Enqueue(caller, depth + 1);

                if (normalizedOptions.IncludeComponentHosts
                    && caller.ContainingType is INamedTypeSymbol callerType
                    && processedComponentTypes.Add(callerType)
                    && RoslynGraphHelpers.IsUIComponent(callerType))
                {
                    var hostEdges = await RoslynGraphHelpers.FindComponentHostsAsync(
                        callerType,
                        roslynSubject.Solution,
                        normalizedOptions.MaxHostDepth,
                        cancellationToken).ConfigureAwait(false);

                    foreach (var (host, component) in hostEdges)
                    {
                        var hostProject = host.ContainingAssembly?.Name;
                        var componentProject = component.ContainingAssembly?.Name;

                        AddNodeAndContainers(graph, host, hostProject, roslynSubject.Solution);
                        AddNodeAndContainers(graph, component, componentProject, roslynSubject.Solution);

                        graph.AddLink(new GraphLink(
                            GetProjectScopedId(host, hostProject),
                            GetProjectScopedId(component, componentProject),
                            "UsedBy"));

                        if (graph.NodeCount >= normalizedOptions.MaxNodeCount)
                        {
                            return graph;
                        }
                    }
                }
            }
        }

        return graph;

        void Enqueue(ISymbol symbol, int depth)
        {
            var normalized = CallGraphSyntaxWalker.NormalizeSymbol(symbol);
            if (normalized is null)
            {
                return;
            }

            var projectName = normalized.ContainingAssembly?.Name;
            var id = GetProjectScopedId(normalized, projectName);
            if (!queuedIds.Add(id))
            {
                return;
            }

            AddNodeAndContainers(graph, normalized, projectName, roslynSubject.Solution);
            queue.Enqueue((normalized, depth));
        }
    }

    public async Task<TraversalGraph> TraverseDownAsync(
        HierarchySubject subject,
        TraversalOptions options,
        IProgress<TraversalProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (subject.Payload is not RoslynHierarchySubject roslynSubject)
        {
            throw new InvalidOperationException("Unexpected hierarchy subject payload.");
        }

        var normalizedOptions = options.Normalize();
        var graph = new TraversalGraph();
        var queuedIds = new HashSet<string>(StringComparer.Ordinal);
        var queue = new Queue<(ISymbol Symbol, int Depth)>();

        Enqueue(roslynSubject.Symbol, 0);

        if (graph.NodeCount >= normalizedOptions.MaxNodeCount)
        {
            return graph;
        }

        while (queue.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var (current, depth) = queue.Dequeue();
            progress?.Report(new TraversalProgress(
                "Traversing callees",
                depth,
                graph.NodeCount,
                current.ToDisplayString()));

            if (depth >= normalizedOptions.MaxDepth)
            {
                continue;
            }

            var currentNormalized = CallGraphSyntaxWalker.NormalizeSymbol(current)!;
            var currentProject = currentNormalized.ContainingAssembly?.Name;

            // DGML is a static call graph: it only cares about which symbol was called, never
            // about fluent-chain metadata (that's sequence-diagram-only rendering, see item 1g).
            var callees = await CallGraphSyntaxWalker.FindCalleesAsync(current, roslynSubject.Solution, cancellationToken).ConfigureAwait(false);

            foreach (var calleeInfo in callees)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var normalized = CallGraphSyntaxWalker.NormalizeSymbol(calleeInfo.Symbol);
                if (normalized is null || !CallGraphFilters.IsAllowed(normalized, normalizedOptions))
                {
                    continue;
                }

                var calleeProject = normalized.ContainingAssembly?.Name;

                AddNodeAndContainers(graph, currentNormalized, currentProject, roslynSubject.Solution);
                AddNodeAndContainers(graph, normalized, calleeProject, roslynSubject.Solution);

                graph.AddLink(new GraphLink(
                    GetProjectScopedId(currentNormalized, currentProject),
                    GetProjectScopedId(normalized, calleeProject),
                    "CodeSchema_Calls"));

                if (graph.NodeCount >= normalizedOptions.MaxNodeCount)
                {
                    return graph;
                }

                Enqueue(normalized, depth + 1);

                if (normalized.ContainingType?.TypeKind == TypeKind.Interface)
                {
                    if (await AddInterfaceImplementationsAsync(graph, normalized, calleeProject, roslynSubject.Solution, normalizedOptions, depth, Enqueue, cancellationToken).ConfigureAwait(false))
                        return graph;
                }

                if (normalized.IsOverride)
                {
                    if (AddOverriddenBase(graph, normalized, calleeProject, roslynSubject.Solution, normalizedOptions, depth, Enqueue))
                        return graph;
                }
            }
        }

        return graph;

        void Enqueue(ISymbol symbol, int depth)
        {
            var normalized = CallGraphSyntaxWalker.NormalizeSymbol(symbol);
            if (normalized is null)
            {
                return;
            }

            var projectName = normalized.ContainingAssembly?.Name;
            var id = GetProjectScopedId(normalized, projectName);
            if (!queuedIds.Add(id))
            {
                return;
            }

            AddNodeAndContainers(graph, normalized, projectName, roslynSubject.Solution);
            queue.Enqueue((normalized, depth));
        }
    }

    /// <summary>
    /// For a callee that is an interface member, finds all implementations across the solution,
    /// adds them to the graph with an <c>Implements</c> link from the interface member to each
    /// implementation, and enqueues them for further traversal.
    /// Returns <see langword="true"/> when the node limit has been reached.
    /// </summary>
    private static async Task<bool> AddInterfaceImplementationsAsync(
        TraversalGraph graph,
        ISymbol interfaceMember,
        string? interfaceProject,
        RoslynSolution solution,
        TraversalOptions options,
        int depth,
        Action<ISymbol, int> enqueue,
        CancellationToken cancellationToken)
    {
        var implementations = await SymbolFinder.FindImplementationsAsync(
            interfaceMember, solution, cancellationToken: cancellationToken).ConfigureAwait(false);

        foreach (var impl in implementations)
        {
            var normImpl = CallGraphSyntaxWalker.NormalizeSymbol(impl);
            if (normImpl is null || !CallGraphFilters.IsAllowed(normImpl, options))
            {
                continue;
            }

            var implProject = normImpl.ContainingAssembly?.Name;
            AddNodeAndContainers(graph, normImpl, implProject, solution);

            graph.AddLink(new GraphLink(
                GetProjectScopedId(interfaceMember, interfaceProject),
                GetProjectScopedId(normImpl, implProject),
                "Implements"));

            if (graph.NodeCount >= options.MaxNodeCount) return true;
            enqueue(normImpl, depth + 1);
        }

        return false;
    }

    /// <summary>
    /// For a callee that overrides a base member, adds the base member to the graph with an
    /// <c>Overrides</c> link from the base member to the overriding member, and enqueues the
    /// base member for further traversal.
    /// Returns <see langword="true"/> when the node limit has been reached.
    /// </summary>
    private static bool AddOverriddenBase(
        TraversalGraph graph,
        ISymbol overridingMember,
        string? overridingProject,
        RoslynSolution solution,
        TraversalOptions options,
        int depth,
        Action<ISymbol, int> enqueue)
    {
        var baseSymbol = GetOverriddenSymbol(overridingMember);
        if (baseSymbol is null) return false;

        var normBase = CallGraphSyntaxWalker.NormalizeSymbol(baseSymbol);
        if (normBase is null || !CallGraphFilters.IsAllowed(normBase, options)) return false;

        var baseProject = normBase.ContainingAssembly?.Name;
        AddNodeAndContainers(graph, normBase, baseProject, solution);

        graph.AddLink(new GraphLink(
            GetProjectScopedId(normBase, baseProject),
            GetProjectScopedId(overridingMember, overridingProject),
            "Overrides"));

        if (graph.NodeCount >= options.MaxNodeCount) return true;
        enqueue(normBase, depth + 1);

        return false;
    }

    private static ISymbol? GetOverriddenSymbol(ISymbol symbol)
    {
        if (symbol is IMethodSymbol method) return method.OverriddenMethod;
        if (symbol is IPropertySymbol property) return property.OverriddenProperty;
        if (symbol is IEventSymbol evt) return evt.OverriddenEvent;
        return null;
    }

    // FindCalleesAsync, GetInvocationsPostOrder, IsSupportedCalleeSymbol, and UnwrapLambdaContainer
    // now live in CodeGraphToDgml.Roslyn.CallGraphSyntaxWalker, which has no VS SDK dependency and
    // is exercised directly by Roslyn-backed unit tests.

    // AddNodeAndContainers and CreateContainerNode now live in RoslynGraphHelpers, shared with
    // RoslynReferencesProvider (see using static above), so both DGML paths build the same
    // project/namespace/type containment chain from a single implementation.

    private static async Task<ISymbol?> ResolveSymbolAsync(
        RoslynDocument document,
        Workspace workspace,
        int position,
        CancellationToken cancellationToken)
    {
        var semanticModel = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
        if (semanticModel is null)
        {
            return null;
        }

        var root = await semanticModel.SyntaxTree.GetRootAsync(cancellationToken).ConfigureAwait(false);
        var token = root.FindToken(position);
        var node = token.Parent;

        // Walk up the syntax tree to find the enclosing method, property, or event declaration.
        // This ensures that when the caret is anywhere inside a method body (e.g., on a call
        // to another method Y), the resolved symbol is the enclosing method X, not Y.
        while (node is not null)
        {
            var declaredSymbol = semanticModel.GetDeclaredSymbol(node, cancellationToken);
            if (declaredSymbol is not null)
            {
                var normalized = CallGraphSyntaxWalker.NormalizeSymbol(declaredSymbol);
                if (normalized is not null && CallGraphFilters.IsSupportedRootSymbol(normalized))
                {
                    return declaredSymbol;
                }
            }

            node = node.Parent;
        }

        // Fallback for edge cases where no enclosing declaration was found (e.g., top-level statements).
        var symbol = await SymbolFinder.FindSymbolAtPositionAsync(
            semanticModel,
            position,
            workspace,
            cancellationToken).ConfigureAwait(false);

        return symbol;
    }

    private static RoslynDocument? GetDocument(RoslynSolution solution, string filePath)
    {
        var normalizedPath = NormalizePath(filePath);

        foreach (var documentId in solution.GetDocumentIdsWithFilePath(filePath))
        {
            var document = solution.GetDocument(documentId);
            if (document is not null && SupportedLanguages.Contains(document.Project.Language))
            {
                return document;
            }
        }

        foreach (var project in solution.Projects)
        {
            if (!SupportedLanguages.Contains(project.Language))
            {
                continue;
            }

            foreach (var document in project.Documents)
            {
                if (string.Equals(NormalizePath(document.FilePath), normalizedPath, StringComparison.OrdinalIgnoreCase))
                {
                    return document;
                }
            }
        }

        return null;
    }

    private static string NormalizePath(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return string.Empty;
        }

        try
        {
            return Path.GetFullPath(filePath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch
        {
            return filePath!.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
    }

    // NormalizeSymbol now lives in CallGraphSyntaxWalker; IsSupportedRootSymbol, IsAllowed,
    // IsIncludedByKind, and IsGenerated now live in CodeGraphToDgml.Roslyn.CallGraphFilters
    // (which also carries the item-4 well-known-framework-method whitelist carve-out).

    // CreateNode, GetNodeLabel, GetDeclarationLocation, GetCategory, GetContainerCategory,
    // GetStableId, and GetProjectScopedId now live in RoslynGraphHelpers (see using static above).

    private async Task<Workspace?> GetWorkspaceAsync(CancellationToken cancellationToken)
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

        var componentModel = await _package.GetServiceAsync(typeof(SComponentModel)).ConfigureAwait(true) as IComponentModel;
        if (componentModel is null)
        {
            return null;
        }

        var genericMethodDefinition = typeof(IComponentModel)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Single(method => method.Name == nameof(IComponentModel.GetService) && method.IsGenericMethodDefinition && method.GetParameters().Length == 0);

        foreach (var workspaceType in GetWorkspaceCandidateTypes())
        {
            if (!typeof(Workspace).IsAssignableFrom(workspaceType))
            {
                continue;
            }

            try
            {
                var genericMethod = genericMethodDefinition.MakeGenericMethod(workspaceType);
                if (genericMethod.Invoke(componentModel, null) is Workspace workspace)
                {
                    return workspace;
                }
            }
            catch
            {
            }
        }

        try
        {
            var exportProviderProperty = componentModel.GetType().GetProperty("DefaultExportProvider", BindingFlags.Public | BindingFlags.Instance);
            var exportProvider = exportProviderProperty?.GetValue(componentModel);
            var getExportedValuesMethod = exportProvider?.GetType()
                .GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .FirstOrDefault(method => method.Name == "GetExportedValues" && method.IsGenericMethodDefinition && method.GetParameters().Length == 0);

            if (exportProvider is not null && getExportedValuesMethod is not null)
            {
                var genericMethod = getExportedValuesMethod.MakeGenericMethod(typeof(Workspace));
                if (genericMethod.Invoke(exportProvider, null) is IEnumerable<Workspace> workspaces)
                {
                    return workspaces.FirstOrDefault();
                }
            }
        }
        catch
        {
        }

        return null;
    }

    private static IEnumerable<Type> GetWorkspaceCandidateTypes()
    {
        yield return typeof(Workspace);

        var primaryType = Type.GetType(
            "Microsoft.VisualStudio.LanguageServices.VisualStudioWorkspace, Microsoft.VisualStudio.LanguageServices",
            throwOnError: false);

        if (primaryType is not null)
        {
            yield return primaryType;
        }

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type[] types;
            try
            {
                types = assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                types = ex.Types.Where(type => type is not null).Cast<Type>().ToArray();
            }
            catch
            {
                continue;
            }

            foreach (var type in types)
            {
                if (!typeof(Workspace).IsAssignableFrom(type))
                {
                    continue;
                }

                if (type.Name.IndexOf("Workspace", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                yield return type;
            }
        }
    }

    public async Task<CallSequence> TraverseDownToSequenceAsync(
        HierarchySubject subject,
        TraversalOptions options,
        IProgress<TraversalProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (subject.Payload is not RoslynHierarchySubject roslynSubject)
            throw new InvalidOperationException("Unexpected hierarchy subject payload.");

        var normalizedOptions = options.Normalize();
        var participantTable = new ParticipantTable();
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var nodeCount = 0;

        var rootSymbol = CallGraphSyntaxWalker.NormalizeSymbol(roslynSubject.Symbol)!;
        var rootParticipantId = GetParticipantId(rootSymbol, participantTable, callerParticipantId: null);

        var rootCalls = await BuildCallsAsync(rootSymbol, rootParticipantId, 0).ConfigureAwait(false);

        return new CallSequence
        {
            Title = subject.DisplayName,
            Participants = participantTable.Participants,
            RootCalls = rootCalls,
            RootParticipantId = rootParticipantId,
            RootMethodLabel = GetNodeLabel(rootSymbol),
        };

        async Task<IReadOnlyList<CallSequenceCallNode>> BuildCallsAsync(
            ISymbol caller,
            string callerParticipantId,
            int depth)
        {
            if (depth >= normalizedOptions.MaxDepth)
                return [];

            progress?.Report(new TraversalProgress("Traversing callees", depth, nodeCount, caller.ToDisplayString()));

            var callees = await CallGraphSyntaxWalker.FindCalleesAsync(caller, roslynSubject.Solution, cancellationToken).ConfigureAwait(false);

            // Item 1g: fluent invocation chains (e.g. obj.GetClient().GetProduct()) are nested
            // under their receiver's activation in the sequence diagram, instead of being shown
            // as siblings both called directly by `caller`. This is a rendering-only distinction —
            // the DGML graph (TraverseDownAsync above) never sees CalleeInfo.FluentReceiver.
            //
            // Pass 1: build each callee's own node content (participant, label, its own nested
            // calls) without yet deciding whether it will be a top-level sibling or grafted under
            // a fluent receiver.
            var order = new List<ISymbol>();
            var ownNestedOf = new Dictionary<ISymbol, IReadOnlyList<CallSequenceCallNode>>(SymbolEqualityComparer.Default);
            var participantOf = new Dictionary<ISymbol, string>(SymbolEqualityComparer.Default);
            var labelOf = new Dictionary<ISymbol, string>(SymbolEqualityComparer.Default);
            var fluentReceiverOf = new Dictionary<ISymbol, ISymbol>(SymbolEqualityComparer.Default);

            foreach (var calleeInfo in callees)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var normalized = CallGraphSyntaxWalker.NormalizeSymbol(calleeInfo.Symbol);
                if (normalized is null || !CallGraphFilters.IsAllowed(normalized, normalizedOptions))
                    continue;

                nodeCount++;
                if (nodeCount >= normalizedOptions.MaxNodeCount)
                    break;

                var calleeParticipantId = GetParticipantId(normalized, participantTable, callerParticipantId);
                var calleeId = GetStableId(normalized);

                IReadOnlyList<CallSequenceCallNode> nested;
                if (normalized.ContainingType?.TypeKind == TypeKind.Interface)
                {
                    visited.Add(calleeId);
                    nested = await BuildInterfaceDispatchCallsAsync(normalized, calleeParticipantId, depth).ConfigureAwait(false);
                }
                else if (visited.Add(calleeId))
                {
                    nested = await BuildCallsAsync(normalized, calleeParticipantId, depth + 1).ConfigureAwait(false);
                }
                else
                {
                    nested = [];
                }

                order.Add(normalized);
                ownNestedOf[normalized] = nested;
                participantOf[normalized] = calleeParticipantId;
                labelOf[normalized] = GetNodeLabel(normalized);

                if (calleeInfo.FluentReceiver is { } receiver)
                {
                    var normReceiver = CallGraphSyntaxWalker.NormalizeSymbol(receiver);
                    if (normReceiver != null)
                        fluentReceiverOf[normalized] = normReceiver;
                }
            }

            // Pass 2: assemble nodes, grafting fluent-dependents into their receiver's
            // NestedCalls (with the receiver's own participant as the dependent's caller)
            // instead of returning them as top-level siblings of `caller`.
            var finalized = new Dictionary<ISymbol, CallSequenceCallNode>(SymbolEqualityComparer.Default);

            CallSequenceCallNode Finalize(ISymbol symbol, string effectiveCallerParticipantId)
            {
                if (finalized.TryGetValue(symbol, out var existing))
                    return existing;

                var participantId = participantOf[symbol];
                var ownNested = new List<CallSequenceCallNode>(ownNestedOf[symbol]);
                foreach (var dependent in order)
                {
                    if (fluentReceiverOf.TryGetValue(dependent, out var receiver)
                        && SymbolEqualityComparer.Default.Equals(receiver, symbol))
                    {
                        ownNested.Add(Finalize(dependent, participantId));
                    }
                }

                var node = new CallSequenceCallNode(effectiveCallerParticipantId, participantId, labelOf[symbol], ownNested);
                finalized[symbol] = node;
                return node;
            }

            var nodes = new List<CallSequenceCallNode>();
            foreach (var symbol in order)
            {
                // Skip symbols that will be grafted under their fluent receiver's NestedCalls
                // (only when that receiver was itself kept in this batch).
                if (fluentReceiverOf.TryGetValue(symbol, out var receiver) && ownNestedOf.ContainsKey(receiver))
                    continue;

                nodes.Add(Finalize(symbol, callerParticipantId));
            }

            return nodes;
        }

        async Task<IReadOnlyList<CallSequenceCallNode>> BuildInterfaceDispatchCallsAsync(
            ISymbol interfaceMember,
            string interfaceParticipantId,
            int depth)
        {
            var implementations = await SymbolFinder.FindImplementationsAsync(
                interfaceMember, roslynSubject.Solution, cancellationToken: cancellationToken).ConfigureAwait(false);

            var dispatchNodes = new List<CallSequenceCallNode>();
            foreach (var impl in implementations)
            {
                var normImpl = CallGraphSyntaxWalker.NormalizeSymbol(impl);
                if (normImpl is null || !CallGraphFilters.IsAllowed(normImpl, normalizedOptions))
                    continue;

                nodeCount++;
                if (nodeCount >= normalizedOptions.MaxNodeCount)
                    break;

                var implParticipantId = GetParticipantId(normImpl, participantTable, interfaceParticipantId);
                var implId = GetStableId(normImpl);

                IReadOnlyList<CallSequenceCallNode> implBody;
                if (visited.Add(implId))
                    implBody = await BuildCallsAsync(normImpl, implParticipantId, depth + 1).ConfigureAwait(false);
                else
                    implBody = [];

                dispatchNodes.Add(new CallSequenceCallNode(interfaceParticipantId, implParticipantId, GetNodeLabel(normImpl), implBody));
            }

            return dispatchNodes;
        }
    }

    private static string GetParticipantId(ISymbol symbol, ParticipantTable table, string? callerParticipantId)
    {
        // Item 4: well-known framework "outcome" methods (e.g. ControllerBase.Ok/BadRequest) are
        // rendered as a self-call on the caller's own lifeline rather than a separate participant
        // for their (external) declaring type — one noisy "ControllerBase" swimlane per whitelisted
        // call would be both cluttered and semantically wrong, since the caller itself produces
        // the response, not a distinct collaborator.
        if (callerParticipantId is not null && symbol is IMethodSymbol method && WellKnownFrameworkMethods.IsWellKnownOutcomeMethod(method))
        {
            return callerParticipantId;
        }

        var containingType = symbol.ContainingType;
        if (containingType is not null)
            return table.GetOrAdd(containingType);

        // Top-level statements or module-level code: use assembly name as fallback.
        var assemblyName = symbol.ContainingAssembly?.Name ?? "?";
        return table.GetOrAddRaw(assemblyName);
    }

    private sealed class ParticipantTable
    {
        private readonly Dictionary<string, string> _symbolIdToMermaidId = new(StringComparer.Ordinal);
        private readonly Dictionary<string, int> _baseNameCount = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<CallSequenceParticipant> _participants = new();

        public IReadOnlyList<CallSequenceParticipant> Participants => _participants;

        public string GetOrAdd(INamedTypeSymbol type)
        {
            var symbolId = type.GetDocumentationCommentId() ?? type.ToDisplayString();
            if (_symbolIdToMermaidId.TryGetValue(symbolId, out var existing))
                return existing;

            var mermaidId = AllocateId(type.Name, symbolId);
            _participants.Add(new CallSequenceParticipant(mermaidId, type.Name));
            return mermaidId;
        }

        public string GetOrAddRaw(string name)
        {
            if (_symbolIdToMermaidId.TryGetValue(name, out var existing))
                return existing;

            var mermaidId = AllocateId(name, name);
            _participants.Add(new CallSequenceParticipant(mermaidId, name));
            return mermaidId;
        }

        private string AllocateId(string typeName, string symbolKey)
        {
            var baseName = SanitizeName(typeName);
            if (string.IsNullOrEmpty(baseName)) baseName = "Unknown";

            string mermaidId;
            if (!_baseNameCount.TryGetValue(baseName, out var count))
            {
                _baseNameCount[baseName] = 1;
                mermaidId = baseName;
            }
            else
            {
                _baseNameCount[baseName] = count + 1;
                mermaidId = $"{baseName}_{count}";
            }

            _symbolIdToMermaidId[symbolKey] = mermaidId;
            return mermaidId;
        }

        private static string SanitizeName(string name)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var c in name)
            {
                if (char.IsLetterOrDigit(c) || c == '_')
                    sb.Append(c);
                else
                    sb.Append('_');
            }

            var result = sb.ToString().TrimStart('_');
            if (result.Length == 0) return "Unknown";
            return char.IsDigit(result[0]) ? "_" + result : result;
        }
    }

    private sealed class RoslynHierarchySubject
    {
        public RoslynHierarchySubject(ISymbol symbol, RoslynSolution solution)
        {
            Symbol = symbol;
            Solution = solution;
        }

        public ISymbol Symbol { get; }

        public RoslynSolution Solution { get; }
    }
}
