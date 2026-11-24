using System.Collections.Generic;

namespace CodeGraphToDgml.Core;

// Data model produced by SequenceSegmentPlanBuilder and consumed by MermaidSequenceEmitter and
// SequenceHtmlRenderer when a sequence is split into several diagrams.

// When a segment is a sub-part of an oversized call, that parent call's execution spans
// every sub-part. Rendered exactly like the «Caller» wrap: the sub-part that opens it shows
// the real opening arrow (caller->>+callee: label), sub-parts in between keep the bar alive
// with bare activate/deactivate, and the sub-part that closes it shows the return. A leaf
// segment can be nested inside several such spans at once — e.g. a big call split into
// 1A/1B/1C, where 1C is itself a single oversized call split further into 1C1/1C2 — so each
// leaf carries an ordered stack of frames, outermost first.
internal sealed class SplitFrame
{
    public string CallerParticipantId { get; set; } = "";
    public string CalleeParticipantId { get; set; } = "";
    public string MessageLabel { get; set; } = "";
    public string ReturnTypeLabel { get; set; } = "";
    public bool IsFirstSubPart { get; set; }
    public bool IsLastSubPart { get; set; }

    // The split call's own enclosing fragments: re-opened around the frame in every sub-part
    // (with a "(cont.)" marker on all but the sub-part that first opens each of them).
    public IReadOnlyList<CallSequenceFragmentScope> FragmentPath { get; set; } = [];

    // How many leading fragments of FragmentPath were already open before the split call
    // (shared with its previous sibling), so even the first sub-part marks them continued.
    public int ContinuedFragmentDepth { get; set; }
}

internal sealed class SegmentPlan
{
    public string PartNumber { get; set; } = "";
    public string Title { get; set; } = "";
    public int StartMessage { get; set; }
    public int EndMessage { get; set; }
    public List<string> ParticipantIds { get; set; } = new List<string>();
    public List<string> BoundaryFromPrev { get; set; } = new List<string>();

    public List<SplitFrame> SplitParents { get; set; } = new List<SplitFrame>();

    public string? PrevPart { get; set; }
    public string? NextPart { get; set; }
    public string? PrevTitle { get; set; }
    public string? NextTitle { get; set; }
    public List<CallSequenceCallNode> VirtualRootCalls { get; set; } = new List<CallSequenceCallNode>();

    // The sibling that precedes VirtualRootCalls[0] in the original call list, when the cut
    // fell between siblings; lets the renderer re-open fragments the cut split in two.
    public CallSequenceCallNode? PrevSibling { get; set; }

    // Item 2: whether this is the first/last segment of the whole (possibly multi-part)
    // diagram — used to decide where the synthetic «Caller» actor's opening/closing arrows
    // land vs. where it merely stays "in flight" via activate/deactivate.
    public bool IsFirstSegment { get; set; }
    public bool IsLastSegment { get; set; }
}

internal sealed record DiagramSection(string Heading, string Content, SegmentPlan? Plan = null);
