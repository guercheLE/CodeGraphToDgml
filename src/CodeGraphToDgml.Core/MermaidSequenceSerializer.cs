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

        foreach (var p in sequence.Participants)
        {
            sb.Append("    participant ");
            sb.Append(p.Id);
            sb.Append(" as ");
            sb.AppendLine(EscapeLabel(p.Label));
        }

        if (sequence.Participants.Count > 0 && sequence.RootCalls.Count > 0)
            sb.AppendLine();

        foreach (var call in sequence.RootCalls)
            EmitCall(sb, call, stackedActivationBars);

        while (sb.Length > 0 && (sb[sb.Length - 1] == '\r' || sb[sb.Length - 1] == '\n'))
            sb.Length--;

        return sb.ToString();
    }

    public string BuildMarkdown(CallSequence sequence, bool stackedActivationBars = true, bool autoNumber = false, int maxParticipantsPerDiagram = 0)
    {
        var sections = BuildSections(sequence, stackedActivationBars, autoNumber, maxParticipantsPerDiagram);

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

    public string BuildHtml(CallSequence sequence, bool stackedActivationBars = true, bool autoNumber = false, int maxParticipantsPerDiagram = 0)
    {
        var sections = BuildSections(sequence, stackedActivationBars, autoNumber, maxParticipantsPerDiagram);
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
        public string? PrevPart { get; set; }
        public string? NextPart { get; set; }
        public string? PrevTitle { get; set; }
        public string? NextTitle { get; set; }
        public List<CallSequenceCallNode> VirtualRootCalls { get; set; } = new List<CallSequenceCallNode>();
    }

    private sealed record DiagramSection(string Heading, string Content, SegmentPlan? Plan = null);

    // ── Section building ──────────────────────────────────────────────────────

    private List<DiagramSection> BuildSections(CallSequence sequence, bool stackedBars, bool autoNumber, int maxPerDiagram)
    {
        if (maxPerDiagram <= 0 || sequence.Participants.Count <= maxPerDiagram)
            return [new DiagramSection("", Serialize(sequence, stackedBars, autoNumber))];

        if (sequence.RootCalls.Count == 0)
            return [new DiagramSection("", Serialize(sequence, stackedBars, autoNumber))];

        var plans = ComputeSegmentPlans(sequence, maxPerDiagram, stackedBars);

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

    private static List<SegmentPlan> ComputeSegmentPlans(CallSequence sequence, int maxParticipants, bool stackedBars)
    {
        // Phase 1: group root calls into natural phases (greedy by participant count)
        var phases = GroupRootCallsIntoPhases(sequence.RootCalls, maxParticipants);

        var allPlans = new List<SegmentPlan>();
        int phaseNumber = 1;
        int msgCounter = 1;

        foreach (var phase in phases)
        {
            var phaseParts = GetCallListParticipants(phase);

            List<(List<CallSequenceCallNode> Calls, HashSet<string> Participants)> subSegs;
            if (phaseParts.Count <= maxParticipants)
            {
                subSegs = new List<(List<CallSequenceCallNode>, HashSet<string>)> { (phase, phaseParts) };
            }
            else
            {
                subSegs = SplitPhaseIntoSubParts(phase, maxParticipants);
            }

            bool hasSubParts = subSegs.Count > 1;

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

        return allPlans;
    }

    private static List<List<CallSequenceCallNode>> GroupRootCallsIntoPhases(
        IReadOnlyList<CallSequenceCallNode> rootCalls,
        int maxParticipants)
    {
        var phases = new List<List<CallSequenceCallNode>>();
        var currentPhase = new List<CallSequenceCallNode>();
        var currentParticipants = new HashSet<string>(StringComparer.Ordinal);

        foreach (var call in rootCalls)
        {
            var callParticipants = GetSubtreeParticipants(call);
            var combined = new HashSet<string>(currentParticipants, StringComparer.Ordinal);
            combined.UnionWith(callParticipants);

            if (combined.Count > maxParticipants && currentPhase.Count > 0)
            {
                phases.Add(new List<CallSequenceCallNode>(currentPhase));
                currentPhase.Clear();
                currentParticipants.Clear();
            }

            currentPhase.Add(call);
            currentParticipants.UnionWith(callParticipants);
        }

        if (currentPhase.Count > 0)
            phases.Add(currentPhase);

        return phases;
    }

    private static List<(List<CallSequenceCallNode> Calls, HashSet<string> Participants)> SplitPhaseIntoSubParts(
        List<CallSequenceCallNode> phaseCalls,
        int maxParticipants)
    {
        // Multiple calls in phase: try greedy grouping
        if (phaseCalls.Count > 1)
        {
            var result = new List<(List<CallSequenceCallNode>, HashSet<string>)>();
            var current = new List<CallSequenceCallNode>();
            var currentParts = new HashSet<string>(StringComparer.Ordinal);

            foreach (var call in phaseCalls)
            {
                var callParts = GetSubtreeParticipants(call);
                var combined = new HashSet<string>(currentParts, StringComparer.Ordinal);
                combined.UnionWith(callParts);

                if (combined.Count > maxParticipants && current.Count > 0)
                {
                    result.Add((new List<CallSequenceCallNode>(current), new HashSet<string>(currentParts, StringComparer.Ordinal)));
                    current.Clear();
                    currentParts.Clear();
                }

                current.Add(call);
                currentParts.UnionWith(callParts);
            }

            if (current.Count > 0)
                result.Add((current, currentParts));

            if (result.Count > 1) return result;
        }

        // Single oversized call: promote its nested calls as virtual roots
        var parentCall = phaseCalls[0];
        if (parentCall.NestedCalls.Count == 0)
            return [(phaseCalls, GetCallListParticipants(phaseCalls))];

        var boundary = new HashSet<string>(StringComparer.Ordinal)
        {
            parentCall.CallerParticipantId,
            parentCall.CalleeParticipantId,
        };

        var subResult = new List<(List<CallSequenceCallNode>, HashSet<string>)>();
        var subCurrent = new List<CallSequenceCallNode>();
        var subParts = new HashSet<string>(boundary, StringComparer.Ordinal);

        foreach (var nested in parentCall.NestedCalls)
        {
            var nestedParts = GetSubtreeParticipants(nested);
            var combined = new HashSet<string>(subParts, StringComparer.Ordinal);
            combined.UnionWith(nestedParts);

            if (combined.Count > maxParticipants && subCurrent.Count > 0)
            {
                var segParts = new HashSet<string>(subParts, StringComparer.Ordinal);
                subResult.Add((new List<CallSequenceCallNode>(subCurrent), segParts));
                subCurrent.Clear();
                subParts = new HashSet<string>(boundary, StringComparer.Ordinal);
            }

            subCurrent.Add(nested);
            subParts.UnionWith(nestedParts);
        }

        if (subCurrent.Count > 0)
            subResult.Add((subCurrent, subParts));

        // Ensure boundary participants appear in every sub-segment
        foreach (var (_, parts) in subResult)
            parts.UnionWith(boundary);

        if (subResult.Count <= 1)
            return [(phaseCalls, GetCallListParticipants(phaseCalls))];

        return subResult;
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

    private static int CountArrows(List<CallSequenceCallNode> calls, bool stackedBars)
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

        var segSet = new HashSet<string>(plan.ParticipantIds, StringComparer.Ordinal);
        foreach (var p in sequence.Participants)
        {
            if (segSet.Contains(p.Id))
                sb.Append("    participant ").Append(p.Id).Append(" as ").AppendLine(EscapeLabel(p.Label));
        }

        if (segSet.Count > 0 && plan.VirtualRootCalls.Count > 0)
            sb.AppendLine();

        foreach (var call in plan.VirtualRootCalls)
            EmitCall(sb, call, stackedBars);

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
