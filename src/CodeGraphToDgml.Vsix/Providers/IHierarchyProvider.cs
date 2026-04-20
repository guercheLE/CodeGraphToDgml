using CodeGraphToDgml.Core;

namespace CodeGraphToDgml.Vsix.Providers;

internal interface IHierarchyProvider
{
    string Id { get; }

    bool CouldSupport(string? filePath);

    Task<HierarchySubject?> TryResolveSubjectAsync(CancellationToken cancellationToken);

    Task<TraversalGraph> TraverseUpAsync(
        HierarchySubject subject,
        TraversalOptions options,
        IProgress<TraversalProgress>? progress,
        CancellationToken cancellationToken);

    Task<TraversalGraph> TraverseDownAsync(
        HierarchySubject subject,
        TraversalOptions options,
        IProgress<TraversalProgress>? progress,
        CancellationToken cancellationToken);
}
