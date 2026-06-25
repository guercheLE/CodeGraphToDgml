using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.CodeAnalysis.Host.Mef;
using CodeGraphToDgml.Core;
using EnvDTE;
using EnvDTE80;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.VisualStudio.ComponentModelHost;
using RoslynDocument = Microsoft.CodeAnalysis.Document;
using RoslynSolution = Microsoft.CodeAnalysis.Solution;

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

        symbol = NormalizeSymbol(symbol);

        await _logger.WriteLineAsync($"[ResolveSubject] Normalized symbol: {symbol?.ToDisplayString() ?? "(null)"}, Kind={symbol?.Kind}").ConfigureAwait(false);

        if (symbol is null || !IsSupportedRootSymbol(symbol))
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
                var caller = NormalizeSymbol(UnwrapLambdaContainer(callerInfo.CallingSymbol));
                if (caller is null || !IsAllowed(caller, normalizedOptions))
                {
                    continue;
                }

                var currentCallee = NormalizeSymbol(current);
                var calledSymbol = NormalizeSymbol(callerInfo.CalledSymbol);

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
            var normalized = NormalizeSymbol(symbol);
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

            var currentNormalized = NormalizeSymbol(current)!;
            var currentProject = currentNormalized.ContainingAssembly?.Name;

            var callees = await FindCalleesAsync(current, roslynSubject.Solution, cancellationToken).ConfigureAwait(false);

            foreach (var callee in callees)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var normalized = NormalizeSymbol(callee);
                if (normalized is null || !IsAllowed(normalized, normalizedOptions))
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
            var normalized = NormalizeSymbol(symbol);
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
            var normImpl = NormalizeSymbol(impl);
            if (normImpl is null || !IsAllowed(normImpl, options))
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

        var normBase = NormalizeSymbol(baseSymbol);
        if (normBase is null || !IsAllowed(normBase, options)) return false;

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

    private static async Task<IReadOnlyList<ISymbol>> FindCalleesAsync(
        ISymbol symbol,
        RoslynSolution solution,
        CancellationToken cancellationToken)
    {
        var callees = new List<ISymbol>();
        var seen = new HashSet<ISymbol>(SymbolEqualityComparer.Default);

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

            foreach (var descendant in syntaxNode.DescendantNodes())
            {
                // Only process invocation expressions for chained call analysis
                if (descendant is Microsoft.CodeAnalysis.CSharp.Syntax.InvocationExpressionSyntax invocation)
                {
                    var symbolInfo = semanticModel.GetSymbolInfo(invocation, cancellationToken);
                    var resolved = symbolInfo.Symbol ?? (symbolInfo.CandidateSymbols.Length == 1 ? symbolInfo.CandidateSymbols[0] : null);

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
                    if (SymbolEqualityComparer.Default.Equals(normalized, NormalizeSymbol(symbol)))
                    {
                        continue;
                    }

                    if (seen.Add(normalized))
                    {
                        callees.Add(normalized);
                    }

                    // Recursively resolve chained member accesses (e.g., obj.Property.Method())
                    var expr = invocation.Expression;
                    while (expr is Microsoft.CodeAnalysis.CSharp.Syntax.MemberAccessExpressionSyntax memberAccess)
                    {
                        var memberSymbolInfo = semanticModel.GetSymbolInfo(memberAccess, cancellationToken);
                        var memberResolved = memberSymbolInfo.Symbol ?? (memberSymbolInfo.CandidateSymbols.Length == 1 ? memberSymbolInfo.CandidateSymbols[0] : null);
                        var memberNormalized = NormalizeSymbol(memberResolved);
                        if (memberNormalized != null && IsSupportedCalleeSymbol(memberNormalized))
                        {
                            if (!SymbolEqualityComparer.Default.Equals(memberNormalized, NormalizeSymbol(symbol)) && seen.Add(memberNormalized))
                            {
                                callees.Add(memberNormalized);
                            }
                        }
                        expr = memberAccess.Expression;
                    }
                }
            }
        }

        return callees;
    }

    private static bool IsSupportedCalleeSymbol(ISymbol symbol)
    {
        // Exclude lambdas, anonymous methods, and local functions. They have empty Name
        // and a synthesised containing type, which would render as empty-named nodes in
        // the graph. Their inner invocations are still discovered because the syntax
        // walker descends through the lambda/local-function body.
        if (symbol is IMethodSymbol method
            && (method.MethodKind == MethodKind.AnonymousFunction
                || method.MethodKind == MethodKind.LocalFunction))
        {
            return false;
        }

        return symbol.Kind is SymbolKind.Method or SymbolKind.Property or SymbolKind.Event;
    }

    /// <summary>
    /// If <paramref name="symbol"/> is a lambda, anonymous method, or local function,
    /// walks up the containing-symbol chain until a real (non-lambda) symbol is found.
    /// Used when an upward-traversal caller comes back as a lambda's anonymous method.
    /// </summary>
    private static ISymbol? UnwrapLambdaContainer(ISymbol? symbol)
    {
        while (symbol is IMethodSymbol method
            && (method.MethodKind == MethodKind.AnonymousFunction
                || method.MethodKind == MethodKind.LocalFunction))
        {
            symbol = symbol.ContainingSymbol;
        }

        return symbol;
    }

    private static void AddNodeAndContainers(TraversalGraph graph, ISymbol symbol, string? projectName, RoslynSolution? solution = null)
    {
        var node = CreateNode(symbol, projectName);
        if (graph.ContainsNode(node.Id)) return;
        graph.UpsertNode(node);

        var currentId = node.Id;
        var currentContainer = symbol.ContainingSymbol;

        while (currentContainer != null)
        {
            if (currentContainer is INamespaceSymbol ns && ns.IsGlobalNamespace)
            {
                if (solution != null && projectName != null && currentId == node.Id)
                {
                    var proj = solution.Projects.FirstOrDefault(p => p.AssemblyName == projectName);
                    if (proj != null && !string.IsNullOrWhiteSpace(proj.DefaultNamespace))
                    {
                        var defaultNsId = $"Project={projectName}|N:{proj.DefaultNamespace}";
                        if (defaultNsId != currentId)
                        {
                            var defaultNsNode = new GraphNode(
                                defaultNsId,
                                proj.DefaultNamespace!,
                                "CodeSchema_Namespace",
                                null,
                                null,
                                null);

                            graph.UpsertNode(defaultNsNode);
                            graph.AddLink(new GraphLink(defaultNsNode.Id, currentId, "Contains"));
                            currentId = defaultNsId;
                        }
                    }
                }
                break;
            }

            var containerNode = CreateContainerNode(currentContainer, projectName);
            graph.UpsertNode(containerNode);
            graph.AddLink(new GraphLink(containerNode.Id, currentId, "Contains"));

            currentId = containerNode.Id;
            currentContainer = currentContainer.ContainingSymbol;
        }

        if (!string.IsNullOrWhiteSpace(projectName))
        {
            var projectId = $"Project={projectName}";
            var projectNode = new GraphNode(
                projectId,
                projectName!,
                "CodeSchema_Assembly",
                null,
                null,
                projectName);

            graph.UpsertNode(projectNode);
            graph.AddLink(new GraphLink(projectId, currentId, "Contains"));
        }
    }

    private static GraphNode CreateContainerNode(ISymbol container, string? projectName)
    {
        var category = GetCategory(container);

        var id = GetStableId(container);
        if ((container is INamespaceSymbol || container is INamedTypeSymbol) && !string.IsNullOrWhiteSpace(projectName))
        {
            id = $"Project={projectName}|{id}";
        }

        var label = container is INamespaceSymbol ns
            ? ns.ToDisplayString()
            : container.Name;

        var (filePath, lineNumber) = GetDeclarationLocation(container);

        return new GraphNode(
            id,
            label,
            category,
            filePath,
            lineNumber,
            null);
    }

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
                var normalized = NormalizeSymbol(declaredSymbol);
                if (normalized is not null && IsSupportedRootSymbol(normalized))
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

    private static ISymbol? NormalizeSymbol(ISymbol? symbol)
    {
        if (symbol is IMethodSymbol method && method.AssociatedSymbol is not null)
        {
            symbol = method.AssociatedSymbol;
        }

        return symbol?.OriginalDefinition;
    }

    private static bool IsSupportedRootSymbol(ISymbol symbol)
    {
        return symbol.Kind is SymbolKind.Method or SymbolKind.Property or SymbolKind.Event;
    }

    private static bool IsAllowed(ISymbol symbol, TraversalOptions options)
    {
        if (!IsIncludedByKind(symbol, options))
        {
            return false;
        }

        var sourceLocation = symbol.Locations.FirstOrDefault(location => location.IsInSource);
        if (!options.IncludeExternalSymbols && sourceLocation is null)
        {
            return false;
        }

        if (!options.IncludeGeneratedCode && IsGenerated(sourceLocation?.SourceTree?.FilePath))
        {
            return false;
        }

        return true;
    }

    private static bool IsIncludedByKind(ISymbol symbol, TraversalOptions options)
    {
        return symbol.Kind switch
        {
            SymbolKind.Method => true,
            SymbolKind.Property => options.IncludeProperties,
            SymbolKind.Event => options.IncludeEvents,
            _ => false,
        };
    }

    private static bool IsGenerated(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return false;
        }

        var normalizedFilePath = filePath!;
        var fileName = Path.GetFileName(normalizedFilePath) ?? string.Empty;
        return fileName.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase)
            || fileName.EndsWith(".g.vb", StringComparison.OrdinalIgnoreCase)
            || fileName.EndsWith(".designer.cs", StringComparison.OrdinalIgnoreCase)
            || fileName.EndsWith(".designer.vb", StringComparison.OrdinalIgnoreCase)
            || fileName.EndsWith(".generated.cs", StringComparison.OrdinalIgnoreCase)
            || fileName.EndsWith(".generated.vb", StringComparison.OrdinalIgnoreCase)
            || normalizedFilePath.IndexOf("\\obj\\", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static GraphNode CreateNode(ISymbol symbol, string? projectName = null)
    {
        var (filePath, lineNumber) = GetDeclarationLocation(symbol);

        return new GraphNode(
            GetProjectScopedId(symbol, projectName),
            GetNodeLabel(symbol),
            GetCategory(symbol),
            filePath,
            lineNumber,
            symbol.ContainingAssembly?.Name);
    }

    private static string GetNodeLabel(ISymbol symbol)
    {
        if (symbol is IMethodSymbol method && method.MethodKind == MethodKind.Constructor)
        {
            return symbol.ContainingType?.Name ?? symbol.Name;
        }

        var name = symbol.Name;

        // Explicit interface implementations in C# include the interface name as a qualifier
        // (e.g. "IFoo.Bar"). Strip the qualifier so the label shows only the member name.
        var lastDot = name.LastIndexOf('.');
        if (lastDot >= 0 && lastDot < name.Length - 1)
        {
            name = name.Substring(lastDot + 1);
        }

        // Guard against synthesised or metadata symbols that have an empty Name.
        if (string.IsNullOrWhiteSpace(name))
        {
            name = symbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
        }

        return name;
    }

    /// <summary>
    /// Returns the source file path and 1-based line number of a symbol's declaration identifier.
    /// Prefers <see cref="ISymbol.Locations"/> (which Roslyn sets to the identifier token for
    /// named source symbols) and falls back to scanning the declaring syntax node's child tokens
    /// for the name token, then to the declaring syntax node's own start position.
    /// </summary>
    private static (string? FilePath, int? Line) GetDeclarationLocation(ISymbol symbol)
    {
        // Roslyn sets Locations[0] to the identifier token for most named source symbols.
        var inSourceLocation = symbol.Locations.FirstOrDefault(l => l.IsInSource);
        if (inSourceLocation is not null)
        {
            var span = inSourceLocation.GetLineSpan();
            return (span.Path, span.StartLinePosition.Line + 1);
        }

        // For metadata-only symbols, DeclaringSyntaxReferences is empty – nothing to fall back to.
        if (symbol.DeclaringSyntaxReferences.Length == 0)
        {
            return (null, null);
        }

        // Fallback: scan the declaring syntax node for the child token matching the symbol name.
        // This pinpoints the identifier precisely rather than using the full declaration span.
        var syntaxNode = symbol.DeclaringSyntaxReferences[0].GetSyntax();
        if (!string.IsNullOrEmpty(symbol.Name))
        {
            foreach (var token in syntaxNode.ChildTokens())
            {
                if (!token.IsMissing && token.ValueText == symbol.Name)
                {
                    var tokenSpan = token.GetLocation().GetLineSpan();
                    return (tokenSpan.Path, tokenSpan.StartLinePosition.Line + 1);
                }
            }
        }

        // Last resort: use the start of the declaring syntax node.
        var nodeSpan = syntaxNode.GetLocation().GetLineSpan();
        return (nodeSpan.Path, nodeSpan.StartLinePosition.Line + 1);
    }

    private static string GetCategory(ISymbol symbol)
    {
        return symbol.Kind switch
        {
            SymbolKind.NamedType => GetContainerCategory((INamedTypeSymbol)symbol),
            SymbolKind.Namespace => "CodeSchema_Namespace",
            SymbolKind.Property => "CodeSchema_Property",
            SymbolKind.Event => "CodeSchema_Event",
            SymbolKind.Method => "CodeSchema_Method",
            SymbolKind.Field => "CodeSchema_Field",
            _ => "Group",
        };
    }

    private static string GetContainerCategory(INamedTypeSymbol container)
    {
        return container.TypeKind switch
        {
            TypeKind.Interface => "CodeSchema_Interface",
            TypeKind.Struct => "CodeSchema_Struct",
            TypeKind.Enum => "CodeSchema_Enum",
            TypeKind.Delegate => "CodeSchema_Delegate",
            _ => "CodeSchema_Class"
        };
    }

    private static string GetStableId(ISymbol symbol)
    {
        return symbol.GetDocumentationCommentId()
            ?? symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
    }

    private static string GetProjectScopedId(ISymbol symbol, string? projectName)
    {
        var id = GetStableId(symbol);
        if (!string.IsNullOrWhiteSpace(projectName))
        {
            id = $"Project={projectName}|{id}";
        }

        return id;
    }

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
