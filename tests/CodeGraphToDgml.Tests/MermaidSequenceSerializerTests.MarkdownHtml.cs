using System.Linq;
using CodeGraphToDgml.Core;

namespace CodeGraphToDgml.Tests;

// BuildMarkdown and BuildHtml output (structure, security, encoding).
public sealed partial class MermaidSequenceSerializerTests
{
    // ──────────────────────────────────────────────────────────────────────────
    // BuildMarkdown
    // ──────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void BuildMarkdown_ContainsTitleHeading()
    {
        var sequence = new CallSequence { Title = "MyClass.DoWork" };
        var result = Serializer.BuildMarkdown(sequence);
        Assert.Contains("# Sequence: MyClass.DoWork", result);
    }

    [TestMethod]
    public void BuildMarkdown_ContainsMermaidFenceBlock()
    {
        var result = Serializer.BuildMarkdown(new CallSequence());
        Assert.Contains("```mermaid", result);
        Assert.Contains("sequenceDiagram", result);
        // At least two ``` markers (open + close)
        Assert.IsGreaterThanOrEqualTo(3, result.Split(["```"], StringSplitOptions.None).Length);
    }

    [TestMethod]
    public void BuildMarkdown_MermaidContentInsideFence()
    {
        var sequence = new CallSequence
        {
            Title = "X",
            Participants = [P("A", "A"), P("B", "B")],
            RootCalls = [Leaf("A", "B", "Go")],
        };

        var result = Serializer.BuildMarkdown(sequence);
        int fenceOpen = result.IndexOf("```mermaid");
        int fenceClose = result.LastIndexOf("```");
        int callLine  = result.IndexOf("A->>+B: Go");

        Assert.IsGreaterThan(fenceOpen, callLine, "Call line must be inside the opening fence");
        Assert.IsLessThan(fenceClose, callLine, "Call line must be before the closing fence");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // BuildHtml — structure
    // ──────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void BuildHtml_IsWellFormedHtmlDocument()
    {
        var result = Serializer.BuildHtml(new CallSequence { Title = "Test" });

        Assert.StartsWith("<!DOCTYPE html>", result.TrimStart());
        Assert.Contains("<html", result);
        Assert.Contains("</html>", result);
        Assert.Contains("<head>", result);
        Assert.Contains("</head>", result);
        Assert.Contains("<body>", result);
        Assert.Contains("</body>", result);
    }

    [TestMethod]
    public void BuildHtml_ContainsMermaidCdnImport()
    {
        var result = Serializer.BuildHtml(new CallSequence { Title = "Test" });
        Assert.Contains("cdn.jsdelivr.net/npm/mermaid@11", result);
    }

    [TestMethod]
    public void BuildHtml_ContainsDiagramSourceAsJsVariable()
    {
        var result = Serializer.BuildHtml(new CallSequence { Title = "Test" });
        Assert.Contains("const DIAGRAM_SOURCE =", result);
    }

    [TestMethod]
    public void BuildHtml_ContainsZoomControls()
    {
        var result = Serializer.BuildHtml(new CallSequence { Title = "Test" });
        Assert.Contains("zoom(", result);
        Assert.Contains("fitWidth", result);
        Assert.Contains("resetZoom", result);
    }

    [TestMethod]
    public void BuildHtml_ContainsDiagramContainer()
    {
        var result = Serializer.BuildHtml(new CallSequence { Title = "Test" });
        Assert.Contains("diagram-container", result);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // BuildHtml — security / encoding
    // ──────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void BuildHtml_TitleWithHtmlSpecialChars_IsEscaped()
    {
        var sequence = new CallSequence { Title = "<script>alert('xss')</script>" };
        var result = Serializer.BuildHtml(sequence);

        Assert.DoesNotContain("<script>alert", result, "Raw XSS script tag must not appear in HTML");
        Assert.Contains("&lt;script&gt;", result);
    }

    [TestMethod]
    public void BuildHtml_TitleWithAmpersand_IsHtmlEscaped()
    {
        var sequence = new CallSequence { Title = "A & B" };
        var result = Serializer.BuildHtml(sequence);

        Assert.Contains("A &amp; B", result);
    }

    [TestMethod]
    public void BuildHtml_ScriptBlockDoesNotContainRawScriptCloseTag()
    {
        // If mermaid source contained </script>, it would break HTML parsing.
        var result = Serializer.BuildHtml(new CallSequence { Title = "Test" });

        const string scriptOpen = "<script type=\"module\">";
        int start = result.IndexOf(scriptOpen) + scriptOpen.Length;
        int end   = result.LastIndexOf("</script>");
        var scriptContent = result[start..end];

        Assert.DoesNotContain("</script", scriptContent, "Script block must not contain </script");
    }

    [TestMethod]
    public void BuildHtml_MermaidArrowsEncodedAsUnicodeEscapesInJsString()
    {
        // Mermaid arrows (->> -->>-) contain '>' which must be > in the JS string
        // to prevent the HTML parser from finding </script> inside the script tag.
        var sequence = new CallSequence
        {
            Participants = [P("A", "A"), P("B", "B")],
            RootCalls = [Leaf("A", "B", "M")],
        };

        var html = Serializer.BuildHtml(sequence);

        const string varPrefix = "const DIAGRAM_SOURCE = \"";
        int markerIdx = html.IndexOf(varPrefix);
        Assert.IsGreaterThanOrEqualTo(0, markerIdx, "DIAGRAM_SOURCE variable not found");

        int contentStart = markerIdx + varPrefix.Length;
        int contentEnd   = html.IndexOf("\";", contentStart);
        var jsStringContent = html[contentStart..contentEnd];

        Assert.Contains("\\u003e", jsStringContent, "Arrow '>' must be unicode-escaped to \\u003e");
        Assert.DoesNotContain('>', jsStringContent, "Raw '>' must not appear in the JS string literal");
    }

    [TestMethod]
    public void BuildHtml_AngleBracketsInDiagramSource_NeverRaw()
    {
        // Even when the mermaid source has angle-bracket escapes (#lt; #gt;),
        // no raw '<' or '>' should appear inside the JS string in the HTML.
        var sequence = new CallSequence
        {
            Participants = [P("A", "A"), P("B", "B")],
            RootCalls = [Leaf("A", "B", "Get<T>")],  // label will become Get#lt;T#gt;
        };

        var html = Serializer.BuildHtml(sequence);

        const string varPrefix = "const DIAGRAM_SOURCE = \"";
        int contentStart = html.IndexOf(varPrefix) + varPrefix.Length;
        int contentEnd   = html.IndexOf("\";", contentStart);
        var jsString = html[contentStart..contentEnd];

        Assert.DoesNotContain('<', jsString, "No raw '<' in JS string literal");
        Assert.DoesNotContain('>', jsString, "No raw '>' in JS string literal");
    }

    [TestMethod]
    public void BuildHtml_BackslashInLabel_IsDoubleEscapedInJsString()
    {
        // A backslash in the mermaid label must become \\ in the JS string.
        var sequence = new CallSequence
        {
            Participants = [P("A", "A"), P("B", "B")],
            RootCalls = [Leaf("A", "B", @"Path\File")],
        };

        var html = Serializer.BuildHtml(sequence);

        const string varPrefix = "const DIAGRAM_SOURCE = \"";
        int contentStart = html.IndexOf(varPrefix) + varPrefix.Length;
        int contentEnd   = html.IndexOf("\";", contentStart);
        var jsString = html[contentStart..contentEnd];

        // In HTML the JS string literal: "...Path\\File..." — C# sees it as "...Path\\File..."
        Assert.Contains(@"\\", jsString);
    }

    [TestMethod]
    public void BuildHtml_NewlinesInMermaidSource_EncodedAsLiteralEscape()
    {
        // Need a multi-line mermaid source (participants + calls) to have newlines to encode.
        var sequence = new CallSequence
        {
            Title = "Test",
            Participants = [P("A", "A"), P("B", "B")],
            RootCalls = [Leaf("A", "B", "Go")],
        };
        var result = Serializer.BuildHtml(sequence);

        const string varPrefix = "const DIAGRAM_SOURCE = \"";
        int contentStart = result.IndexOf(varPrefix) + varPrefix.Length;
        int contentEnd   = result.IndexOf("\";", contentStart);
        var jsString = result[contentStart..contentEnd];

        // Newlines must be encoded as \n, not embedded literally
        Assert.DoesNotContain('\n', jsString, "Literal newlines in JS string literal are invalid");
        Assert.Contains("\\n", jsString, "Newlines must be encoded as \\n");
    }
}
