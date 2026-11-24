using System.Collections.Generic;
using CodeGraphToDgml.Core;
using CodeGraphToDgml.Roslyn;
using Microsoft.CodeAnalysis;
using static CodeGraphToDgml.Vsix.Providers.RoslynGraphHelpers;

namespace CodeGraphToDgml.Vsix.Providers;

// Traverse Down (DGML): breadth-first over the call sites inside each member body.
internal sealed partial class RoslynCallHierarchyProvider
{
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

                if (CallGraphDispatch.IsInterfaceMember(normalized))
                {
                    if (await AddInterfaceImplementationsAsync(graph, normalized, calleeProject, roslynSubject.Solution, normalizedOptions, depth, Enqueue, cancellationToken).ConfigureAwait(false))
                        return graph;
                }
                else if (CallGraphDispatch.CanBeOverridden(normalized))
                {
                    if (await AddOverridesAsync(graph, normalized, calleeProject, roslynSubject.Solution, normalizedOptions, depth, Enqueue, cancellationToken).ConfigureAwait(false))
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
}
