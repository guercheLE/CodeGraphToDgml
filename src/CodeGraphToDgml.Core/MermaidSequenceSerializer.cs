using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace CodeGraphToDgml.Core;

public sealed class MermaidSequenceSerializer
{
    // ── Public API ────────────────────────────────────────────────────────────

    public string Serialize(CallSequence sequence, bool stackedActivationBars = true, bool autoNumber = false)
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
            foreach (var call in sequence.RootCalls)
                EmitCall(sb, call, stackedActivationBars);
        }

        while (sb.Length > 0 && (sb[sb.Length - 1] == '\r' || sb[sb.Length - 1] == '\n'))
            sb.Length--;

        return sb.ToString();
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
            foreach (var call in sequence.RootCalls)
                EmitCall(sb, call, stackedActivationBars);
            sb.Append("    ").Append(sequence.RootParticipantId).Append("-->>-").Append(callerId).AppendLine(": ");
        }
        else
        {
            sb.Append("    ").Append(callerId).Append("->>").Append(sequence.RootParticipantId).Append(": ").AppendLine(rootLabel);
            foreach (var call in sequence.RootCalls)
                EmitCall(sb, call, stackedActivationBars);
        }
    }

    public string BuildMarkdown(CallSequence sequence, bool stackedActivationBars = true, bool autoNumber = false, int maxParticipantsPerDiagram = 0, int maxMessagesPerDiagram = 0)
    {
        var sections = BuildSections(sequence, stackedActivationBars, autoNumber, maxParticipantsPerDiagram, maxMessagesPerDiagram);

        var sb = new StringBuilder();
        sb.Append("# Sequence: ").AppendLine(sequence.Title);
        sb.AppendLine();

        if (sections.Count == 1)
        {
            sb.AppendLine("```mermaid");
            sb.AppendLine(sections[0].Content);
            sb.AppendLine("```");
        }
        else
        {
            for (int i = 0; i < sections.Count; i++)
            {
                if (i > 0) sb.AppendLine();
                sb.Append("## ").AppendLine(sections[i].Heading);
                sb.AppendLine();
                sb.AppendLine("```mermaid");
                sb.AppendLine(sections[i].Content);
                sb.AppendLine("```");
            }
        }

        return sb.ToString();
    }

    public string BuildHtml(CallSequence sequence, bool stackedActivationBars = true, bool autoNumber = false, int maxParticipantsPerDiagram = 0, int maxMessagesPerDiagram = 0)
    {
        var sections = BuildSections(sequence, stackedActivationBars, autoNumber, maxParticipantsPerDiagram, maxMessagesPerDiagram);
        var htmlTitle = EscapeHtml(sequence.Title);

        if (sections.Count > 1)
            return BuildMultiHtml(htmlTitle, sections);

        // Single-diagram path – keeps existing tests passing
        var jsSource = ToJsString(sections[0].Content);

        var sb = new StringBuilder();
        sb.AppendLine("<!DOCTYPE html>");
        sb.AppendLine("<html lang=\"en\">");
        sb.AppendLine("<head>");
        sb.AppendLine("<meta charset=\"utf-8\">");
        sb.Append("<title>Sequence: ").Append(htmlTitle).AppendLine("</title>");
        sb.AppendLine("<style>");
        sb.AppendLine("* { box-sizing: border-box; margin: 0; padding: 0; }");
        sb.AppendLine("body { font-family: system-ui, -apple-system, sans-serif; background: #f0f2f5; }");
        sb.AppendLine(".toolbar {");
        sb.AppendLine("  position: sticky; top: 0; z-index: 10;");
        sb.AppendLine("  display: flex; align-items: center; gap: 8px; flex-wrap: wrap;");
        sb.AppendLine("  padding: 8px 16px; background: #fff; border-bottom: 1px solid #d0d0d0;");
        sb.AppendLine("  box-shadow: 0 1px 4px rgba(0,0,0,.08);");
        sb.AppendLine("}");
        sb.AppendLine(".toolbar h1 { font-size: 14px; font-weight: 600; flex: 1; min-width: 120px;");
        sb.AppendLine("  overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }");
        sb.AppendLine(".toolbar button { padding: 4px 12px; border: 1px solid #bbb; border-radius: 4px;");
        sb.AppendLine("  background: #fff; cursor: pointer; font-size: 13px; user-select: none; }");
        sb.AppendLine(".toolbar button:hover { background: #f0f0f0; }");
        sb.AppendLine("#zoom-pct { font-size: 13px; min-width: 44px; text-align: center; color: #555; }");
        sb.AppendLine(".hint { font-size: 11px; color: #999; }");
        sb.AppendLine(".scroll { overflow: auto; width: 100%; padding: 24px; min-height: calc(100vh - 50px); }");
        sb.AppendLine("#diagram-container { display: inline-block; }");
        sb.AppendLine("#diagram-container svg { display: block; border-radius: 6px;");
        sb.AppendLine("  box-shadow: 0 1px 8px rgba(0,0,0,.12); background: #fff; padding: 16px; }");
        sb.AppendLine("</style>");
        sb.AppendLine("</head>");
        sb.AppendLine("<body>");
        sb.AppendLine("<div class=\"toolbar\">");
        sb.Append("  <h1>Sequence: ").Append(htmlTitle).AppendLine("</h1>");
        sb.AppendLine("  <button onclick=\"zoom(-0.1)\" title=\"Zoom out\">&#8722;</button>");
        sb.AppendLine("  <span id=\"zoom-pct\">100%</span>");
        sb.AppendLine("  <button onclick=\"zoom(+0.1)\" title=\"Zoom in\">+</button>");
        sb.AppendLine("  <button onclick=\"resetZoom()\">Reset</button>");
        sb.AppendLine("  <button onclick=\"fitWidth()\">Fit width</button>");
        sb.AppendLine("  <span class=\"hint\">Ctrl+scroll to zoom</span>");
        sb.AppendLine("</div>");
        sb.AppendLine("<div class=\"scroll\" id=\"scroll\">");
        sb.AppendLine("  <div id=\"diagram-container\"></div>");
        sb.AppendLine("</div>");
        sb.AppendLine("<script type=\"module\">");
        sb.AppendLine("import mermaid from 'https://cdn.jsdelivr.net/npm/mermaid@11/dist/mermaid.esm.min.mjs';");
        sb.AppendLine("mermaid.initialize({ startOnLoad: false, theme: 'default', maxTextSize: 500000 });");
        sb.AppendLine();
        sb.Append("const DIAGRAM_SOURCE = ").Append(jsSource).AppendLine(";");
        sb.AppendLine();
        sb.AppendLine("let z = 1, svg = null, nw = 0, nh = 0;");
        sb.AppendLine();
        sb.AppendLine("async function render() {");
        sb.AppendLine("  const { svg: svgText } = await mermaid.render('seq', DIAGRAM_SOURCE);");
        sb.AppendLine("  const container = document.getElementById('diagram-container');");
        sb.AppendLine("  container.innerHTML = svgText;");
        sb.AppendLine("  svg = container.querySelector('svg');");
        sb.AppendLine("  const vb = svg.viewBox.baseVal;");
        sb.AppendLine("  nw = vb.width  > 0 ? vb.width  : (parseFloat(svg.getAttribute('width'))  || 800);");
        sb.AppendLine("  nh = vb.height > 0 ? vb.height : (parseFloat(svg.getAttribute('height')) || 600);");
        sb.AppendLine("  const avail = document.getElementById('scroll').clientWidth - 48;");
        sb.AppendLine("  if (nw > avail) z = Math.max(0.1, avail / nw);");
        sb.AppendLine("  applyZoom();");
        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine("function applyZoom() {");
        sb.AppendLine("  if (!svg) return;");
        sb.AppendLine("  svg.setAttribute('width',  nw * z);");
        sb.AppendLine("  svg.setAttribute('height', nh * z);");
        sb.AppendLine("  document.getElementById('zoom-pct').textContent = Math.round(z * 100) + '%';");
        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine("window.zoom      = d  => { z = Math.max(0.1, Math.min(8, z + d)); applyZoom(); };");
        sb.AppendLine("window.resetZoom = ()  => { z = 1; applyZoom(); };");
        sb.AppendLine("window.fitWidth  = ()  => {");
        sb.AppendLine("  const avail = document.getElementById('scroll').clientWidth - 48;");
        sb.AppendLine("  z = Math.max(0.1, avail / nw);");
        sb.AppendLine("  applyZoom();");
        sb.AppendLine("};");
        sb.AppendLine();
        sb.AppendLine("document.getElementById('scroll').addEventListener('wheel', e => {");
        sb.AppendLine("  if (!e.ctrlKey) return;");
        sb.AppendLine("  e.preventDefault();");
        sb.AppendLine("  zoom(e.deltaY < 0 ? 0.1 : -0.1);");
        sb.AppendLine("}, { passive: false });");
        sb.AppendLine();
        sb.AppendLine("render().catch(err => {");
        sb.AppendLine("  document.getElementById('diagram-container').innerHTML =");
        sb.AppendLine("    '<pre style=\"color:red;padding:16px\">' + err.message + '</pre>';");
        sb.AppendLine("});");
        sb.AppendLine("</script>");
        sb.AppendLine("</body>");
        sb.Append("</html>");
        return sb.ToString();
    }

    // ── Segment metadata ──────────────────────────────────────────────────────

    private sealed class SegmentPlan
    {
        public string PartNumber { get; set; } = "";
        public string Title { get; set; } = "";
        public int StartMessage { get; set; }
        public int EndMessage { get; set; }
        public List<string> ParticipantIds { get; set; } = new List<string>();
        public List<string> BoundaryFromPrev { get; set; } = new List<string>();
        public List<string> SplitBoundary { get; set; } = new List<string>();
        public string? PrevPart { get; set; }
        public string? NextPart { get; set; }
        public string? PrevTitle { get; set; }
        public string? NextTitle { get; set; }
        public List<CallSequenceCallNode> VirtualRootCalls { get; set; } = new List<CallSequenceCallNode>();

        // Item 2: whether this is the first/last segment of the whole (possibly multi-part)
        // diagram — used to decide where the synthetic «Caller» actor's opening/closing arrows
        // land vs. where it merely stays "in flight" via activate/deactivate.
        public bool IsFirstSegment { get; set; }
        public bool IsLastSegment { get; set; }
    }

    private sealed record DiagramSection(string Heading, string Content, SegmentPlan? Plan = null);

    // ── Section building ──────────────────────────────────────────────────────

    private List<DiagramSection> BuildSections(CallSequence sequence, bool stackedBars, bool autoNumber, int maxPerDiagram, int maxMessagesPerDiagram = 0)
    {
        if (maxPerDiagram <= 0 && maxMessagesPerDiagram <= 0)
            return [new DiagramSection("", Serialize(sequence, stackedBars, autoNumber))];

        if (sequence.RootCalls.Count == 0)
            return [new DiagramSection("", Serialize(sequence, stackedBars, autoNumber))];

        var maxParticipants = maxPerDiagram > 0 ? maxPerDiagram : int.MaxValue;
        var maxMessages = maxMessagesPerDiagram > 0 ? maxMessagesPerDiagram : int.MaxValue;

        bool participantsFit = sequence.Participants.Count <= maxParticipants;
        bool messagesFit = CountArrows(sequence.RootCalls, stackedBars) <= maxMessages;
        if (participantsFit && messagesFit)
            return [new DiagramSection("", Serialize(sequence, stackedBars, autoNumber))];

        var plans = ComputeSegmentPlans(sequence, maxParticipants, maxMessages, stackedBars);

        if (plans.Count <= 1)
            return [new DiagramSection("", Serialize(sequence, stackedBars, autoNumber))];

        for (int i = 0; i < plans.Count; i++)
        {
            if (i > 0)
            {
                plans[i].PrevPart = plans[i - 1].PartNumber;
                plans[i].PrevTitle = plans[i - 1].Title;
            }
            if (i < plans.Count - 1)
            {
                plans[i].NextPart = plans[i + 1].PartNumber;
                plans[i].NextTitle = plans[i + 1].Title;
            }
        }

        var sections = new List<DiagramSection>(plans.Count);
        foreach (var plan in plans)
        {
            var content = RenderSegment(sequence, plan, stackedBars, autoNumber);
            var heading = "Part " + plan.PartNumber + " — " + plan.Title;
            sections.Add(new DiagramSection(heading, content, plan));
        }

        return sections;
    }

    // ── Segment plan computation ──────────────────────────────────────────────

    // Heuristic knobs for the "business-flow boundary" signal in ShouldCutBefore. Deliberately
    // conservative and grounded in the call graph's actual structure (participant overlap)
    // rather than names/comments — expect to tune these against real large diagrams.
    private const double LowOverlapThreshold = 0.2;
    private const int MinPhaseParticipantsForSplit = 3;

    private static readonly HashSet<string> NoBoundary = new(StringComparer.Ordinal);

    private static List<SegmentPlan> ComputeSegmentPlans(CallSequence sequence, int maxParticipants, int maxMessages, bool stackedBars)
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
        int msgCounter = 1;

        foreach (var phase in phases)
        {
            var phaseParts = GetCallListParticipants(phase);
            phaseParts.UnionWith(rootBoundary);

            List<(List<CallSequenceCallNode> Calls, HashSet<string> Participants)> subSegs;
            if (phaseParts.Count <= maxParticipants)
            {
                subSegs = new List<(List<CallSequenceCallNode>, HashSet<string>)> { (phase, phaseParts) };
            }
            else
            {
                // The only way a phase can still exceed the cap here is a single root call whose
                // own subtree alone exceeds it (SegmentCalls already keeps every multi-call phase
                // under the cap) — split that call's nested calls instead, keeping its own
                // caller/callee as an always-present boundary across every sub-part.
                var parentCall = phase[0];
                var boundary = new HashSet<string>(StringComparer.Ordinal)
                {
                    parentCall.CallerParticipantId,
                    parentCall.CalleeParticipantId,
                };

                var subPhases = parentCall.NestedCalls.Count == 0
                    ? [new List<CallSequenceCallNode>(phase)]
                    : SegmentCalls(parentCall.NestedCalls, boundary, maxParticipants, maxMessages, stackedBars);

                if (subPhases.Count <= 1)
                {
                    subSegs = new List<(List<CallSequenceCallNode>, HashSet<string>)> { (phase, phaseParts) };
                }
                else
                {
                    subSegs = subPhases
                        .Select(calls =>
                        {
                            var parts = GetCallListParticipants(calls);
                            parts.UnionWith(boundary);
                            return (calls, parts);
                        })
                        .ToList();
                }
            }

            bool hasSubParts = subSegs.Count > 1;

            // When a single oversized call is split into sub-parts, its caller and callee
            // are still "in flight" across every sub-segment — record them so the renderer
            // can emit activate/deactivate to show the active execution context.
            List<string>? splitBoundary = null;
            if (hasSubParts && phase.Count == 1)
            {
                splitBoundary = new List<string>
                {
                    phase[0].CallerParticipantId,
                    phase[0].CalleeParticipantId,
                };
            }

            for (int subIdx = 0; subIdx < subSegs.Count; subIdx++)
            {
                var (calls, parts) = subSegs[subIdx];
                int arrowCount = CountArrows(calls, stackedBars);

                string partNumber = hasSubParts
                    ? phaseNumber.ToString() + (char)('A' + subIdx)
                    : phaseNumber.ToString();

                var plan = new SegmentPlan
                {
                    PartNumber = partNumber,
                    Title = InferTitle(calls),
                    StartMessage = msgCounter,
                    EndMessage = arrowCount > 0 ? msgCounter + arrowCount - 1 : msgCounter,
                    ParticipantIds = new List<string>(parts),
                    SplitBoundary = splitBoundary ?? new List<string>(),
                    VirtualRootCalls = calls,
                };

                allPlans.Add(plan);
                msgCounter += arrowCount > 0 ? arrowCount : 1;
            }

            phaseNumber++;
        }

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

        return allPlans;
    }

    // Shared segmentation primitive used both for grouping top-level RootCalls into phases
    // (boundaryParticipants empty) and for splitting a single oversized call's NestedCalls into
    // sub-parts (boundaryParticipants = that call's caller+callee, always present in every
    // resulting segment). A segment boundary is drawn when the next call would push the segment
    // over the participant or message cap, or when it looks like the start of an unrelated
    // business flow (see ShouldCutBefore).
    private static List<List<CallSequenceCallNode>> SegmentCalls(
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

            if (current.Count > 0 && ShouldCutBefore(
                    currentParticipants, currentMessages, callParticipants, callMessages,
                    boundaryParticipants, maxParticipants, maxMessages))
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

        return segments;
    }

    private static bool ShouldCutBefore(
        HashSet<string> segParticipants,
        int segMessages,
        HashSet<string> nextParticipants,
        int nextMessages,
        HashSet<string> boundaryParticipants,
        int maxParticipants,
        int maxMessages)
    {
        var combined = new HashSet<string>(segParticipants, StringComparer.Ordinal);
        combined.UnionWith(nextParticipants);
        if (combined.Count > maxParticipants)
            return true;

        if (segMessages + nextMessages > maxMessages)
            return true;

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

    private static HashSet<string> GetSubtreeParticipants(CallSequenceCallNode call)
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

    private static HashSet<string> GetCallListParticipants(List<CallSequenceCallNode> calls)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (var call in calls)
            set.UnionWith(GetSubtreeParticipants(call));
        return set;
    }

    // ── Arrow counting ────────────────────────────────────────────────────────

    private static int CountArrows(IReadOnlyList<CallSequenceCallNode> calls, bool stackedBars)
    {
        int count = 0;
        foreach (var call in calls)
            count += CountArrowsInSubtree(call, stackedBars);
        return count;
    }

    private static int CountArrowsInSubtree(CallSequenceCallNode call, bool stackedBars)
    {
        int count = 1; // call arrow
        foreach (var nested in call.NestedCalls)
            count += CountArrowsInSubtree(nested, stackedBars);
        if (stackedBars)
            count += 1; // return arrow
        return count;
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

    // ── Segment rendering ─────────────────────────────────────────────────────

    private string RenderSegment(CallSequence sequence, SegmentPlan plan, bool stackedBars, bool autoNumber)
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
        // the same way SplitBoundary already keeps a split parent's caller/callee active across
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

        if (stackedBars && plan.SplitBoundary.Count > 0)
        {
            foreach (var p in plan.SplitBoundary)
                sb.Append("    activate ").AppendLine(p);
        }

        if (hasCaller && stackedBars)
        {
            if (plan.IsFirstSegment)
                sb.Append("    ").Append(callerId).Append("->>+").Append(sequence.RootParticipantId).Append(": ").AppendLine(EscapeLabel(sequence.RootMethodLabel));
            else
                sb.Append("    activate ").AppendLine(callerId);
        }
        else if (hasCaller && plan.IsFirstSegment)
        {
            // Stacked bars off: no activation concept, just the plain opening arrow on segment 1.
            sb.Append("    ").Append(callerId).Append("->>").Append(sequence.RootParticipantId).Append(": ").AppendLine(EscapeLabel(sequence.RootMethodLabel));
        }

        foreach (var call in plan.VirtualRootCalls)
            EmitCall(sb, call, stackedBars);

        if (hasCaller && stackedBars)
        {
            if (plan.IsLastSegment)
                sb.Append("    ").Append(sequence.RootParticipantId).Append("-->>-").Append(callerId).AppendLine(": ");
            else
                sb.Append("    deactivate ").AppendLine(callerId);
        }

        if (stackedBars && plan.SplitBoundary.Count > 0)
        {
            for (int i = plan.SplitBoundary.Count - 1; i >= 0; i--)
                sb.Append("    deactivate ").AppendLine(plan.SplitBoundary[i]);
        }

        while (sb.Length > 0 && (sb[sb.Length - 1] == '\r' || sb[sb.Length - 1] == '\n'))
            sb.Length--;

        return sb.ToString();
    }

    // ── Call emission ─────────────────────────────────────────────────────────

    private static void EmitCall(StringBuilder sb, CallSequenceCallNode call, bool stackedActivationBars)
    {
        var from = call.CallerParticipantId;
        var to = call.CalleeParticipantId;
        var label = EscapeLabel(call.MessageLabel);

        if (stackedActivationBars)
        {
            sb.Append("    ").Append(from).Append("->>+").Append(to).Append(": ").AppendLine(label);
            foreach (var nested in call.NestedCalls)
                EmitCall(sb, nested, stackedActivationBars);
            sb.Append("    ").Append(to).Append("-->>-").Append(from).AppendLine(": ");
        }
        else
        {
            sb.Append("    ").Append(from).Append("->>").Append(to).Append(": ").AppendLine(label);
            foreach (var nested in call.NestedCalls)
                EmitCall(sb, nested, stackedActivationBars);
        }
    }

    // ── Multi-diagram HTML ────────────────────────────────────────────────────

    private static string BuildMultiHtml(string htmlTitle, List<DiagramSection> sections)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<!DOCTYPE html>");
        sb.AppendLine("<html lang=\"en\">");
        sb.AppendLine("<head>");
        sb.AppendLine("<meta charset=\"utf-8\">");
        sb.Append("<title>Sequence: ").Append(htmlTitle).AppendLine("</title>");
        sb.AppendLine("<style>");
        sb.AppendLine("* { box-sizing: border-box; margin: 0; padding: 0; }");
        sb.AppendLine("body { font-family: system-ui, -apple-system, sans-serif; background: #f0f2f5; }");
        sb.AppendLine(".page-header { position: sticky; top: 0; z-index: 10; padding: 8px 16px;");
        sb.AppendLine("  background: #fff; border-bottom: 1px solid #d0d0d0;");
        sb.AppendLine("  box-shadow: 0 1px 4px rgba(0,0,0,.08); }");
        sb.AppendLine(".page-header h1 { font-size: 14px; font-weight: 600; margin-bottom: 4px;");
        sb.AppendLine("  white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }");
        sb.AppendLine(".toc { display: flex; gap: 10px; flex-wrap: wrap; }");
        sb.AppendLine(".toc a { font-size: 12px; color: #0969da; text-decoration: none; }");
        sb.AppendLine(".toc a:hover { text-decoration: underline; }");
        sb.AppendLine(".diagram-section { margin: 20px 16px; }");
        sb.AppendLine(".section-bar { display: flex; align-items: center; gap: 8px; flex-wrap: wrap;");
        sb.AppendLine("  padding: 7px 12px; background: #fff; border: 1px solid #d0d0d0;");
        sb.AppendLine("  border-radius: 6px 6px 0 0; }");
        sb.AppendLine(".section-bar h2 { font-size: 13px; font-weight: 600; flex: 1; min-width: 0;");
        sb.AppendLine("  white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }");
        sb.AppendLine(".meta { font-size: 11px; color: #777; white-space: nowrap; }");
        sb.AppendLine(".section-bar button { padding: 3px 10px; border: 1px solid #bbb; border-radius: 4px;");
        sb.AppendLine("  background: #fff; cursor: pointer; font-size: 12px; user-select: none; }");
        sb.AppendLine(".section-bar button:hover { background: #f0f0f0; }");
        sb.AppendLine(".zoom-pct { font-size: 12px; min-width: 40px; text-align: center; color: #555; }");
        sb.AppendLine(".hint { font-size: 11px; color: #999; }");
        sb.AppendLine(".continuation { font-size: 12px; color: #555; padding: 8px 14px;");
        sb.AppendLine("  background: #f8f9fa; border-left: 3px solid #0969da; margin: 0;");
        sb.AppendLine("  border-right: 1px solid #d0d0d0; }");
        sb.AppendLine(".continuation-top { border-top: none; }");
        sb.AppendLine(".continuation-bottom { border-bottom: 1px solid #d0d0d0;");
        sb.AppendLine("  border-radius: 0 0 6px 6px; }");
        sb.AppendLine(".continuation b { color: #333; }");
        sb.AppendLine(".diagram-scroll { overflow: auto; background: #fff; border: 1px solid #d0d0d0;");
        sb.AppendLine("  border-top: none; padding: 24px; min-height: 80px; }");
        sb.AppendLine(".diagram-section:not(:has(.continuation-bottom)) .diagram-scroll { border-radius: 0 0 6px 6px; }");
        sb.AppendLine(".diagram-wrap { display: inline-block; }");
        sb.AppendLine(".diagram-wrap svg { display: block; border-radius: 6px;");
        sb.AppendLine("  box-shadow: 0 1px 8px rgba(0,0,0,.12); background: #fff; padding: 16px; }");
        sb.AppendLine("</style>");
        sb.AppendLine("</head>");
        sb.AppendLine("<body>");

        // Sticky header with TOC
        sb.AppendLine("<div class=\"page-header\">");
        sb.Append("  <h1>Sequence: ").Append(htmlTitle).AppendLine("</h1>");
        sb.AppendLine("  <nav class=\"toc\">");
        for (int i = 0; i < sections.Count; i++)
        {
            var plan = sections[i].Plan;
            var partNum = plan != null ? plan.PartNumber : (i + 1).ToString();
            var tocLabel = "Part " + partNum;
            sb.Append("    <a href=\"#part-").Append(EscapeHtml(partNum)).Append("\">").Append(EscapeHtml(tocLabel)).AppendLine("</a>");
        }
        sb.AppendLine("  </nav>");
        sb.AppendLine("</div>");

        // One section per diagram
        for (int i = 0; i < sections.Count; i++)
        {
            var plan = sections[i].Plan;
            var partNum = plan != null ? plan.PartNumber : (i + 1).ToString();
            var heading = EscapeHtml(sections[i].Heading);
            string metaText = "";
            if (plan != null)
            {
                metaText = "Messages " + plan.StartMessage + "–" + plan.EndMessage
                           + " · " + plan.ParticipantIds.Count + " participants";
            }

            sb.Append("<section class=\"diagram-section\" id=\"part-").Append(EscapeHtml(partNum)).AppendLine("\">");

            // Section header bar
            sb.AppendLine("  <header class=\"section-bar\">");
            sb.Append("    <h2>").Append(heading).AppendLine("</h2>");
            if (!string.IsNullOrEmpty(metaText))
                sb.Append("    <div class=\"meta\">").Append(EscapeHtml(metaText)).AppendLine("</div>");
            sb.Append("    <button onclick=\"diagramZoom(").Append(i).AppendLine(",-0.1)\" title=\"Zoom out\">&#8722;</button>");
            sb.Append("    <span class=\"zoom-pct\" id=\"pct-").Append(i).AppendLine("\">100%</span>");
            sb.Append("    <button onclick=\"diagramZoom(").Append(i).AppendLine(",0.1)\" title=\"Zoom in\">+</button>");
            sb.Append("    <button onclick=\"diagramReset(").Append(i).AppendLine(")\">Reset</button>");
            sb.Append("    <button onclick=\"diagramFit(").Append(i).AppendLine(")\">Fit width</button>");
            sb.AppendLine("    <span class=\"hint\">Ctrl+scroll to zoom</span>");
            sb.AppendLine("  </header>");

            // Top continuation banner (not for first section)
            if (plan != null && plan.PrevPart != null)
            {
                sb.AppendLine("  <div class=\"continuation continuation-top\">");
                sb.Append("    Continues from <b>Part ").Append(EscapeHtml(plan.PrevPart)).Append("</b>");
                if (!string.IsNullOrEmpty(plan.PrevTitle))
                    sb.Append(" — ").Append(EscapeHtml(plan.PrevTitle ?? ""));
                sb.Append(". Previous messages: ").Append(sections[i - 1].Plan!.StartMessage).Append("–").Append(sections[i - 1].Plan!.EndMessage).AppendLine(".");
                if (plan.BoundaryFromPrev.Count > 0)
                {
                    sb.Append("    Open lifelines from previous part: <b>").Append(EscapeHtml(string.Join(", ", plan.BoundaryFromPrev))).AppendLine("</b>.");
                }
                sb.AppendLine("  </div>");
            }
            else if (i == 0)
            {
                sb.AppendLine("  <div class=\"continuation continuation-top\">");
                sb.AppendLine("    Start of sequence.");
                sb.AppendLine("  </div>");
            }

            // Diagram area
            sb.Append("  <div class=\"diagram-scroll\" id=\"scroll-").Append(i).AppendLine("\">");
            sb.AppendLine("    <div class=\"diagram-wrap\"></div>");
            sb.AppendLine("  </div>");

            // Bottom continuation banner (not for last section)
            if (plan != null && plan.NextPart != null)
            {
                var nextPlan = sections[i + 1].Plan;
                var sharedWithNext = nextPlan != null
                    ? plan.ParticipantIds.Where(p => nextPlan.ParticipantIds.Contains(p)).ToList()
                    : new List<string>();

                sb.AppendLine("  <div class=\"continuation continuation-bottom\">");
                sb.Append("    Continues in <b>Part ").Append(EscapeHtml(plan.NextPart)).Append("</b>");
                if (!string.IsNullOrEmpty(plan.NextTitle))
                    sb.Append(" — ").Append(EscapeHtml(plan.NextTitle ?? ""));
                sb.Append(". Next messages: ").Append(sections[i + 1].Plan!.StartMessage).Append("–").Append(sections[i + 1].Plan!.EndMessage).AppendLine(".");
                if (sharedWithNext.Count > 0)
                {
                    sb.Append("    Open lifelines continuing: <b>").Append(EscapeHtml(string.Join(", ", sharedWithNext))).AppendLine("</b>.");
                }
                sb.AppendLine("  </div>");
            }
            else if (i == sections.Count - 1)
            {
                sb.AppendLine("  <div class=\"continuation continuation-bottom\">");
                sb.AppendLine("    End of sequence.");
                sb.AppendLine("  </div>");
            }

            sb.AppendLine("</section>");
        }

        // JavaScript
        sb.AppendLine("<script type=\"module\">");
        sb.AppendLine("import mermaid from 'https://cdn.jsdelivr.net/npm/mermaid@11/dist/mermaid.esm.min.mjs';");
        sb.AppendLine("mermaid.initialize({ startOnLoad: false, theme: 'default', maxTextSize: 500000 });");
        sb.AppendLine();
        sb.AppendLine("const DIAGRAMS = [");
        for (int i = 0; i < sections.Count; i++)
        {
            sb.Append("  ").Append(ToJsString(sections[i].Content));
            if (i < sections.Count - 1) sb.Append(",");
            sb.AppendLine();
        }
        sb.AppendLine("];");
        sb.AppendLine();
        sb.AppendLine("const states = DIAGRAMS.map(() => ({ z: 1, nw: 0, nh: 0, svg: null }));");
        sb.AppendLine();
        sb.AppendLine("async function init() {");
        sb.AppendLine("  for (let i = 0; i < DIAGRAMS.length; i++) {");
        sb.AppendLine("    const scroll = document.getElementById('scroll-' + i);");
        sb.AppendLine("    const wrap = scroll.querySelector('.diagram-wrap');");
        sb.AppendLine("    let svgText;");
        sb.AppendLine("    try {");
        sb.AppendLine("      const result = await mermaid.render('seq-' + i, DIAGRAMS[i]);");
        sb.AppendLine("      svgText = result.svg;");
        sb.AppendLine("    } catch (err) {");
        sb.AppendLine("      wrap.innerHTML = '<pre style=\"color:red;padding:16px\">' + err.message + '</pre>';");
        sb.AppendLine("      continue;");
        sb.AppendLine("    }");
        sb.AppendLine("    wrap.innerHTML = svgText;");
        sb.AppendLine("    const s = states[i];");
        sb.AppendLine("    s.svg = wrap.querySelector('svg');");
        sb.AppendLine("    if (s.svg) {");
        sb.AppendLine("      const vb = s.svg.viewBox.baseVal;");
        sb.AppendLine("      s.nw = vb.width  > 0 ? vb.width  : (parseFloat(s.svg.getAttribute('width'))  || 800);");
        sb.AppendLine("      s.nh = vb.height > 0 ? vb.height : (parseFloat(s.svg.getAttribute('height')) || 600);");
        sb.AppendLine("      const avail = scroll.clientWidth - 48;");
        sb.AppendLine("      if (s.nw > avail) s.z = Math.max(0.1, avail / s.nw);");
        sb.AppendLine("      applyZoom(i);");
        sb.AppendLine("    }");
        sb.AppendLine("  }");
        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine("function applyZoom(i) {");
        sb.AppendLine("  const s = states[i];");
        sb.AppendLine("  if (!s.svg) return;");
        sb.AppendLine("  s.svg.setAttribute('width',  s.nw * s.z);");
        sb.AppendLine("  s.svg.setAttribute('height', s.nh * s.z);");
        sb.AppendLine("  document.getElementById('pct-' + i).textContent = Math.round(s.z * 100) + '%';");
        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine("window.diagramZoom  = (i, d) => { const s = states[i]; s.z = Math.max(0.1, Math.min(8, s.z + d)); applyZoom(i); };");
        sb.AppendLine("window.diagramReset = i      => { states[i].z = 1; applyZoom(i); };");
        sb.AppendLine("window.diagramFit   = i      => {");
        sb.AppendLine("  const w = document.getElementById('scroll-' + i).clientWidth - 48;");
        sb.AppendLine("  states[i].z = Math.max(0.1, w / states[i].nw);");
        sb.AppendLine("  applyZoom(i);");
        sb.AppendLine("};");
        sb.AppendLine();
        sb.AppendLine("for (let i = 0; i < DIAGRAMS.length; i++) {");
        sb.AppendLine("  document.getElementById('scroll-' + i).addEventListener('wheel', e => {");
        sb.AppendLine("    if (!e.ctrlKey) return;");
        sb.AppendLine("    e.preventDefault();");
        sb.AppendLine("    diagramZoom(i, e.deltaY < 0 ? 0.1 : -0.1);");
        sb.AppendLine("  }, { passive: false });");
        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine("init();");
        sb.AppendLine("</script>");
        sb.AppendLine("</body>");
        sb.Append("</html>");
        return sb.ToString();
    }

    // ── Encoding helpers ──────────────────────────────────────────────────────

    private static string EscapeHtml(string text)
        => text
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;")
            .Replace("\"", "&quot;");

    private static string EscapeLabel(string label)
        => label
            .Replace(":", "#colon;")
            .Replace("<", "#lt;")
            .Replace(">", "#gt;")
            .Replace("&", "#amp;");

    // Produces a JS string literal (double-quoted, all special chars escaped).
    // Encodes '<' and '>' as unicode escapes to prevent the HTML parser finding </script>.
    private static string ToJsString(string value)
    {
        var sb = new StringBuilder("\"");
        foreach (var c in value)
        {
            switch (c)
            {
                case '"':  sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n");  break;
                case '\r': sb.Append("\\r");  break;
                case '\t': sb.Append("\\t");  break;
                case '<':  sb.Append("\\u003c"); break;
                case '>':  sb.Append("\\u003e"); break;
                default:
                    if (c < 0x20)
                        sb.AppendFormat("\\u{0:x4}", (int)c);
                    else
                        sb.Append(c);
                    break;
            }
        }
        sb.Append('"');
        return sb.ToString();
    }
}
