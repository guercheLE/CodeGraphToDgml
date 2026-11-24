using CodeGraphToDgml.Core;
using CodeGraphToDgml.Roslyn;
using EnvDTE;
using EnvDTE80;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FindSymbols;
using RoslynDocument = Microsoft.CodeAnalysis.Document;
using RoslynSolution = Microsoft.CodeAnalysis.Solution;

namespace CodeGraphToDgml.Vsix.Providers;

/// <summary>
/// Roslyn-backed <see cref="IHierarchyProvider"/> for C# and Visual Basic. This file resolves the
/// subject at the caret; each traversal lives in its own part, split by the kind of call site
/// it follows:
/// <list type="bullet">
/// <item><c>.Callers.cs</c>: upward, call sites that call the subject (plus UI component hosts).</item>
/// <item><c>.Callees.cs</c>: downward, call sites inside the subject's body (DGML).</item>
/// <item><c>.Dispatch.cs</c>: virtual and interface call sites, resolved to their run-time targets.</item>
/// <item><c>.Sequence.cs</c>: downward into a sequence diagram, built by <see cref="CallSequenceBuilder"/>.</item>
/// <item><c>.Workspace.cs</c>: locating the Visual Studio Roslyn workspace.</item>
/// </list>
/// </summary>
internal sealed partial class RoslynCallHierarchyProvider : IHierarchyProvider
{
    private readonly ToolkitPackage _package;
    private readonly OutputWindowLogger _logger;

    public RoslynCallHierarchyProvider(ToolkitPackage package)
    {
        _package = package;
        _logger = new OutputWindowLogger(package);
    }

    public string Id => "Roslyn";

    public bool CouldSupport(string? filePath) => RoslynGraphHelpers.CouldSupport(filePath);

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

        var document = RoslynGraphHelpers.GetDocument(workspace.CurrentSolution, activeDocument.FullName);
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
