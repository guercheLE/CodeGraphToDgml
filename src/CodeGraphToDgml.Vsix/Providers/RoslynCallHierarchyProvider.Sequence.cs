using CodeGraphToDgml.Core;

namespace CodeGraphToDgml.Vsix.Providers;

// Traverse Down to Sequence Diagram: delegates to CallSequenceBuilder.
internal sealed partial class RoslynCallHierarchyProvider
{
    public Task<CallSequence> TraverseDownToSequenceAsync(
        HierarchySubject subject,
        TraversalOptions options,
        IProgress<TraversalProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (subject.Payload is not RoslynHierarchySubject roslynSubject)
            throw new InvalidOperationException("Unexpected hierarchy subject payload.");

        var builder = new CallSequenceBuilder(roslynSubject.Solution, options.Normalize(), progress, cancellationToken);
        return builder.BuildAsync(roslynSubject.Symbol, subject.DisplayName);
    }
}
