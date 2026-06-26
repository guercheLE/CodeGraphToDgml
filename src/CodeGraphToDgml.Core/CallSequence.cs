using System.Collections.Generic;

namespace CodeGraphToDgml.Core;

public sealed record CallSequenceParticipant(string Id, string Label);

public sealed record CallSequenceCallNode(
    string CallerParticipantId,
    string CalleeParticipantId,
    string MessageLabel,
    IReadOnlyList<CallSequenceCallNode> NestedCalls);

public sealed class CallSequence
{
    public string Title { get; init; } = string.Empty;

    public IReadOnlyList<CallSequenceParticipant> Participants { get; init; } = System.Array.Empty<CallSequenceParticipant>();

    public IReadOnlyList<CallSequenceCallNode> RootCalls { get; init; } = System.Array.Empty<CallSequenceCallNode>();
}
