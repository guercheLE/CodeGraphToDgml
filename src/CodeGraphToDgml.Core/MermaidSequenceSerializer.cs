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
            sb.Append("    ").Append(sequence.RootParticipantId).Append("-->>-").Append(callerId).Append(": ").AppendLine(EscapeLabel(BuildReturnLabel(sequence.RootMethodLabel, sequence.RootReturnTypeLabel)));
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
        sb.AppendLine("  document.getElementById('dseq')?.remove(); // mermaid leaves its error SVG in <body>");
        sb.AppendLine("});");
        sb.AppendLine("</script>");
        sb.AppendLine("</body>");
        sb.Append("</html>");
        return sb.ToString();
    }

    // ── Segment metadata ──────────────────────────────────────────────────────

    // When a segment is a sub-part of an oversized call, that parent call's execution spans
    // every sub-part. Rendered exactly like the «Caller» wrap: the sub-part that opens it shows
    // the real opening arrow (caller->>+callee: label), sub-parts in between keep the bar alive
    // with bare activate/deactivate, and the sub-part that closes it shows the return. A leaf
    // segment can be nested inside several such spans at once — e.g. a big call split into
    // 1A/1B/1C, where 1C is itself a single oversized call split further into 1C1/1C2 — so each
    // leaf carries an ordered stack of frames, outermost first.
    private sealed class SplitFrame
    {
        public string CallerParticipantId { get; set; } = "";
        public string CalleeParticipantId { get; set; } = "";
        public string MessageLabel { get; set; } = "";
        public string ReturnTypeLabel { get; set; } = "";
        public bool IsFirstSubPart { get; set; }
        public bool IsLastSubPart { get; set; }
    }

    private sealed class SegmentPlan
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

        foreach (var phase in phases)
        {
            var phaseParts = GetCallListParticipants(phase);
            phaseParts.UnionWith(rootBoundary);

            allPlans.AddRange(ExpandPhase(phase, phaseParts, phaseNumber.ToString(), depth: 1,
                maxParticipants, maxMessages, stackedBars));

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
        bool stackedBars)
    {
        bool fits = participants.Count <= maxParticipants
            && CountArrows(calls, stackedBars) <= maxMessages;
        if (fits || calls.Count != 1)
            return [MakeLeafPlan(calls, participants, partNumber)];

        var parentCall = calls[0];
        if (parentCall.NestedCalls.Count == 0)
            return [MakeLeafPlan(calls, participants, partNumber)];

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
            leaves.AddRange(ExpandPhase(subCalls, subParts, childPartNumber, childDepth,
                maxParticipants, maxMessages, stackedBars));
        }

        // Nothing actually split (however deep we descended, no divisible level was found) —
        // emit the original call as one leaf rather than wrapping it in a pointless frame.
        if (leaves.Count == 1)
            return [MakeLeafPlan(calls, participants, partNumber)];

        // The parent call is "in flight" across every leaf produced above — the first leaf opens
        // it with the real arrow, the last leaf closes it, everything in between just keeps its
        // bars alive.
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
            });
        }

        return leaves;
    }

    private static SegmentPlan MakeLeafPlan(List<CallSequenceCallNode> calls, HashSet<string> participants, string partNumber)
        => new()
        {
            PartNumber = partNumber,
            Title = InferTitle(calls),
            ParticipantIds = new List<string>(participants),
            VirtualRootCalls = calls,
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

        // Every segment must keep Mermaid's activation stack balanced on its own: an arrow's '+'
        // activates the RECEIVER and a return's '-' deactivates the SENDER, so a bar that spans
        // segments is opened by the real arrow '+' in its first segment (or a bare activate in
        // later ones) and closed by a bare deactivate (non-last segments) or the return arrow's
        // '-' (its last segment). Bars span this way for the root method's (opened by the
        // «Caller» arrow, spanning ALL segments) and, for sub-parts of an oversized call, each
        // split-parent call's callee (opened by that parent-call arrow, spanning its sub-parts) —
        // a leaf can be nested inside several such spans when a sub-part was itself split further
        // (e.g. 1C1/1C2 inside 1C inside 1). Frames are rendered outermost-first on open and
        // innermost-first on close, mirroring how nested real calls stack via EmitCall. The
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
            if (stackedBars)
            {
                if (splitCallerNeedsOwnBar)
                    sb.Append("    activate ").AppendLine(plan.SplitParents[0].CallerParticipantId);

                foreach (var frame in plan.SplitParents)
                {
                    if (frame.IsFirstSubPart)
                        sb.Append("    ").Append(frame.CallerParticipantId).Append("->>+").Append(frame.CalleeParticipantId).Append(": ").AppendLine(EscapeLabel(frame.MessageLabel));
                    else
                        sb.Append("    activate ").AppendLine(frame.CalleeParticipantId);
                }
            }
            else
            {
                foreach (var frame in plan.SplitParents)
                {
                    if (frame.IsFirstSubPart)
                        sb.Append("    ").Append(frame.CallerParticipantId).Append("->>").Append(frame.CalleeParticipantId).Append(": ").AppendLine(EscapeLabel(frame.MessageLabel));
                }
            }
        }

        foreach (var call in plan.VirtualRootCalls)
            EmitCall(sb, call, stackedBars);

        if (hasSplitParent && stackedBars)
        {
            for (int i = plan.SplitParents.Count - 1; i >= 0; i--)
            {
                var frame = plan.SplitParents[i];
                if (frame.IsLastSubPart)
                    sb.Append("    ").Append(frame.CalleeParticipantId).Append("-->>-").Append(frame.CallerParticipantId).Append(": ").AppendLine(EscapeLabel(BuildReturnLabel(frame.MessageLabel, frame.ReturnTypeLabel)));
                else
                    sb.Append("    deactivate ").AppendLine(frame.CalleeParticipantId);
            }

            if (splitCallerNeedsOwnBar)
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
            sb.Append("    ").Append(to).Append("-->>-").Append(from).Append(": ").AppendLine(EscapeLabel(BuildReturnLabel(call.MessageLabel, call.ReturnTypeLabel)));
        }
        else
        {
            sb.Append("    ").Append(from).Append("->>").Append(to).Append(": ").AppendLine(label);
            foreach (var nested in call.NestedCalls)
                EmitCall(sb, nested, stackedActivationBars);
        }
    }

    // Labels a return arrow with which call is returning and, when known, its declared return
    // type — e.g. "BigEntry returns int" — so the return stays identifiable even when it lands
    // far from its opening arrow (a different split part, or just many lines further down).
    private static string BuildReturnLabel(string callLabel, string returnTypeLabel)
        => string.IsNullOrEmpty(returnTypeLabel) ? callLabel : callLabel + " returns " + returnTypeLabel;

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
        sb.AppendLine("      document.getElementById('dseq-' + i)?.remove(); // mermaid leaves its error SVG in <body>");
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
