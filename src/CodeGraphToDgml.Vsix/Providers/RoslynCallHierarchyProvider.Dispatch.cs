using System.Collections.Generic;
using CodeGraphToDgml.Core;
using CodeGraphToDgml.Roslyn;
using Microsoft.CodeAnalysis;
using RoslynSolution = Microsoft.CodeAnalysis.Solution;
using static CodeGraphToDgml.Vsix.Providers.RoslynGraphHelpers;

namespace CodeGraphToDgml.Vsix.Providers;

// Interface and virtual call sites: adds the members that may actually run (Implements / Overrides links).
internal sealed partial class RoslynCallHierarchyProvider
{
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
        var implementations = await CallGraphDispatch.FindImplementationsAsync(
            interfaceMember, solution, cancellationToken).ConfigureAwait(false);

        return AddDispatchTargets(graph, interfaceMember, interfaceProject, solution, options, depth, enqueue, implementations, "Implements");
    }

    /// <summary>
    /// For a callee that is virtual, abstract, or a non-sealed override, finds every override in
    /// the solution, adds each with an <c>Overrides</c> link from the base member to the override,
    /// and enqueues it for further traversal, so the graph shows the members that may actually run.
    /// Returns <see langword="true"/> when the node limit has been reached.
    /// </summary>
    private static async Task<bool> AddOverridesAsync(
        TraversalGraph graph,
        ISymbol member,
        string? memberProject,
        RoslynSolution solution,
        TraversalOptions options,
        int depth,
        Action<ISymbol, int> enqueue,
        CancellationToken cancellationToken)
    {
        var overrides = await CallGraphDispatch.FindOverridesAsync(
            member, solution, cancellationToken).ConfigureAwait(false);

        return AddDispatchTargets(graph, member, memberProject, solution, options, depth, enqueue, overrides, "Overrides");
    }

    private static bool AddDispatchTargets(
        TraversalGraph graph,
        ISymbol member,
        string? memberProject,
        RoslynSolution solution,
        TraversalOptions options,
        int depth,
        Action<ISymbol, int> enqueue,
        IReadOnlyList<ISymbol> targets,
        string linkCategory)
    {
        foreach (var target in targets)
        {
            if (!CallGraphFilters.IsAllowed(target, options))
            {
                continue;
            }

            var targetProject = target.ContainingAssembly?.Name;
            AddNodeAndContainers(graph, target, targetProject, solution);

            graph.AddLink(new GraphLink(
                GetProjectScopedId(member, memberProject),
                GetProjectScopedId(target, targetProject),
                linkCategory));

            if (graph.NodeCount >= options.MaxNodeCount) return true;
            enqueue(target, depth + 1);
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
}
