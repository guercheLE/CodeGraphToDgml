using System.Collections.Generic;

namespace CodeGraphToDgml.Core;

public sealed record CallSequenceParticipant(string Id, string Label);

public sealed record CallSequenceCallNode(
    string CallerParticipantId,
    string CalleeParticipantId,
    string MessageLabel,
    IReadOnlyList<CallSequenceCallNode> NestedCalls,
    string ReturnTypeLabel = "");

public sealed class CallSequence
{
    public string Title { get; init; } = string.Empty;

    public IReadOnlyList<CallSequenceParticipant> Participants { get; init; } = [];

    public IReadOnlyList<CallSequenceCallNode> RootCalls { get; init; } = [];

    /// <summary>
    /// The participant id of the root/entry method itself (i.e. what would be
    /// <c>RootCalls[0].CallerParticipantId</c> by construction, but explicit so it's available
    /// even when <see cref="RootCalls"/> is empty). Used to give the entry method its own
    /// activation bar via a synthetic «Caller» actor.
    /// </summary>
    public string RootParticipantId { get; init; } = string.Empty;

    /// <summary>
    /// The bare method-name label of the root/entry method (e.g. "btnLiberar_Click"), used as
    /// the message label on the synthetic «Caller» actor's call into the entry method.
    /// </summary>
    public string RootMethodLabel { get; init; } = string.Empty;

    /// <summary>
    /// The root/entry method's declared return type (e.g. "int", "bool"), rendered on its return
    /// arrow alongside <see cref="RootMethodLabel"/> so the return is identifiable even when it
    /// lands far from its activation (e.g. the last part of a multi-part split). Empty for void
    /// methods or when unknown.
    /// </summary>
    public string RootReturnTypeLabel { get; init; } = string.Empty;
}
