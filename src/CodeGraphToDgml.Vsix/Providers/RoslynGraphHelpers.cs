using System.IO;
using System.Linq;
using System.Reflection;
using CodeGraphToDgml.Core;
using EnvDTE;
using EnvDTE80;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.VisualStudio.ComponentModelHost;
using RoslynDocument = Microsoft.CodeAnalysis.Document;
using RoslynSolution = Microsoft.CodeAnalysis.Solution;

namespace CodeGraphToDgml.Vsix.Providers;

internal static class RoslynGraphHelpers
{
    internal static readonly HashSet<string> SupportedLanguages = new(StringComparer.Ordinal)
    {
        LanguageNames.CSharp,
        LanguageNames.VisualBasic,
    };

    internal static bool CouldSupport(string? filePath)
    {
        var extension = Path.GetExtension(filePath ?? string.Empty);
        return extension.Equals(".cs", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".vb", StringComparison.OrdinalIgnoreCase);
    }

    internal static void AddNodeAndContainers(TraversalGraph graph, ISymbol symbol, string? projectName)
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

    internal static GraphNode CreateNode(ISymbol symbol, string? projectName = null)
    {
        var location = symbol.Locations.FirstOrDefault(candidate => candidate.IsInSource);
        if (location == null && symbol.DeclaringSyntaxReferences.Length > 0)
        {
            location = symbol.DeclaringSyntaxReferences[0].GetSyntax().GetLocation();
        }
        var lineSpan = location?.GetLineSpan();

        string? filePath = null;
        int? lineNumber = null;

        if (lineSpan.HasValue)
        {
            filePath = lineSpan.Value.Path;
            lineNumber = lineSpan.Value.StartLinePosition.Line + 1;
        }

        return new GraphNode(
            GetProjectScopedId(symbol, projectName),
            GetNodeLabel(symbol),
            GetCategory(symbol),
            filePath,
            lineNumber,
            symbol.ContainingAssembly?.Name);
    }

    internal static GraphNode CreateContainerNode(ISymbol container, string? projectName)
    {
        var category = container is INamedTypeSymbol nt ? GetContainerCategory(nt) :
                       container is INamespaceSymbol ? "CodeSchema_Namespace" :
                       "Group";

        var id = GetStableId(container);
        if ((container is INamespaceSymbol || container is INamedTypeSymbol) && !string.IsNullOrWhiteSpace(projectName))
        {
            id = $"Project={projectName}|{id}";
        }

        var label = container is INamespaceSymbol ns
            ? ns.ToDisplayString()
            : container.Name;

        string? filePath = null;
        int? lineNumber = null;

        var location = container.Locations.FirstOrDefault(candidate => candidate.IsInSource);
        if (location is null && container.DeclaringSyntaxReferences.Length > 0)
        {
            location = container.DeclaringSyntaxReferences[0].GetSyntax().GetLocation();
        }

        var lineSpan = location?.GetLineSpan();
        if (lineSpan.HasValue)
        {
            filePath = lineSpan.Value.Path;
            lineNumber = lineSpan.Value.StartLinePosition.Line + 1;
        }

        return new GraphNode(
            id,
            label,
            category,
            filePath,
            lineNumber,
            null);
    }

    internal static string GetStableId(ISymbol symbol)
    {
        return symbol.GetDocumentationCommentId()
            ?? symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
    }

    internal static string GetProjectScopedId(ISymbol symbol, string? projectName)
    {
        var id = GetStableId(symbol);
        if (!string.IsNullOrWhiteSpace(projectName))
        {
            id = $"Project={projectName}|{id}";
        }

        return id;
    }

    internal static ISymbol? NormalizeSymbol(ISymbol? symbol)
    {
        if (symbol is IMethodSymbol method && method.AssociatedSymbol is not null)
        {
            symbol = method.AssociatedSymbol;
        }

        return symbol?.OriginalDefinition;
    }

    internal static string GetNodeLabel(ISymbol symbol)
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

    internal static string GetCategory(ISymbol symbol)
    {
        return symbol.Kind switch
        {
            SymbolKind.NamedType => GetContainerCategory((INamedTypeSymbol)symbol),
            SymbolKind.Property => "CodeSchema_Property",
            SymbolKind.Event => "CodeSchema_Event",
            SymbolKind.Method => "CodeSchema_Method",
            _ => "CodeSchema_Method",
        };
    }

    internal static string GetContainerCategory(INamedTypeSymbol container)
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

    internal static async Task<Workspace?> GetWorkspaceAsync(ToolkitPackage package, CancellationToken cancellationToken)
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

        var componentModel = await package.GetServiceAsync(typeof(SComponentModel)).ConfigureAwait(true) as IComponentModel;
        if (componentModel is null)
        {
            return null;
        }

        var genericMethodDefinition = typeof(IComponentModel)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Single(m => m.Name == nameof(IComponentModel.GetService) && m.IsGenericMethodDefinition && m.GetParameters().Length == 0);

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
                // Intentionally swallowed: try the next candidate type.
            }
        }

        return null;
    }

    internal static RoslynDocument? GetDocument(RoslynSolution solution, string filePath)
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

    internal static async Task<int> GetPositionFromSelectionAsync(
        RoslynDocument document,
        TextSelection selection,
        CancellationToken cancellationToken)
    {
        var text = await document.GetTextAsync(cancellationToken).ConfigureAwait(false);
        if (text.Lines.Count == 0)
        {
            return 0;
        }

        var requestedLineIndex = Math.Max(0, selection.ActivePoint.Line - 1);
        var lineIndex = Math.Min(requestedLineIndex, text.Lines.Count - 1);
        var line = text.Lines[lineIndex];

        var requestedColumnIndex = Math.Max(0, selection.ActivePoint.LineCharOffset - 1);
        var maxColumnIndex = line.End - line.Start;
        var columnIndex = Math.Min(requestedColumnIndex, maxColumnIndex);

        return line.Start + columnIndex;
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

    private static IEnumerable<Type> GetWorkspaceCandidateTypes()
    {
        const string primaryTypeName = "Microsoft.VisualStudio.LanguageServices.VisualStudioWorkspace";
        const string roslynAssemblyName = "Microsoft.VisualStudio.LanguageServices";

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (!assembly.FullName.StartsWith(roslynAssemblyName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            foreach (var type in assembly.GetTypes())
            {
                if (type.FullName == primaryTypeName || typeof(Workspace).IsAssignableFrom(type))
                {
                    yield return type;
                }
            }
        }
    }

    private static readonly HashSet<string> UIComponentBaseTypes = new(StringComparer.Ordinal)
    {
        "System.Windows.Forms.UserControl",
        "System.Web.UI.UserControl",
        "System.Windows.Controls.UserControl",
        "Microsoft.AspNetCore.Components.ComponentBase",
        "Microsoft.Maui.Controls.ContentView",
        "Avalonia.Controls.UserControl",
    };

    private static readonly HashSet<string> VisualContainerBaseTypes = new(UIComponentBaseTypes, StringComparer.Ordinal)
    {
        // WinForms hosts
        "System.Windows.Forms.Form",
        "System.Windows.Forms.ContainerControl",
        // WebForms hosts
        "System.Web.UI.Page",
        "System.Web.UI.MasterPage",
        // WPF hosts
        "System.Windows.Window",
        "System.Windows.Controls.Page",
        // MAUI hosts
        "Microsoft.Maui.Controls.ContentPage",
        // Avalonia hosts
        "Avalonia.Controls.Window",
    };

    internal static bool IsUIComponent(INamedTypeSymbol type)
    {
        return HasBaseTypeIn(type, UIComponentBaseTypes);
    }

    internal static bool IsVisualContainerType(INamedTypeSymbol type)
    {
        return HasBaseTypeIn(type, VisualContainerBaseTypes);
    }

    private static bool HasBaseTypeIn(INamedTypeSymbol type, HashSet<string> baseTypeNames)
    {
        var current = type.BaseType;
        while (current is not null)
        {
            if (baseTypeNames.Contains(current.ToDisplayString()))
            {
                return true;
            }

            current = current.BaseType;
        }

        return false;
    }

    internal static async Task<IReadOnlyList<(INamedTypeSymbol Host, INamedTypeSymbol Component)>> FindComponentHostsAsync(
        INamedTypeSymbol componentType,
        RoslynSolution solution,
        int maxHostDepth,
        CancellationToken cancellationToken)
    {
        var results = new List<(INamedTypeSymbol Host, INamedTypeSymbol Component)>();
        var visited = new HashSet<ISymbol>(SymbolEqualityComparer.Default) { componentType };
        var queue = new Queue<(INamedTypeSymbol Type, int Depth)>();
        queue.Enqueue((componentType, 0));

        while (queue.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var (current, depth) = queue.Dequeue();

            var references = await SymbolFinder.FindReferencesAsync(
                current, solution, cancellationToken).ConfigureAwait(false);

            foreach (var referencedSymbol in references)
            {
                foreach (var location in referencedSymbol.Locations)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var document = solution.GetDocument(location.Document.Id);
                    if (document is null)
                    {
                        continue;
                    }

                    var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
                    var semanticModel = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
                    if (root is null || semanticModel is null)
                    {
                        continue;
                    }

                    var node = root.FindNode(location.Location.SourceSpan);
                    var hostType = FindEnclosingNamedType(node, semanticModel, cancellationToken);

                    if (hostType is null
                        || SymbolEqualityComparer.Default.Equals(hostType, current)
                        || !IsVisualContainerType(hostType))
                    {
                        continue;
                    }

                    if (!visited.Add(hostType))
                    {
                        continue;
                    }

                    results.Add((hostType, current));

                    if (depth + 1 < maxHostDepth && IsUIComponent(hostType))
                    {
                        queue.Enqueue((hostType, depth + 1));
                    }
                }
            }
        }

        return results;
    }

    private static INamedTypeSymbol? FindEnclosingNamedType(
        Microsoft.CodeAnalysis.SyntaxNode? node,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        while (node is not null)
        {
            var symbol = semanticModel.GetDeclaredSymbol(node, cancellationToken);
            if (symbol is INamedTypeSymbol namedType)
            {
                return namedType;
            }

            node = node.Parent;
        }

        return null;
    }

    internal sealed class RoslynSubjectPayload
    {
        public RoslynSubjectPayload(ISymbol symbol, RoslynSolution solution)
        {
            Symbol = symbol;
            Solution = solution;
        }

        public ISymbol Symbol { get; }

        public RoslynSolution Solution { get; }
    }
}
