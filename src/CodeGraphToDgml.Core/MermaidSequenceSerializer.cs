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

        // Single-diagram path – original HTML (keeps existing tests passing)
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

    // ── Section building ─────────────────────────────────────────────────────

    private sealed record DiagramSection(string Heading, string Content);

    private List<DiagramSection> BuildSections(CallSequence sequence, bool stackedBars, bool autoNumber, int maxPerDiagram)
    {
        if (maxPerDiagram <= 0 || sequence.Participants.Count <= maxPerDiagram)
            return [new DiagramSection("", Serialize(sequence, stackedBars, autoNumber))];

        var allCalls = FlattenByDepth(sequence.RootCalls);
        if (allCalls.Count == 0)
            return [new DiagramSection("", Serialize(sequence, stackedBars, autoNumber))];

        var ranges = ComputeDepthRanges(allCalls, maxPerDiagram);
        if (ranges.Count <= 1)
            return [new DiagramSection("", Serialize(sequence, stackedBars, autoNumber))];

        int actualMaxDepth = allCalls.Max(c => c.Depth);
        var sections = new List<DiagramSection>(ranges.Count);

        for (int i = 0; i < ranges.Count; i++)
        {
            var (minD, maxD) = ranges[i];
            var content = SerializeRange(sequence, sequence.RootCalls, minD, maxD, stackedBars, autoNumber);

            int participantCount = allCalls
                .Where(c => c.Depth >= minD && c.Depth <= maxD)
                .SelectMany(c => new[] { c.Call.CallerParticipantId, c.Call.CalleeParticipantId })
                .Distinct(StringComparer.Ordinal)
                .Count();

            int displayMax = maxD == int.MaxValue ? actualMaxDepth : maxD;
            var heading = $"Part {i + 1} of {ranges.Count} — Levels {minD}–{displayMax} ({participantCount} participants)";
            sections.Add(new DiagramSection(heading, content));
        }

        return sections;
    }

    private string SerializeRange(
        CallSequence sequence,
        IReadOnlyList<CallSequenceCallNode> rootCalls,
        int minDepth,
        int maxDepth,
        bool stackedActivationBars,
        bool autoNumber)
    {
        var participantIds = new HashSet<string>(StringComparer.Ordinal);
        CollectParticipantIds(rootCalls, 1, minDepth, maxDepth, participantIds);

        var sb = new StringBuilder();
        sb.AppendLine("sequenceDiagram");
        if (autoNumber)
            sb.AppendLine("    autonumber");

        foreach (var p in sequence.Participants)
        {
            if (participantIds.Contains(p.Id))
            {
                sb.Append("    participant ").Append(p.Id).Append(" as ").AppendLine(EscapeLabel(p.Label));
            }
        }

        if (participantIds.Count > 0)
            sb.AppendLine();

        foreach (var call in rootCalls)
            EmitCallInRange(sb, call, 1, minDepth, maxDepth, stackedActivationBars);

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

    private static void EmitCallInRange(
        StringBuilder sb,
        CallSequenceCallNode call,
        int currentDepth,
        int minDepth,
        int maxDepth,
        bool stackedActivationBars)
    {
        if (currentDepth > maxDepth) return;

        if (currentDepth >= minDepth)
        {
            var from = call.CallerParticipantId;
            var to = call.CalleeParticipantId;
            var label = EscapeLabel(call.MessageLabel);

            if (stackedActivationBars)
            {
                sb.Append("    ").Append(from).Append("->>+").Append(to).Append(": ").AppendLine(label);
                foreach (var nested in call.NestedCalls)
                    EmitCallInRange(sb, nested, currentDepth + 1, minDepth, maxDepth, stackedActivationBars);
                sb.Append("    ").Append(to).Append("-->>-").Append(from).AppendLine(": ");
            }
            else
            {
                sb.Append("    ").Append(from).Append("->>").Append(to).Append(": ").AppendLine(label);
                foreach (var nested in call.NestedCalls)
                    EmitCallInRange(sb, nested, currentDepth + 1, minDepth, maxDepth, stackedActivationBars);
            }
        }
        else
        {
            // Above minDepth: recurse without emitting to reach calls within the range.
            foreach (var nested in call.NestedCalls)
                EmitCallInRange(sb, nested, currentDepth + 1, minDepth, maxDepth, stackedActivationBars);
        }
    }

    // ── Splitting helpers ─────────────────────────────────────────────────────

    private static List<(CallSequenceCallNode Call, int Depth)> FlattenByDepth(IReadOnlyList<CallSequenceCallNode> calls)
    {
        var result = new List<(CallSequenceCallNode, int)>();
        FlattenByDepthInto(calls, 1, result);
        return result;
    }

    private static void FlattenByDepthInto(IReadOnlyList<CallSequenceCallNode> calls, int depth, List<(CallSequenceCallNode, int)> result)
    {
        foreach (var call in calls)
        {
            result.Add((call, depth));
            FlattenByDepthInto(call.NestedCalls, depth + 1, result);
        }
    }

    private static void CollectParticipantIds(
        IReadOnlyList<CallSequenceCallNode> calls,
        int currentDepth,
        int minDepth,
        int maxDepth,
        HashSet<string> ids)
    {
        if (currentDepth > maxDepth) return;
        foreach (var call in calls)
        {
            if (currentDepth >= minDepth)
            {
                ids.Add(call.CallerParticipantId);
                ids.Add(call.CalleeParticipantId);
            }
            CollectParticipantIds(call.NestedCalls, currentDepth + 1, minDepth, maxDepth, ids);
        }
    }

    private static List<(int MinDepth, int MaxDepth)> ComputeDepthRanges(
        List<(CallSequenceCallNode Call, int Depth)> allCalls,
        int maxParticipants)
    {
        var depthLevels = allCalls
            .GroupBy(c => c.Depth)
            .OrderBy(g => g.Key)
            .Select(g => (
                Depth: g.Key,
                Participants: new HashSet<string>(
                    g.SelectMany(c => new[] { c.Call.CallerParticipantId, c.Call.CalleeParticipantId }),
                    StringComparer.Ordinal)))
            .ToList();

        if (depthLevels.Count == 0)
            return [];

        var ranges = new List<(int, int)>();
        int rangeStart = depthLevels[0].Depth;
        var current = new HashSet<string>(StringComparer.Ordinal);

        foreach (var (depth, levelParticipants) in depthLevels)
        {
            var combined = new HashSet<string>(current, StringComparer.Ordinal);
            combined.UnionWith(levelParticipants);

            if (combined.Count > maxParticipants && current.Count > 0)
            {
                ranges.Add((rangeStart, depth - 1));
                rangeStart = depth;
                current = new HashSet<string>(levelParticipants, StringComparer.Ordinal);
            }
            else
            {
                current = combined;
            }
        }

        ranges.Add((rangeStart, int.MaxValue));
        return ranges;
    }

    // ── HTML helpers ──────────────────────────────────────────────────────────

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
        sb.AppendLine(".section { margin: 20px 16px; }");
        sb.AppendLine(".section-bar { display: flex; align-items: center; gap: 8px; flex-wrap: wrap;");
        sb.AppendLine("  padding: 7px 12px; background: #fff; border: 1px solid #d0d0d0;");
        sb.AppendLine("  border-radius: 6px 6px 0 0; }");
        sb.AppendLine(".section-bar h2 { font-size: 13px; font-weight: 600; flex: 1; min-width: 0;");
        sb.AppendLine("  white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }");
        sb.AppendLine(".section-bar button { padding: 3px 10px; border: 1px solid #bbb; border-radius: 4px;");
        sb.AppendLine("  background: #fff; cursor: pointer; font-size: 12px; user-select: none; }");
        sb.AppendLine(".section-bar button:hover { background: #f0f0f0; }");
        sb.AppendLine(".zoom-pct { font-size: 12px; min-width: 40px; text-align: center; color: #555; }");
        sb.AppendLine(".hint { font-size: 11px; color: #999; }");
        sb.AppendLine(".diagram-scroll { overflow: auto; background: #fff; border: 1px solid #d0d0d0;");
        sb.AppendLine("  border-top: none; border-radius: 0 0 6px 6px; padding: 24px; min-height: 80px; }");
        sb.AppendLine(".diagram-wrap { display: inline-block; }");
        sb.AppendLine(".diagram-wrap svg { display: block; border-radius: 6px;");
        sb.AppendLine("  box-shadow: 0 1px 8px rgba(0,0,0,.12); background: #fff; padding: 16px; }");
        sb.AppendLine("</style>");
        sb.AppendLine("</head>");
        sb.AppendLine("<body>");

        // Sticky page header with TOC
        sb.AppendLine("<div class=\"page-header\">");
        sb.Append("  <h1>Sequence: ").Append(htmlTitle).AppendLine("</h1>");
        sb.AppendLine("  <nav class=\"toc\">");
        for (int i = 0; i < sections.Count; i++)
        {
            sb.Append("    <a href=\"#s").Append(i).Append("\">Part ").Append(i + 1).Append(" of ").Append(sections.Count).AppendLine("</a>");
        }
        sb.AppendLine("  </nav>");
        sb.AppendLine("</div>");

        // One card per section
        for (int i = 0; i < sections.Count; i++)
        {
            sb.Append("<div class=\"section\" id=\"s").Append(i).AppendLine("\">");
            sb.AppendLine("  <div class=\"section-bar\">");
            sb.Append("    <h2>").Append(EscapeHtml(sections[i].Heading)).AppendLine("</h2>");
            sb.Append("    <button onclick=\"diagramZoom(").Append(i).AppendLine(",-0.1)\" title=\"Zoom out\">&#8722;</button>");
            sb.Append("    <span class=\"zoom-pct\" id=\"pct-").Append(i).AppendLine("\">100%</span>");
            sb.Append("    <button onclick=\"diagramZoom(").Append(i).AppendLine(",0.1)\" title=\"Zoom in\">+</button>");
            sb.Append("    <button onclick=\"diagramReset(").Append(i).AppendLine(")\">Reset</button>");
            sb.Append("    <button onclick=\"diagramFit(").Append(i).AppendLine(")\">Fit width</button>");
            sb.AppendLine("    <span class=\"hint\">Ctrl+scroll to zoom</span>");
            sb.AppendLine("  </div>");
            sb.Append("  <div class=\"diagram-scroll\" id=\"scroll-").Append(i).AppendLine("\">");
            sb.AppendLine("    <div class=\"diagram-wrap\"></div>");
            sb.AppendLine("  </div>");
            sb.AppendLine("</div>");
        }

        // Script
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
    // Encodes '<' as < to prevent the HTML parser from finding </script> inside the tag.
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
