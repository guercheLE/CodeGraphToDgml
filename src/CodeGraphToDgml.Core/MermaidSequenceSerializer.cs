using System.Text;

namespace CodeGraphToDgml.Core;

public sealed class MermaidSequenceSerializer
{
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

    private static string EscapeLabel(string label)
        => label
            .Replace(":", "#colon;")
            .Replace("<", "#lt;")
            .Replace(">", "#gt;")
            .Replace("&", "#amp;");

    public string BuildMarkdown(CallSequence sequence, bool stackedActivationBars = true, bool autoNumber = false)
    {
        var sb = new StringBuilder();
        sb.Append("# Sequence: ").AppendLine(sequence.Title);
        sb.AppendLine();
        sb.AppendLine("```mermaid");
        sb.AppendLine(Serialize(sequence, stackedActivationBars, autoNumber));
        sb.AppendLine("```");
        return sb.ToString();
    }

    public string BuildHtml(CallSequence sequence, bool stackedActivationBars = true, bool autoNumber = false)
    {
        var mermaid = Serialize(sequence, stackedActivationBars, autoNumber);
        var jsSource = ToJsString(mermaid);
        var htmlTitle = EscapeHtml(sequence.Title);

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

    private static string EscapeHtml(string text)
        => text
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;")
            .Replace("\"", "&quot;");

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
