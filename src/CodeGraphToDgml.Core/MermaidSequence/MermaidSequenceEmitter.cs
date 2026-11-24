using System.Collections.Generic;
using System.Linq;
using System.Text;

using static CodeGraphToDgml.Core.FragmentPaths;

namespace CodeGraphToDgml.Core;

// Text emitter: writes Mermaid sequenceDiagram source, either for a whole CallSequence or for
// one SegmentPlan of a split one. Owns the «Caller» wrap, activation bars, control-flow
// fragments, and label escaping.
internal static class MermaidSequenceEmitter
{
    internal static string EmitDiagram(CallSequence sequence, bool stackedActivationBars, bool autoNumber)
    {
        var sb = new StringBuilder();
        sb.AppendLine("sequenceDiagram");
        if (autoNumber)
            sb.AppendLine("    autonumber");

        // Item 2: give the root/entry method its own activation bar by wrapping RootCalls with a
        // synthetic external actor. Only when RootParticipantId is populated (i.e. the sequence
        // came from the real traversal path) — an unset RootParticipantId means "no root concept
        // declared," so hand-built/legacy CallSequences render exactly as before.
        bool hasCaller = !string.IsNullOrEmpty(sequence.RootParticipantId) && sequence.RootCalls.Count > 0;
        var callerId = hasCaller ? GetCallerActorId(sequence.Participants) : string.Empty;

        if (hasCaller)
            sb.Append("    actor ").Append(callerId).Append(" as ").AppendLine(CallerActorLabel);

        foreach (var p in sequence.Participants)
        {
            sb.Append("    participant ");
            sb.Append(p.Id);
            sb.Append(" as ");
            sb.AppendLine(EscapeLabel(p.Label));
        }

        if (sequence.Participants.Count > 0 && sequence.RootCalls.Count > 0)
            sb.AppendLine();

        if (hasCaller)
        {
            EmitRootCallerWrap(sb, callerId, sequence, stackedActivationBars);
        }
        else
        {
            EmitSiblings(sb, sequence.RootCalls, null, stackedActivationBars);
        }

        return TrimTrailingNewlines(sb);
    }

    // ── Root «Caller» actor ───────────────────────────────────────────────────

    private const string CallerActorLabel = "«Caller»"; // «Caller»

    private static string GetCallerActorId(IReadOnlyList<CallSequenceParticipant> participants)
    {
        var existingIds = new HashSet<string>(participants.Select(p => p.Id), StringComparer.Ordinal);
        if (!existingIds.Contains("Caller"))
            return "Caller";

        int suffix = 1;
        string candidate;
        do
        {
            candidate = "Caller_" + suffix;
            suffix++;
        } while (existingIds.Contains(candidate));

        return candidate;
    }

    private static void EmitRootCallerWrap(StringBuilder sb, string callerId, CallSequence sequence, bool stackedActivationBars)
    {
        var rootLabel = EscapeLabel(sequence.RootMethodLabel);

        if (stackedActivationBars)
        {
            sb.Append("    ").Append(callerId).Append("->>+").Append(sequence.RootParticipantId).Append(": ").AppendLine(rootLabel);
            EmitSiblings(sb, sequence.RootCalls, null, stackedActivationBars);
            sb.Append("    ").Append(sequence.RootParticipantId).Append("-->>-").Append(callerId).Append(": ").AppendLine(EscapeLabel(BuildReturnLabel(sequence.RootMethodLabel, sequence.RootReturnTypeLabel)));
        }
        else
        {
            sb.Append("    ").Append(callerId).Append("->>").Append(sequence.RootParticipantId).Append(": ").AppendLine(rootLabel);
            EmitSiblings(sb, sequence.RootCalls, null, stackedActivationBars);
        }
    }

    // ── Segment rendering ─────────────────────────────────────────────────────

    internal static string EmitSegment(CallSequence sequence, SegmentPlan plan, bool stackedBars, bool autoNumber)
    {
        var sb = new StringBuilder();
        sb.AppendLine("sequenceDiagram");

        if (autoNumber)
        {
            if (plan.StartMessage > 1)
                sb.Append("    autonumber ").AppendLine(plan.StartMessage.ToString());
            else
                sb.AppendLine("    autonumber");
        }

        // Item 2: the «Caller» actor spans every segment of a split diagram, staying "in flight"
        // the same way a SplitFrame keeps a split parent's caller/callee active across
        // sub-parts — only the first segment shows the opening call into the root method, and
        // only the last shows the return out of it.
        bool hasCaller = !string.IsNullOrEmpty(sequence.RootParticipantId);
        var callerId = hasCaller ? GetCallerActorId(sequence.Participants) : string.Empty;

        if (hasCaller)
            sb.Append("    actor ").Append(callerId).Append(" as ").AppendLine(CallerActorLabel);

        var segSet = new HashSet<string>(plan.ParticipantIds, StringComparer.Ordinal);
        foreach (var p in sequence.Participants)
        {
            if (segSet.Contains(p.Id))
                sb.Append("    participant ").Append(p.Id).Append(" as ").AppendLine(EscapeLabel(p.Label));
        }

        if (segSet.Count > 0 && plan.VirtualRootCalls.Count > 0)
            sb.AppendLine();

        // Every segment must keep Mermaid's activation stack balanced on its own: an arrow's '+'
        // activates the RECEIVER and a return's '-' deactivates the SENDER, so a bar that spans
        // segments is opened by the real arrow '+' in its first segment (or a bare activate in
        // later ones) and closed by a bare deactivate (non-last segments) or the return arrow's
        // '-' (its last segment). Bars span this way for the root method's (opened by the
        // «Caller» arrow, spanning ALL segments) and, for sub-parts of an oversized call, each
        // split-parent call's callee (opened by that parent-call arrow, spanning its sub-parts) —
        // a leaf can be nested inside several such spans when a sub-part was itself split further
        // (e.g. 1C1/1C2 inside 1C inside 1). Frames are rendered outermost-first on open and
        // innermost-first on close, mirroring how nested real calls stack in EmitSiblings. The
        // Caller's own bar exists only on non-first segments, opened and closed with bare
        // activate/deactivate.
        bool hasSplitParent = plan.SplitParents.Count > 0;
        // Only the outermost split frame's caller can coincide with the root method (every inner
        // frame's caller is the immediately-outer frame's callee, whose bar that outer frame
        // already manages) — the root's bar is itself covered by the «Caller» wrap, so only give
        // the outermost frame its own bar when the wrap doesn't already cover it.
        bool splitCallerNeedsOwnBar = hasSplitParent
            && !(hasCaller && plan.SplitParents[0].CallerParticipantId == sequence.RootParticipantId);

        if (hasCaller && stackedBars)
        {
            if (plan.IsFirstSegment)
            {
                sb.Append("    ").Append(callerId).Append("->>+").Append(sequence.RootParticipantId).Append(": ").AppendLine(EscapeLabel(sequence.RootMethodLabel));
            }
            else
            {
                sb.Append("    activate ").AppendLine(callerId);
                sb.Append("    activate ").AppendLine(sequence.RootParticipantId);
            }
        }
        else if (hasCaller && plan.IsFirstSegment)
        {
            // Stacked bars off: no activation concept, just the plain opening arrow on segment 1.
            sb.Append("    ").Append(callerId).Append("->>").Append(sequence.RootParticipantId).Append(": ").AppendLine(EscapeLabel(sequence.RootMethodLabel));
        }

        if (hasSplitParent)
        {
            if (stackedBars && splitCallerNeedsOwnBar)
                sb.Append("    activate ").AppendLine(plan.SplitParents[0].CallerParticipantId);

            foreach (var frame in plan.SplitParents)
            {
                // The split call's fragments wrap its arrow, body, and return in every sub-part.
                for (int f = 0; f < frame.FragmentPath.Count; f++)
                    EmitFragmentOpen(sb, frame.FragmentPath[f], continued: !frame.IsFirstSubPart || f < frame.ContinuedFragmentDepth);

                if (stackedBars)
                {
                    if (frame.IsFirstSubPart)
                        sb.Append("    ").Append(frame.CallerParticipantId).Append("->>+").Append(frame.CalleeParticipantId).Append(": ").AppendLine(EscapeLabel(frame.MessageLabel));
                    else
                        sb.Append("    activate ").AppendLine(frame.CalleeParticipantId);
                }
                else if (frame.IsFirstSubPart)
                {
                    sb.Append("    ").Append(frame.CallerParticipantId).Append("->>").Append(frame.CalleeParticipantId).Append(": ").AppendLine(EscapeLabel(frame.MessageLabel));
                }
            }
        }

        EmitSiblings(sb, plan.VirtualRootCalls, plan.PrevSibling, stackedBars);

        if (hasSplitParent)
        {
            for (int i = plan.SplitParents.Count - 1; i >= 0; i--)
            {
                var frame = plan.SplitParents[i];
                if (stackedBars)
                {
                    if (frame.IsLastSubPart)
                        sb.Append("    ").Append(frame.CalleeParticipantId).Append("-->>-").Append(frame.CallerParticipantId).Append(": ").AppendLine(EscapeLabel(BuildReturnLabel(frame.MessageLabel, frame.ReturnTypeLabel)));
                    else
                        sb.Append("    deactivate ").AppendLine(frame.CalleeParticipantId);
                }

                for (int f = 0; f < frame.FragmentPath.Count; f++)
                    sb.AppendLine("    end");
            }

            if (stackedBars && splitCallerNeedsOwnBar)
                sb.Append("    deactivate ").AppendLine(plan.SplitParents[0].CallerParticipantId);
        }

        if (hasCaller && stackedBars)
        {
            if (plan.IsLastSegment)
                sb.Append("    ").Append(sequence.RootParticipantId).Append("-->>-").Append(callerId).Append(": ").AppendLine(EscapeLabel(BuildReturnLabel(sequence.RootMethodLabel, sequence.RootReturnTypeLabel)));
            else
                sb.Append("    deactivate ").AppendLine(sequence.RootParticipantId);

            if (!plan.IsFirstSegment)
                sb.Append("    deactivate ").AppendLine(callerId);
        }

        return TrimTrailingNewlines(sb);
    }

    // ── Control-flow fragments ────────────────────────────────────────────────

    // Emits a sibling list, each call with its whole nested subtree, opening and closing the
    // Mermaid fragments (alt/opt/loop/...) that the siblings' FragmentPaths describe. Recursion
    // into a call's nested calls goes straight back into this method, so there is no
    // sibling-list/call cycle between two methods. Adjacent siblings sharing a fragment instance stay inside
    // one fragment; a different section of the same instance emits the section separator; any
    // other change closes the fragments no longer shared and opens the new ones. Every fragment
    // opened here is closed here, so each sibling list is self-balanced. When `prevSibling` is
    // given (a split part whose first call continues a fragment cut in the previous part), the
    // shared fragments are re-opened first with a "(cont.)" marker.
    private static void EmitSiblings(StringBuilder sb, IReadOnlyList<CallSequenceCallNode> calls, CallSequenceCallNode? prevSibling, bool stackedActivationBars)
    {
        if (calls.Count == 0)
            return;

        var open = new List<CallSequenceFragmentScope>();

        if (prevSibling is not null)
        {
            var first = calls[0].FragmentPath;
            int shared = CommonInstancePrefixLength(prevSibling.FragmentPath, first);
            for (int i = 0; i < shared; i++)
            {
                EmitFragmentOpen(sb, first[i], continued: true);
                open.Add(first[i]);
            }
        }

        foreach (var call in calls)
        {
            var path = call.FragmentPath;

            int k = 0;
            while (k < open.Count && k < path.Count
                && open[k].InstanceId == path[k].InstanceId
                && open[k].SectionIndex == path[k].SectionIndex)
            {
                k++;
            }

            if (k < open.Count && k < path.Count && open[k].InstanceId == path[k].InstanceId)
            {
                // Same fragment instance, next section: close everything nested deeper, then
                // switch sections in place.
                for (int i = open.Count - 1; i > k; i--)
                {
                    sb.AppendLine("    end");
                    open.RemoveAt(i);
                }

                EmitSectionSeparator(sb, path[k]);
                open[k] = path[k];
                k++;
            }
            else
            {
                for (int i = open.Count - 1; i >= k; i--)
                {
                    sb.AppendLine("    end");
                    open.RemoveAt(i);
                }
            }

            for (int i = k; i < path.Count; i++)
            {
                EmitFragmentOpen(sb, path[i], continued: false);
                open.Add(path[i]);
            }

            // The call itself: its arrow, its nested calls (a sibling list of their own, with
            // their own fragments), and its return.
            var from = call.CallerParticipantId;
            var to = call.CalleeParticipantId;
            var label = EscapeLabel(call.MessageLabel);

            if (stackedActivationBars)
            {
                sb.Append("    ").Append(from).Append("->>+").Append(to).Append(": ").AppendLine(label);
                EmitSiblings(sb, call.NestedCalls, null, stackedActivationBars);
                sb.Append("    ").Append(to).Append("-->>-").Append(from).Append(": ").AppendLine(EscapeLabel(BuildReturnLabel(call.MessageLabel, call.ReturnTypeLabel)));
            }
            else
            {
                sb.Append("    ").Append(from).Append("->>").Append(to).Append(": ").AppendLine(label);
                EmitSiblings(sb, call.NestedCalls, null, stackedActivationBars);
            }
        }

        for (int i = open.Count - 1; i >= 0; i--)
            sb.AppendLine("    end");
    }

    private static void EmitFragmentOpen(StringBuilder sb, CallSequenceFragmentScope scope, bool continued)
    {
        sb.Append("    ").Append(FragmentKeyword(scope.Kind));
        AppendFragmentLabel(sb, scope.Label, continued);
        sb.AppendLine();
    }

    private static void EmitSectionSeparator(StringBuilder sb, CallSequenceFragmentScope scope)
    {
        switch (scope.Kind)
        {
            case SequenceFragmentKind.Alt:
                sb.Append("    else");
                break;
            case SequenceFragmentKind.Par:
                sb.Append("    and");
                break;
            case SequenceFragmentKind.Critical:
                sb.Append("    option");
                break;
            default:
                // Single-section kinds have no separator: close and re-open.
                sb.AppendLine("    end");
                sb.Append("    ").Append(FragmentKeyword(scope.Kind));
                break;
        }

        AppendFragmentLabel(sb, scope.Label, continued: false);
        sb.AppendLine();
    }

    private static void AppendFragmentLabel(StringBuilder sb, string label, bool continued)
    {
        var text = EscapeFragmentLabel(label);
        if (continued)
            text = text.Length == 0 ? "(cont.)" : text + " (cont.)";
        if (text.Length > 0)
            sb.Append(' ').Append(text);
    }

    private static string FragmentKeyword(SequenceFragmentKind kind)
        => kind switch
        {
            SequenceFragmentKind.Alt => "alt",
            SequenceFragmentKind.Opt => "opt",
            SequenceFragmentKind.Loop => "loop",
            SequenceFragmentKind.Break => "break",
            SequenceFragmentKind.Par => "par",
            SequenceFragmentKind.Critical => "critical",
            _ => "opt",
        };

    // Fragment labels sit on a keyword line, where ';' would end the statement and '%' could
    // start a comment, so they get a stricter escape than message labels.
    private static string EscapeFragmentLabel(string label)
    {
        var sb = new StringBuilder(label.Length + 8);
        foreach (var c in label)
        {
            switch (c)
            {
                case ':': sb.Append("#colon;"); break;
                case '<': sb.Append("#lt;"); break;
                case '>': sb.Append("#gt;"); break;
                case '&': sb.Append("#amp;"); break;
                case ';': sb.Append("#semi;"); break;
                case '%': sb.Append("#37;"); break;
                case '#': sb.Append("#35;"); break;
                case '\r': break;
                case '\n': sb.Append(' '); break;
                default: sb.Append(c); break;
            }
        }
        return sb.ToString();
    }

    // Labels a return arrow with which call is returning and, when known, its declared return
    // type — e.g. "BigEntry returns int" — so the return stays identifiable even when it lands
    // far from its opening arrow (a different split part, or just many lines further down).
    private static string BuildReturnLabel(string callLabel, string returnTypeLabel)
        => string.IsNullOrEmpty(returnTypeLabel) ? callLabel : callLabel + " returns " + returnTypeLabel;

    private static string EscapeLabel(string label)
        => label
            .Replace(":", "#colon;")
            .Replace("<", "#lt;")
            .Replace(">", "#gt;")
            .Replace("&", "#amp;");

    private static string TrimTrailingNewlines(StringBuilder sb)
    {
        while (sb.Length > 0 && (sb[sb.Length - 1] == '\r' || sb[sb.Length - 1] == '\n'))
            sb.Length--;

        return sb.ToString();
    }
}
