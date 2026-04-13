using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.CodeAnalysis.Host.Mef;
using CallHierarchyToDgml.Core;
using EnvDTE;
using EnvDTE80;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.VisualStudio.ComponentModelHost;
using RoslynDocument = Microsoft.CodeAnalysis.Document;
using RoslynSolution = Microsoft.CodeAnalysis.Solution;

namespace CallHierarchyToDgml.Vsix.Providers;

internal sealed class RoslynCallHierarchyProvider : IHierarchyProvider
{
    private static readonly HashSet<string> SupportedLanguages = new(StringComparer.Ordinal)
    {
        LanguageNames.CSharp,
        LanguageNames.VisualBasic,
    };

    private readonly ToolkitPackage _package;

    public RoslynCallHierarchyProvider(ToolkitPackage package)
    {
        _package = package;
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

        var position = Math.Max(0, selection.ActivePoint.AbsoluteCharOffset - 1);

        var symbol = await ResolveSymbolAsync(document, workspace, position, cancellationToken).ConfigureAwait(false);
        symbol = NormalizeSymbol(symbol);

        if (symbol is null || !IsSupportedRootSymbol(symbol))
        {
            return null;
        }


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

                var caller = NormalizeSymbol(callerInfo.CallingSymbol);
                if (caller is null || !IsAllowed(caller, normalizedOptions))
                {
                    continue;
                }

                var callerNode = CreateNode(caller);
                var currentCallee = NormalizeSymbol(current);
                var calledSymbol = NormalizeSymbol(callerInfo.CalledSymbol);

                AddNodeAndContainers(graph, caller, caller.ContainingAssembly?.Name);

                if (calledSymbol is not null && !SymbolEqualityComparer.Default.Equals(calledSymbol, currentCallee))
                {
                    AddNodeAndContainers(graph, calledSymbol, calledSymbol.ContainingAssembly?.Name);
                    AddNodeAndContainers(graph, currentCallee!, currentCallee!.ContainingAssembly?.Name);

                    graph.AddLink(new GraphLink(GetStableId(caller), GetStableId(calledSymbol), "CodeSchema_Calls"));
                    graph.AddLink(new GraphLink(GetStableId(calledSymbol), GetStableId(currentCallee!), "Implements/Overrides"));
                }
                else if (currentCallee is not null)
                {
                    AddNodeAndContainers(graph, currentCallee, currentCallee.ContainingAssembly?.Name);
                    graph.AddLink(new GraphLink(GetStableId(caller), GetStableId(currentCallee), "CodeSchema_Calls"));
                }

                if (graph.NodeCount >= normalizedOptions.MaxNodeCount)
                {
                    return graph;
                }

                Enqueue(caller, depth + 1);
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

            var id = GetStableId(normalized);
            if (!queuedIds.Add(id))
            {
                return;
            }

            AddNodeAndContainers(graph, normalized, normalized.ContainingAssembly?.Name);
            queue.Enqueue((normalized, depth));
        }
    }

    private static void AddNodeAndContainers(TraversalGraph graph, ISymbol symbol, string? projectName)
    {
        var node = CreateNode(symbol);
        if (graph.ContainsNode(node.Id)) return;
        graph.UpsertNode(node);

        var currentId = node.Id;
        var currentContainer = symbol.ContainingSymbol;

        while (currentContainer != null)
        {
            if (currentContainer is INamespaceSymbol ns && ns.IsGlobalNamespace)
            {
                break;
            }

            var containerNode = CreateContainerNode(currentContainer);
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

    private static GraphNode CreateContainerNode(ISymbol container)
    {
        var category = container is INamedTypeSymbol nt ? GetContainerCategory(nt) :
                       container is INamespaceSymbol ? "CodeSchema_Namespace" :
                       "Group";

        return new GraphNode(
            GetStableId(container),
            container.Name,
            category,
            null,
            null,
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

        var symbol = await SymbolFinder.FindSymbolAtPositionAsync(
            semanticModel,
            position,
            workspace,
            cancellationToken).ConfigureAwait(false);

        if (symbol is not null)
        {
            return symbol;
        }

        var root = await semanticModel.SyntaxTree.GetRootAsync(cancellationToken).ConfigureAwait(false);
        var token = root.FindToken(position);
        var node = token.Parent;

        while (node is not null)
        {
            symbol = semanticModel.GetDeclaredSymbol(node, cancellationToken);
            if (symbol is not null)
            {
                return symbol;
            }

            symbol = semanticModel.GetSymbolInfo(node, cancellationToken).Symbol;
            if (symbol is not null)
            {
                return symbol;
            }

            node = node.Parent;
        }

        return null;
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

    private static GraphNode CreateNode(ISymbol symbol)
    {
        var location = symbol.Locations.FirstOrDefault(candidate => candidate.IsInSource);
        var lineSpan = location?.GetLineSpan();

        return new GraphNode(
            GetStableId(symbol),
            GetNodeLabel(symbol),
            GetCategory(symbol),
            lineSpan?.Path,
            lineSpan is null ? null : lineSpan.Value.StartLinePosition.Line + 1,
            symbol.ContainingAssembly?.Name);
    }

    private static string GetNodeLabel(ISymbol symbol)
    {
        if (symbol is IMethodSymbol method && method.MethodKind == MethodKind.Constructor)
        {
            return symbol.ContainingType?.Name ?? symbol.Name;
        }

        var name = symbol.Name;
        var lastDot = name.LastIndexOf('.');
        if (lastDot >= 0 && lastDot < name.Length - 1)
        {
            name = name.Substring(lastDot + 1);
        }

        return name;
    }

    private static string GetCategory(ISymbol symbol)
    {
        return symbol.Kind switch
        {
            SymbolKind.Property => "CodeSchema_Property",
            SymbolKind.Event => "CodeSchema_Event",
            _ => "CodeSchema_Method",
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
