using System.Collections.Generic;
using System.Text;

using static CodeGraphToDgml.Core.FragmentPaths;
using static CodeGraphToDgml.Core.SequenceSegmenter;

namespace CodeGraphToDgml.Core;

// Model builder: turns a CallSequence that exceeds the caps into an ordered list of
// SegmentPlans (part numbers, titles, participants, split-parent frames, message numbering,
// neighbour links). Cutting decisions are delegated to SequenceSegmenter; rendering is left to
// MermaidSequenceEmitter.
internal static class SequenceSegmentPlanBuilder
{
    private static readonly HashSet<string> NoBoundary = new(StringComparer.Ordinal);

    internal static List<SegmentPlan> Build(CallSequence sequence, int maxParticipants, int maxMessages, bool stackedBars)
    {
        // Item 2: the root method's own participant is treated as an always-present boundary
        // across top-level phases too (mirroring how a split parent's caller/callee are treated
        // for sub-parts below) — it keeps the «Caller» wrap's target participant declared in
        // every phase, and correctly excludes it from the low-overlap heuristic's "meaningful
        // participants" count. Empty (not {""}) when RootParticipantId is unset (legacy/hand-built
        // sequences), so behavior is unchanged for callers that don't populate it.
        var rootBoundary = string.IsNullOrEmpty(sequence.RootParticipantId)
            ? NoBoundary
            : new HashSet<string>(StringComparer.Ordinal) { sequence.RootParticipantId };

        // Phase 1: group root calls into natural phases.
        var phases = SegmentCalls(sequence.RootCalls, rootBoundary, maxParticipants, maxMessages, stackedBars);

        var allPlans = new List<SegmentPlan>();
        int phaseNumber = 1;

        for (int i = 0; i < phases.Count; i++)
        {
            var phase = phases[i];
            var phaseParts = GetCallListParticipants(phase);
            phaseParts.UnionWith(rootBoundary);

            var prevSibling = i == 0 ? null : phases[i - 1][phases[i - 1].Count - 1];
            allPlans.AddRange(ExpandPhase(phase, phaseParts, phaseNumber.ToString(), depth: 1,
                maxParticipants, maxMessages, stackedBars, prevSibling));

            phaseNumber++;
        }

        // Every non-first segment emits "activate <root>" to keep the «Caller» wrap's bar
        // alive, so the root participant must be declared in every segment — deep sub-part
        // recursion rebuilds participant sets from its own boundary and can drop it.
        foreach (var plan in allPlans)
            foreach (var rb in rootBoundary)
                AddIfMissing(plan.ParticipantIds, rb);

        // Compute boundary participants (participants shared between adjacent segments)
        for (int i = 1; i < allPlans.Count; i++)
        {
            var prevSet = new HashSet<string>(allPlans[i - 1].ParticipantIds, StringComparer.Ordinal);
            allPlans[i].BoundaryFromPrev = allPlans[i].ParticipantIds
                .Where(p => prevSet.Contains(p))
                .ToList();
        }

        if (allPlans.Count > 0)
        {
            allPlans[0].IsFirstSegment = true;
            allPlans[allPlans.Count - 1].IsLastSegment = true;
        }

        // Global message numbering, done last because it depends on the first/last flags: the
        // «Caller» wrap and split-parent arrows are real numbered arrows in the rendered
        // diagrams, so they must be counted or every subsequent autonumber start drifts.
        bool hasCallerWrap = !string.IsNullOrEmpty(sequence.RootParticipantId);
        int msgCounter = 1;
        foreach (var plan in allPlans)
        {
            int arrows = CountArrows(plan.VirtualRootCalls, stackedBars);
            if (hasCallerWrap)
            {
                if (plan.IsFirstSegment) arrows++;                    // Caller->>Root opening arrow
                if (plan.IsLastSegment && stackedBars) arrows++;      // Root-->>Caller return arrow
            }
            foreach (var frame in plan.SplitParents)
            {
                if (frame.IsFirstSubPart) arrows++;                   // parent call opening arrow
                if (frame.IsLastSubPart && stackedBars) arrows++;     // parent call return arrow
            }

            plan.StartMessage = msgCounter;
            plan.EndMessage = arrows > 0 ? msgCounter + arrows - 1 : msgCounter;
            msgCounter += arrows > 0 ? arrows : 1;
        }

        // Link each plan to its neighbours for the continuation banners and headings.
        for (int i = 0; i < allPlans.Count; i++)
        {
            if (i > 0)
            {
                allPlans[i].PrevPart = allPlans[i - 1].PartNumber;
                allPlans[i].PrevTitle = allPlans[i - 1].Title;
            }
            if (i < allPlans.Count - 1)
            {
                allPlans[i].NextPart = allPlans[i + 1].PartNumber;
                allPlans[i].NextTitle = allPlans[i + 1].Title;
            }
        }

        return allPlans;
    }

    // Recursively expands one phase (or one sub-part of an already-split call) into leaf
    // segments that each fit the caps. A phase can only still be oversized here because it's a
    // single call whose own subtree exceeds a cap (SegmentCalls already keeps every multi-call
    // group under both) — in that case, split that call's NestedCalls the same way, and recurse
    // into each resulting piece so a sub-part that's itself oversized keeps splitting instead of
    // being emitted as one big chunk. Descent continues even when the nested calls form a single
    // sub-phase (the controller→handler→ExecuteAsync shape, where each level has one dominant
    // child), so a deep-but-narrow monolith still gets sliced at the level where siblings appear.
    private static List<SegmentPlan> ExpandPhase(
        List<CallSequenceCallNode> calls,
        HashSet<string> participants,
        string partNumber,
        int depth,
        int maxParticipants,
        int maxMessages,
        bool stackedBars,
        CallSequenceCallNode? prevSibling)
    {
        bool fits = participants.Count <= maxParticipants
            && CountArrows(calls, stackedBars) <= maxMessages;
        if (fits || calls.Count != 1)
            return [MakeLeafPlan(calls, participants, partNumber, prevSibling)];

        var parentCall = calls[0];
        if (parentCall.NestedCalls.Count == 0)
            return [MakeLeafPlan(calls, participants, partNumber, prevSibling)];

        var boundary = new HashSet<string>(StringComparer.Ordinal)
        {
            parentCall.CallerParticipantId,
            parentCall.CalleeParticipantId,
        };

        var subPhases = SegmentCalls(parentCall.NestedCalls, boundary, maxParticipants, maxMessages, stackedBars);

        var leaves = new List<SegmentPlan>();
        for (int i = 0; i < subPhases.Count; i++)
        {
            var subCalls = subPhases[i];
            var subParts = GetCallListParticipants(subCalls);
            subParts.UnionWith(boundary);

            // A lone sub-phase has no sibling to disambiguate from, so it inherits the parent's
            // part number and depth unchanged (avoids a "Part 1A" with no 1B, and keeps the
            // letter/digit suffix alternation tracking actual split levels, not descent levels).
            var childPartNumber = subPhases.Count == 1 ? partNumber : ChildPartNumber(partNumber, i, depth);
            var childDepth = subPhases.Count == 1 ? depth : depth + 1;
            var subPrevSibling = i == 0 ? null : subPhases[i - 1][subPhases[i - 1].Count - 1];
            leaves.AddRange(ExpandPhase(subCalls, subParts, childPartNumber, childDepth,
                maxParticipants, maxMessages, stackedBars, subPrevSibling));
        }

        // Nothing actually split (however deep we descended, no divisible level was found) —
        // emit the original call as one leaf rather than wrapping it in a pointless frame.
        if (leaves.Count == 1)
            return [MakeLeafPlan(calls, participants, partNumber, prevSibling)];

        // The parent call is "in flight" across every leaf produced above — the first leaf opens
        // it with the real arrow, the last leaf closes it, everything in between just keeps its
        // bars alive. Its enclosing fragments are re-opened around it in every leaf.
        int continuedDepth = CommonInstancePrefixLength(prevSibling?.FragmentPath, parentCall.FragmentPath);
        for (int i = 0; i < leaves.Count; i++)
        {
            leaves[i].SplitParents.Insert(0, new SplitFrame
            {
                CallerParticipantId = parentCall.CallerParticipantId,
                CalleeParticipantId = parentCall.CalleeParticipantId,
                MessageLabel = parentCall.MessageLabel,
                ReturnTypeLabel = parentCall.ReturnTypeLabel,
                IsFirstSubPart = i == 0,
                IsLastSubPart = i == leaves.Count - 1,
                FragmentPath = parentCall.FragmentPath,
                ContinuedFragmentDepth = continuedDepth,
            });

            // This frame's caller/callee lifelines get activate/deactivate lines in every leaf,
            // so they must be declared in every leaf. Deeper recursion levels rebuild each
            // leaf's participant set from their own (innermost) boundary only, which drops the
            // outer frames' participants — and Mermaid 11 throws (TypeError reading '.x')
            // when asked to activate an actor the diagram never declared or messaged.
            AddIfMissing(leaves[i].ParticipantIds, parentCall.CallerParticipantId);
            AddIfMissing(leaves[i].ParticipantIds, parentCall.CalleeParticipantId);
        }

        return leaves;
    }

    private static SegmentPlan MakeLeafPlan(List<CallSequenceCallNode> calls, HashSet<string> participants, string partNumber, CallSequenceCallNode? prevSibling)
        => new()
        {
            PartNumber = partNumber,
            Title = InferTitle(calls),
            ParticipantIds = new List<string>(participants),
            VirtualRootCalls = calls,
            PrevSibling = prevSibling,
        };

    // Alternates the sub-part numbering style by nesting depth so a second-level split reads
    // distinctly from the first: depth 1 (splitting a phase) appends a letter (1 -> 1A, 1B, 1C),
    // depth 2 (splitting an oversized sub-part like 1C) appends a digit (1C -> 1C1, 1C2), depth 3
    // appends a letter again, and so on for arbitrarily deep recursion.
    private static string ChildPartNumber(string parentPartNumber, int index, int depth)
        => depth % 2 == 1
            ? parentPartNumber + ToAlphaSuffix(index)
            : parentPartNumber + (index + 1).ToString();

    // Excel-style letters: 0→A … 25→Z, 26→AA, 27→AB, … — an oversized call can produce well
    // over 26 sub-parts now that the message cap is enforced inside a single call, and a plain
    // ('A' + index) would walk off the alphabet into "Part 2[", "Part 2\".
    private static string ToAlphaSuffix(int index)
    {
        var sb = new StringBuilder();
        for (int i = index + 1; i > 0; i /= 26)
        {
            i--;
            sb.Insert(0, (char)('A' + i % 26));
        }
        return sb.ToString();
    }

    private static void AddIfMissing(List<string> list, string id)
    {
        if (!list.Contains(id))
            list.Add(id);
    }

    // ── Title inference ───────────────────────────────────────────────────────

    private static string InferTitle(List<CallSequenceCallNode> calls)
    {
        if (calls.Count == 0) return "Continuation";

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var labels = new List<string>();
        foreach (var call in calls)
        {
            var lbl = call.MessageLabel;
            if (!string.IsNullOrWhiteSpace(lbl) && seen.Add(lbl))
            {
                labels.Add(lbl);
                if (labels.Count == 3) break;
            }
        }

        if (labels.Count == 0) return "Processing";
        if (labels.Count == 1) return labels[0];
        if (labels.Count == 2) return labels[0] + " and " + labels[1];
        return labels[0] + ", " + labels[1] + " and more";
    }
}
