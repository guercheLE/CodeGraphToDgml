using System.Collections.Generic;
using CodeGraphToDgml.Core;
using CodeGraphToDgml.Roslyn;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FindSymbols;
using static CodeGraphToDgml.Vsix.Providers.RoslynGraphHelpers;

namespace CodeGraphToDgml.Vsix.Providers;

// Traverse Up: breadth-first over the call sites that call the subject, plus UI component hosts.
internal sealed partial class RoslynCallHierarchyProvider
{
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
}
