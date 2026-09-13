using System.Collections.Generic;

namespace CodeGraphToDgml.Core;

public sealed record CallSequenceParticipant(string Id, string Label);

/// <summary>
/// Mermaid combined-fragment kinds a call can be enclosed in.
/// <c>Alt</c> renders sections separated by <c>else</c>, <c>Par</c> by <c>and</c>,
/// <c>Critical</c> by <c>option</c>; <c>Opt</c>, <c>Loop</c>, and <c>Break</c> have one section.
/// </summary>
public enum SequenceFragmentKind
{
    Alt,
    Opt,
    Loop,
    Break,
    Par,
    Critical,
}

/// <summary>
/// One enclosing fragment level of a call, relative to the sibling list the call lives in.
/// </summary>
/// <param name="InstanceId">
/// Identifies the fragment instance among siblings, so two consecutive <c>if (x)</c> statements
/// (different instances, same label) are rendered as two fragments, while two calls inside one
/// <c>if</c> share a fragment.
/// </param>
/// <param name="Kind">The Mermaid fragment keyword.</param>
/// <param name="SectionIndex">Zero-based section within the instance (0 = the first branch).</param>
/// <param name="Label">Section label (condition text, loop header, <c>else</c>, ...).</param>
public sealed record CallSequenceFragmentScope(int InstanceId, SequenceFragmentKind Kind, int SectionIndex, string Label);

public sealed record CallSequenceCallNode(
    string CallerParticipantId,
    string CalleeParticipantId,
    string MessageLabel,
    IReadOnlyList<CallSequenceCallNode> NestedCalls,
    string ReturnTypeLabel = "")
{
    /// <summary>
    /// Control-flow fragments enclosing this call within its sibling list, outermost first.
    /// Empty for plain calls, which is also the default, so callers that do not model control
    /// flow are unaffected. Adjacent siblings sharing a prefix of the same fragment instances are
    /// rendered inside one fragment; a fragment never appears without at least one call inside.
    /// </summary>
    public IReadOnlyList<CallSequenceFragmentScope> FragmentPath { get; init; } = [];
}

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
