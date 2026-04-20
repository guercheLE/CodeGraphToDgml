using System.Collections.Generic;
using System.Linq;
using CodeGraphToDgml.Core;
using EnvDTE;
using EnvDTE80;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FindSymbols;
using RoslynSolution = Microsoft.CodeAnalysis.Solution;

namespace CodeGraphToDgml.Vsix.Providers;

internal sealed class RoslynReferencesProvider
{
    private readonly ToolkitPackage _package;
    private readonly OutputWindowLogger _logger;

    public RoslynReferencesProvider(ToolkitPackage package)
    {
        _package = package;
        _logger = new OutputWindowLogger(package);
    }

    public bool CouldSupport(string? filePath)
    {
        return RoslynGraphHelpers.CouldSupport(filePath);
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

        var workspace = await RoslynGraphHelpers.GetWorkspaceAsync(_package, cancellationToken).ConfigureAwait(true);
        if (workspace is null)
        {
            return null;
        }

        var document = RoslynGraphHelpers.GetDocument(workspace.CurrentSolution, activeDocument.FullName);
        if (document is null)
        {
            return null;
        }

        var position = await RoslynGraphHelpers.GetPositionFromSelectionAsync(document, selection, cancellationToken).ConfigureAwait(false);

        await _logger.WriteLineAsync($"[ResolveReferencesSubject] Position={position}, File={activeDocument.FullName}").ConfigureAwait(false);

        var symbol = await ResolveTypeSymbolAsync(document, workspace, position, cancellationToken).ConfigureAwait(false);

        await _logger.WriteLineAsync($"[ResolveReferencesSubject] Resolved symbol: {symbol?.ToDisplayString() ?? "(null)"}, Kind={symbol?.Kind}").ConfigureAwait(false);

        if (symbol is not INamedTypeSymbol)
        {
            await _logger.WriteLineAsync($"[ResolveReferencesSubject] Symbol rejected: not a named type.").ConfigureAwait(false);
            return null;
        }

        return new HierarchySubject(
            "RoslynReferences",
            symbol.ToDisplayString(),
            new RoslynGraphHelpers.RoslynSubjectPayload(symbol, workspace.CurrentSolution));
    }

    public async Task<TraversalGraph> FindReferencesAsync(
        HierarchySubject subject,
        TraversalOptions options,
        IProgress<TraversalProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (subject.Payload is not RoslynGraphHelpers.RoslynSubjectPayload payload)
        {
            throw new InvalidOperationException("Unexpected hierarchy subject payload.");
        }

        if (payload.Symbol is not INamedTypeSymbol rootType)
        {
            throw new InvalidOperationException("Expected a named type symbol.");
        }

        var normalizedOptions = options.Normalize();
        var graph = new TraversalGraph();
        var visitedIds = new HashSet<string>(StringComparer.Ordinal);
        var queue = new Queue<(INamedTypeSymbol Type, int Depth)>();

        var rootProject = rootType.ContainingAssembly?.Name;
        RoslynGraphHelpers.AddNodeAndContainers(graph, rootType, rootProject);
        visitedIds.Add(RoslynGraphHelpers.GetProjectScopedId(rootType, rootProject));
        queue.Enqueue((rootType, 0));

        while (queue.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var (currentType, depth) = queue.Dequeue();
            progress?.Report(new TraversalProgress(
                "Finding references",
                depth,
                graph.NodeCount,
                currentType.ToDisplayString()));

            if (depth >= normalizedOptions.MaxDepth)
            {
                continue;
            }

            // Find derived classes
            var derivedClasses = await SymbolFinder.FindDerivedClassesAsync(
                currentType,
                payload.Solution,
                transitive: false,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            foreach (var derived in derivedClasses)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var derivedOriginal = derived.OriginalDefinition;
                if (!IsAllowed(derivedOriginal, normalizedOptions))
                {
                    continue;
                }

                var derivedProject = derivedOriginal.ContainingAssembly?.Name;
                RoslynGraphHelpers.AddNodeAndContainers(graph, derivedOriginal, derivedProject);

                graph.AddLink(new GraphLink(
                    RoslynGraphHelpers.GetProjectScopedId(currentType, currentType.ContainingAssembly?.Name),
                    RoslynGraphHelpers.GetProjectScopedId(derivedOriginal, derivedProject),
                    "Overrides"));

                if (graph.NodeCount >= normalizedOptions.MaxNodeCount)
                {
                    return graph;
                }

                if (visitedIds.Add(RoslynGraphHelpers.GetProjectScopedId(derivedOriginal, derivedProject)))
                {
                    queue.Enqueue((derivedOriginal, depth + 1));
                }
            }

            // Find implementations (if the current type is an interface)
            if (currentType.TypeKind == TypeKind.Interface)
            {
                var implementations = await SymbolFinder.FindImplementationsAsync(
                    currentType,
                    payload.Solution,
                    transitive: false,
                    cancellationToken: cancellationToken).ConfigureAwait(false);

                foreach (var impl in implementations)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (impl is not INamedTypeSymbol implType)
                    {
                        continue;
                    }

                    var implOriginal = implType.OriginalDefinition;
                    if (!IsAllowed(implOriginal, normalizedOptions))
                    {
                        continue;
                    }

                    var implProject = implOriginal.ContainingAssembly?.Name;
                    RoslynGraphHelpers.AddNodeAndContainers(graph, implOriginal, implProject);

                    graph.AddLink(new GraphLink(
                        RoslynGraphHelpers.GetProjectScopedId(currentType, currentType.ContainingAssembly?.Name),
                        RoslynGraphHelpers.GetProjectScopedId(implOriginal, implProject),
                        "Implements"));

                    if (graph.NodeCount >= normalizedOptions.MaxNodeCount)
                    {
                        return graph;
                    }

                    if (visitedIds.Add(RoslynGraphHelpers.GetProjectScopedId(implOriginal, implProject)))
                    {
                        queue.Enqueue((implOriginal, depth + 1));
                    }
                }
            }
        }

        return graph;
    }

    private static async Task<ISymbol?> ResolveTypeSymbolAsync(
        Microsoft.CodeAnalysis.Document document,
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

        while (node is not null)
        {
            var declaredSymbol = semanticModel.GetDeclaredSymbol(node, cancellationToken);
            if (declaredSymbol is INamedTypeSymbol)
            {
                return declaredSymbol;
            }

            node = node.Parent;
        }

        var symbol = await SymbolFinder.FindSymbolAtPositionAsync(
            semanticModel,
            position,
            workspace,
            cancellationToken).ConfigureAwait(false);

        if (symbol is INamedTypeSymbol)
        {
            return symbol;
        }

        return null;
    }

    private static bool IsAllowed(INamedTypeSymbol type, TraversalOptions options)
    {
        if (!options.IncludeExternalSymbols)
        {
            var sourceLocation = type.Locations.FirstOrDefault(l => l.IsInSource);
            if (sourceLocation is null)
            {
                return false;
            }
        }

        if (!options.IncludeGeneratedCode)
        {
            var sourceLocation = type.Locations.FirstOrDefault(l => l.IsInSource);
            if (sourceLocation is not null && IsGenerated(sourceLocation.SourceTree?.FilePath))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsGenerated(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return false;
        }

        var fileName = System.IO.Path.GetFileName(filePath) ?? string.Empty;
        return fileName.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase)
            || fileName.EndsWith(".g.vb", StringComparison.OrdinalIgnoreCase)
            || fileName.EndsWith(".designer.cs", StringComparison.OrdinalIgnoreCase)
            || fileName.EndsWith(".designer.vb", StringComparison.OrdinalIgnoreCase)
            || fileName.EndsWith(".generated.cs", StringComparison.OrdinalIgnoreCase)
            || fileName.EndsWith(".generated.vb", StringComparison.OrdinalIgnoreCase)
            || filePath!.IndexOf("\\obj\\", StringComparison.OrdinalIgnoreCase) >= 0;
    }
}
