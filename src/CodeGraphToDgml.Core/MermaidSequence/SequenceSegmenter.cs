using System.Collections.Generic;
using System.Linq;

using static CodeGraphToDgml.Core.FragmentPaths;

namespace CodeGraphToDgml.Core;

// Layout heuristics: decides where a list of sibling calls is cut into segments so each one
// respects the participant and message caps, and where a natural business-flow seam lies.
// Knows nothing about part numbers, frames, or Mermaid text.
internal static class SequenceSegmenter
{
    // Heuristic knobs for the "business-flow boundary" signal in ShouldCutBefore. Deliberately
    // conservative and grounded in the call graph's actual structure (participant overlap)
    // rather than names/comments — expect to tune these against real large diagrams.
    private const double LowOverlapThreshold = 0.2;
    private const int MinPhaseParticipantsForSplit = 3;

    // Shared segmentation primitive used both for grouping top-level RootCalls into phases
    // (boundaryParticipants empty) and for splitting a single oversized call's NestedCalls into
    // sub-parts (boundaryParticipants = that call's caller+callee, always present in every
    // resulting segment). A segment boundary is drawn when the next call would push the segment
    // over the participant or message cap, or when it looks like the start of an unrelated
    // business flow (see ShouldCutBefore).
    internal static List<List<CallSequenceCallNode>> SegmentCalls(
        IReadOnlyList<CallSequenceCallNode> calls,
        HashSet<string> boundaryParticipants,
        int maxParticipants,
        int maxMessages,
        bool stackedBars)
    {
        var segments = new List<List<CallSequenceCallNode>>();
        var current = new List<CallSequenceCallNode>();
        var currentParticipants = new HashSet<string>(boundaryParticipants, StringComparer.Ordinal);
        int currentMessages = 0;

        foreach (var call in calls)
        {
            var callParticipants = GetSubtreeParticipants(call);
            int callMessages = CountArrowsInSubtree(call, stackedBars);

            // A soft (business-flow) seam is never drawn inside a control-flow fragment; only
            // the hard caps can cut there, and the renderer re-opens the fragment as "(cont.)".
            bool sharesFragment = current.Count > 0
                && CommonInstancePrefixLength(current[current.Count - 1].FragmentPath, call.FragmentPath) > 0;

            if (current.Count > 0 && ShouldCutBefore(
                    currentParticipants, currentMessages, callParticipants, callMessages,
                    boundaryParticipants, maxParticipants, maxMessages, sharesFragment))
            {
                segments.Add(current);
                current = new List<CallSequenceCallNode>();
                currentParticipants = new HashSet<string>(boundaryParticipants, StringComparer.Ordinal);
                currentMessages = 0;
            }

            current.Add(call);
            currentParticipants.UnionWith(callParticipants);
            currentMessages += callMessages;
        }

        if (current.Count > 0)
            segments.Add(current);

        MergeTinySegments(segments, boundaryParticipants, maxParticipants, maxMessages, stackedBars);

        return segments;
    }

    // Re-attaches "orphan" segments (a handful of messages stranded by a semantic seam or a
    // cap-forced cut) to an adjacent segment when the merged result still respects both caps.
    // Cap-forced boundaries survive automatically — merging across one would exceed a cap — so
    // only soft seams get undone. Skipped when no message cap is configured, since "tiny" is
    // defined relative to that cap.
    private static void MergeTinySegments(
        List<List<CallSequenceCallNode>> segments,
        HashSet<string> boundaryParticipants,
        int maxParticipants,
        int maxMessages,
        bool stackedBars)
    {
        if (segments.Count <= 1 || maxMessages == int.MaxValue)
            return;

        int tinyThreshold = Math.Max(2, maxMessages / 10);

        int i = 0;
        while (i < segments.Count && segments.Count > 1)
        {
            if (CountArrows(segments[i], stackedBars) >= tinyThreshold)
            {
                i++;
                continue;
            }

            if (i > 0 && CanMergeSegments(segments[i - 1], segments[i], boundaryParticipants, maxParticipants, maxMessages, stackedBars))
            {
                segments[i - 1].AddRange(segments[i]);
                segments.RemoveAt(i);
                i--; // the merged segment could itself still be tiny — recheck it
            }
            else if (i < segments.Count - 1 && CanMergeSegments(segments[i], segments[i + 1], boundaryParticipants, maxParticipants, maxMessages, stackedBars))
            {
                segments[i].AddRange(segments[i + 1]);
                segments.RemoveAt(i + 1);
            }
            else
            {
                i++;
            }
        }
    }

    private static bool CanMergeSegments(
        List<CallSequenceCallNode> a,
        List<CallSequenceCallNode> b,
        HashSet<string> boundaryParticipants,
        int maxParticipants,
        int maxMessages,
        bool stackedBars)
    {
        if (CountArrows(a, stackedBars) + CountArrows(b, stackedBars) > maxMessages)
            return false;

        var combined = new HashSet<string>(boundaryParticipants, StringComparer.Ordinal);
        foreach (var call in a)
            combined.UnionWith(GetSubtreeParticipants(call));
        foreach (var call in b)
            combined.UnionWith(GetSubtreeParticipants(call));
        return combined.Count <= maxParticipants;
    }

    private static bool ShouldCutBefore(
        HashSet<string> segParticipants,
        int segMessages,
        HashSet<string> nextParticipants,
        int nextMessages,
        HashSet<string> boundaryParticipants,
        int maxParticipants,
        int maxMessages,
        bool sharesFragment = false)
    {
        var combined = new HashSet<string>(segParticipants, StringComparer.Ordinal);
        combined.UnionWith(nextParticipants);
        if (combined.Count > maxParticipants)
            return true;

        if (segMessages + nextMessages > maxMessages)
            return true;

        if (sharesFragment)
            return false;

        // Natural business-flow seam: the next call barely overlaps what's accumulated so far
        // (excluding always-present boundary participants), and the segment already has enough
        // substance to stand alone — a concrete, deterministic proxy for "unrelated flow
        // starting," grounded in actual participant structure rather than names/comments.
        int meaningfulSegSize = segParticipants.Count(p => !boundaryParticipants.Contains(p));
        if (meaningfulSegSize < MinPhaseParticipantsForSplit)
            return false;

        int shared = nextParticipants.Count(p => segParticipants.Contains(p));
        double overlapRatio = nextParticipants.Count == 0 ? 1.0 : (double)shared / nextParticipants.Count;
        return overlapRatio <= LowOverlapThreshold;
    }

    // ── Participant helpers ───────────────────────────────────────────────────

    internal static HashSet<string> GetSubtreeParticipants(CallSequenceCallNode call)
    {
        var set = new HashSet<string>(StringComparer.Ordinal)
        {
            call.CallerParticipantId,
            call.CalleeParticipantId,
        };
        foreach (var nested in call.NestedCalls)
            set.UnionWith(GetSubtreeParticipants(nested));
        return set;
    }

    internal static HashSet<string> GetCallListParticipants(List<CallSequenceCallNode> calls)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (var call in calls)
            set.UnionWith(GetSubtreeParticipants(call));
        return set;
    }

    // ── Arrow counting ────────────────────────────────────────────────────────

    internal static int CountArrows(IReadOnlyList<CallSequenceCallNode> calls, bool stackedBars)
    {
        int count = 0;
        foreach (var call in calls)
            count += CountArrowsInSubtree(call, stackedBars);
        return count;
    }

    internal static int CountArrowsInSubtree(CallSequenceCallNode call, bool stackedBars)
    {
        int count = 1; // call arrow
        foreach (var nested in call.NestedCalls)
            count += CountArrowsInSubtree(nested, stackedBars);
        if (stackedBars)
            count += 1; // return arrow
        return count;
    }
}
