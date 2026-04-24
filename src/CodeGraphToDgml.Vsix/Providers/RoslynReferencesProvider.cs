using System.Collections.Generic;
using System.Linq;
using CodeGraphToDgml.Core;
using EnvDTE;
using EnvDTE80;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FindSymbols;
using RoslynDocument = Microsoft.CodeAnalysis.Document;
using RoslynSolution = Microsoft.CodeAnalysis.Solution;

namespace CodeGraphToDgml.Vsix.Providers;

/// <summary>
/// Builds a reference graph that mirrors Visual Studio's built-in
/// "Find All References" (Shift+F12) command. For the symbol resolved at the caret,
/// every <see cref="ReferenceLocation"/> reported by
/// <see cref="SymbolFinder.FindReferencesAsync(ISymbol, RoslynSolution, System.Threading.CancellationToken)"/>
/// becomes a "References" link from the enclosing referrer (method/property/event/type)
/// to the target symbol.
/// </summary>
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

        int activeLine = selection.ActivePoint.Line;
        int activeColumn = selection.ActivePoint.LineCharOffset;

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

        var position = await RoslynGraphHelpers.GetPositionFromLinesAsync(document, activeLine, activeColumn, cancellationToken).ConfigureAwait(false);

        await _logger.WriteLineAsync($"[ResolveReferencesSubject] Position={position}, File={activeDocument.FullName}").ConfigureAwait(false);

        var symbol = await ResolveSymbolAsync(document, workspace, position, cancellationToken).ConfigureAwait(false);

        await _logger.WriteLineAsync($"[ResolveReferencesSubject] Raw symbol: {symbol?.ToDisplayString() ?? "(null)"}, Kind={symbol?.Kind}").ConfigureAwait(false);

        symbol = RoslynGraphHelpers.NormalizeSymbol(symbol);

        await _logger.WriteLineAsync($"[ResolveReferencesSubject] Normalized symbol: {symbol?.ToDisplayString() ?? "(null)"}, Kind={symbol?.Kind}").ConfigureAwait(false);

        if (symbol is null || !IsSupportedReferenceSymbol(symbol))
        {
            await _logger.WriteLineAsync("[ResolveReferencesSubject] Symbol rejected: not a supported reference target.").ConfigureAwait(false);
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

        var target = RoslynGraphHelpers.NormalizeSymbol(payload.Symbol)
            ?? throw new InvalidOperationException("Reference target symbol could not be resolved.");

        var normalizedOptions = options.Normalize();
        var graph = new TraversalGraph();

        var targetProject = target.ContainingAssembly?.Name;
        RoslynGraphHelpers.AddNodeAndContainers(graph, target, targetProject, payload.Solution);
        var targetId = RoslynGraphHelpers.GetProjectScopedId(target, targetProject);

        progress?.Report(new TraversalProgress(
            "Finding references",
            0,
            graph.NodeCount,
            target.ToDisplayString()));

        // Same Roslyn API the editor's "Find All References" (Shift+F12) is built on.
        var references = await SymbolFinder.FindReferencesAsync(
            target,
            payload.Solution,
            cancellationToken).ConfigureAwait(false);

        var addedReferrers = new HashSet<string>(StringComparer.Ordinal);

        foreach (var referencedSymbol in references)
        {
            cancellationToken.ThrowIfCancellationRequested();

            foreach (var location in referencedSymbol.Locations)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (location.IsImplicit || !location.Location.IsInSource)
                {
                    continue;
                }

                var referrer = await GetReferrerSymbolAsync(location, cancellationToken).ConfigureAwait(false);
                if (referrer is null || !IsAllowed(referrer, normalizedOptions))
                {
                    continue;
                }

                // Self-reference (e.g. recursive method) â€” keep target node, skip the loopback link.
                if (SymbolEqualityComparer.Default.Equals(referrer, target))
                {
                    continue;
                }

                var referrerProject = referrer.ContainingAssembly?.Name;
                RoslynGraphHelpers.AddNodeAndContainers(graph, referrer, referrerProject, payload.Solution);

                var referrerId = RoslynGraphHelpers.GetProjectScopedId(referrer, referrerProject);
                graph.AddLink(new GraphLink(referrerId, targetId, "References"));

                if (addedReferrers.Add(referrerId))
                {
                    progress?.Report(new TraversalProgress(
                        "Finding references",
                        1,
                        graph.NodeCount,
                        referrer.ToDisplayString()));
                }

                if (graph.NodeCount >= normalizedOptions.MaxNodeCount)
                {
                    return graph;
                }
            }
        }

        return graph;
    }

    private static async Task<ISymbol?> GetReferrerSymbolAsync(
        ReferenceLocation location,
        CancellationToken cancellationToken)
    {
        var document = location.Document;
        if (document is null)
        {
            return null;
        }

        var semanticModel = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
        if (semanticModel is null)
        {
            return null;
        }

        var root = await semanticModel.SyntaxTree.GetRootAsync(cancellationToken).ConfigureAwait(false);
        var token = root.FindToken(location.Location.SourceSpan.Start);
        var node = token.Parent;

        // Walk up to the nearest real declared symbol so the graph never contains
        // anonymous lambdas, local functions, or accessor synthetics.
        while (node is not null)
        {
            var declared = semanticModel.GetDeclaredSymbol(node, cancellationToken);
            if (declared is not null)
            {
                var normalized = RoslynGraphHelpers.NormalizeSymbol(declared);
                if (normalized is not null && IsReferrerCandidate(normalized))
                {
                    return normalized;
                }
            }

            node = node.Parent;
        }

        return null;
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

        // Prefer the symbol referenced at the caret (e.g. the type in `new Foo()`,
        // the method in `Bar.Baz()`). Matches what Shift+F12 binds to.
        var symbol = await SymbolFinder.FindSymbolAtPositionAsync(
            semanticModel,
            position,
            workspace,
            cancellationToken).ConfigureAwait(false);

        if (symbol is not null && IsSupportedReferenceSymbol(symbol))
        {
            return symbol;
        }

        // Fallback: find an enclosing declaration (caret is on the declaration's identifier
        // or inside its body without binding to a specific symbol).
        var root = await semanticModel.SyntaxTree.GetRootAsync(cancellationToken).ConfigureAwait(false);
        var token = root.FindToken(position);
        var node = token.Parent;

        while (node is not null)
        {
            var declared = semanticModel.GetDeclaredSymbol(node, cancellationToken);
            if (declared is not null && IsSupportedReferenceSymbol(declared))
            {
                return declared;
            }

            node = node.Parent;
        }

        return symbol;
    }

    /// <summary>
    /// Symbol kinds we will look up references for. Mirrors the breadth of
    /// the built-in "Find All References" command â€” types and members.
    /// </summary>
    private static bool IsSupportedReferenceSymbol(ISymbol symbol)
    {
        return symbol.Kind is SymbolKind.NamedType
            or SymbolKind.Method
            or SymbolKind.Property
            or SymbolKind.Event
            or SymbolKind.Field;
    }

    /// <summary>
    /// Symbol kinds that are meaningful as graph nodes representing the referrer.
    /// </summary>
    private static bool IsReferrerCandidate(ISymbol symbol)
    {
        return symbol.Kind is SymbolKind.Method
            or SymbolKind.Property
            or SymbolKind.Event
            or SymbolKind.Field
            or SymbolKind.NamedType;
    }

    private static bool IsAllowed(ISymbol symbol, TraversalOptions options)
    {
        var sourceLocation = symbol.Locations.FirstOrDefault(l => l.IsInSource);

        if (!options.IncludeExternalSymbols && sourceLocation is null)
        {
            return false;
        }

        if (!options.IncludeGeneratedCode
            && sourceLocation is not null
            && IsGenerated(sourceLocation.SourceTree?.FilePath))
        {
            return false;
        }

        if (symbol.Kind == SymbolKind.Property && !options.IncludeProperties)
        {
            return false;
        }

        if (symbol.Kind == SymbolKind.Event && !options.IncludeEvents)
        {
            return false;
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
