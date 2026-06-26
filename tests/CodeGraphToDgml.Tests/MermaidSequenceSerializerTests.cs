using System.Linq;
using CodeGraphToDgml.Core;

namespace CodeGraphToDgml.Tests;

[TestClass]
public sealed class MermaidSequenceSerializerTests
{
    private static readonly MermaidSequenceSerializer Serializer = new();

    // ──────────────────────────────────────────────────────────────────────────
    // Helpers
    // ──────────────────────────────────────────────────────────────────────────

    private static CallSequenceCallNode Leaf(string from, string to, string label)
        => new(from, to, label, Array.Empty<CallSequenceCallNode>());

    private static CallSequenceCallNode Nested(string from, string to, string label, params CallSequenceCallNode[] children)
        => new(from, to, label, children);

    private static CallSequenceParticipant P(string id, string label) => new(id, label);

    /// <summary>A→B→C linear chain with participant declarations.</summary>
    private static CallSequence LinearChain() => new()
    {
        Title = "ClassA.Root",
        Participants = new[] { P("ClassA", "ClassA"), P("ClassB", "ClassB"), P("ClassC", "ClassC") },
        RootCalls = new[]
        {
            Nested("ClassA", "ClassB", "DoB",
                Leaf("ClassB", "ClassC", "DoC")),
        },
    };

    // ──────────────────────────────────────────────────────────────────────────
    // Serialize — structure
    // ──────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void Serialize_EmptySequence_ProducesHeaderOnly()
    {
        var result = Serializer.Serialize(new CallSequence());
        Assert.AreEqual("sequenceDiagram", result.Trim());
    }

    [TestMethod]
    public void Serialize_StartsWithSequenceDiagramKeyword()
    {
        var result = Serializer.Serialize(LinearChain());
        Assert.IsTrue(result.TrimStart().StartsWith("sequenceDiagram"), result);
    }

    [TestMethod]
    public void Serialize_ParticipantsOnly_ProducesDeclarations()
    {
        var sequence = new CallSequence
        {
            Participants = new[] { P("SvcA", "ServiceA"), P("SvcB", "ServiceB") },
        };

        var result = Serializer.Serialize(sequence);
        StringAssert.Contains(result, "participant SvcA as ServiceA");
        StringAssert.Contains(result, "participant SvcB as ServiceB");
    }

    [TestMethod]
    public void Serialize_ParticipantsAppearsInDeclarationOrder()
    {
        var sequence = new CallSequence
        {
            Participants = new[] { P("First", "First"), P("Second", "Second"), P("Third", "Third") },
        };

        var result = Serializer.Serialize(sequence);
        Assert.IsTrue(result.IndexOf("participant First") < result.IndexOf("participant Second"));
        Assert.IsTrue(result.IndexOf("participant Second") < result.IndexOf("participant Third"));
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Serialize — arrow types
    // ──────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void Serialize_LeafCall_UsesSimpleArrow()
    {
        var sequence = new CallSequence
        {
            Participants = new[] { P("A", "A"), P("B", "B") },
            RootCalls = new[] { Leaf("A", "B", "Foo") },
        };

        var result = Serializer.Serialize(sequence);
        StringAssert.Contains(result, "A->>B: Foo");
        Assert.IsFalse(result.Contains("->>+"), "Leaf call must not activate");
        Assert.IsFalse(result.Contains("-->>-"), "Leaf call must not deactivate");
    }

    [TestMethod]
    public void Serialize_NestedCall_UsesActivationArrows()
    {
        var result = Serializer.Serialize(LinearChain());

        StringAssert.Contains(result, "ClassA->>+ClassB: DoB");
        StringAssert.Contains(result, "ClassB->>ClassC: DoC");
        StringAssert.Contains(result, "ClassB-->>-ClassA: ");
    }

    [TestMethod]
    public void Serialize_NestedCall_StackedBarsOff_UsesPlainArrows()
    {
        var result = Serializer.Serialize(LinearChain(), stackedActivationBars: false);

        StringAssert.Contains(result, "ClassA->>ClassB: DoB");
        StringAssert.Contains(result, "ClassB->>ClassC: DoC");
        Assert.IsFalse(result.Contains("->>+"), "No activation markers when stacked bars off");
        Assert.IsFalse(result.Contains("-->>-"), "No deactivation markers when stacked bars off");
    }

    [TestMethod]
    public void Serialize_SelfCall_SameParticipantBothEnds()
    {
        var sequence = new CallSequence
        {
            Participants = new[] { P("MyClass", "MyClass") },
            RootCalls = new[] { Leaf("MyClass", "MyClass", "Helper") },
        };

        var result = Serializer.Serialize(sequence);
        StringAssert.Contains(result, "MyClass->>MyClass: Helper");
    }

    [TestMethod]
    public void Serialize_NestedSelfCall_StackedBarsOff_NoActivationMarkers()
    {
        var sequence = new CallSequence
        {
            Participants = new[] { P("A", "A"), P("B", "B") },
            RootCalls = new[]
            {
                Nested("A", "A", "SelfWithNested",
                    Leaf("A", "B", "Inner")),
            },
        };

        var result = Serializer.Serialize(sequence, stackedActivationBars: false);
        StringAssert.Contains(result, "A->>A: SelfWithNested");
        StringAssert.Contains(result, "A->>B: Inner");
        Assert.IsFalse(result.Contains("->>+"), "No activation markers when stacked bars off");
        Assert.IsFalse(result.Contains("-->>-"), "No deactivation markers when stacked bars off");
    }

    [TestMethod]
    public void Serialize_NestedSelfCall_StackedBarsOn_UsesActivationMarkers()
    {
        var sequence = new CallSequence
        {
            Participants = new[] { P("A", "A"), P("B", "B") },
            RootCalls = new[]
            {
                Nested("A", "A", "SelfWithNested",
                    Leaf("A", "B", "Inner")),
            },
        };

        var result = Serializer.Serialize(sequence, stackedActivationBars: true);
        StringAssert.Contains(result, "A->>+A: SelfWithNested");
        StringAssert.Contains(result, "A->>B: Inner");
        StringAssert.Contains(result, "A-->>-A: ");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Serialize — ordering
    // ──────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void Serialize_LinearChain_DeactivationFollowsNestedCall()
    {
        var result = Serializer.Serialize(LinearChain());
        var lines = result.Split('\n').Select(l => l.Trim()).ToArray();

        int callB = Array.FindIndex(lines, l => l.StartsWith("ClassA->>+ClassB"));
        int callC = Array.FindIndex(lines, l => l.StartsWith("ClassB->>ClassC"));
        int retA  = Array.FindIndex(lines, l => l.StartsWith("ClassB-->>-ClassA"));

        Assert.IsTrue(callB < callC, "Call to B should precede call to C");
        Assert.IsTrue(callC < retA,  "Call to C should precede deactivation of B→A");
    }

    [TestMethod]
    public void Serialize_SiblingCalls_BothArrowsPresent()
    {
        var sequence = new CallSequence
        {
            Participants = new[] { P("A", "A"), P("B", "B"), P("C", "C") },
            RootCalls = new[]
            {
                Leaf("A", "B", "Foo"),
                Leaf("A", "C", "Bar"),
            },
        };

        var result = Serializer.Serialize(sequence);
        StringAssert.Contains(result, "A->>B: Foo");
        StringAssert.Contains(result, "A->>C: Bar");
    }

    [TestMethod]
    public void Serialize_SiblingCallOrder_IsPreserved()
    {
        var sequence = new CallSequence
        {
            Participants = new[] { P("A", "A"), P("B", "B"), P("C", "C") },
            RootCalls = new[]
            {
                Leaf("A", "B", "First"),
                Leaf("A", "C", "Second"),
            },
        };

        var result = Serializer.Serialize(sequence);
        Assert.IsTrue(result.IndexOf("First") < result.IndexOf("Second"));
    }

    [TestMethod]
    public void Serialize_DeepNesting_ProducesCorrectActivationStack()
    {
        // A→B→C→D (all nested)
        var sequence = new CallSequence
        {
            Title = "A.Entry",
            Participants = new[] { P("A", "A"), P("B", "B"), P("C", "C"), P("D", "D") },
            RootCalls = new[]
            {
                Nested("A", "B", "Top",
                    Nested("B", "C", "Mid",
                        Leaf("C", "D", "Deep"))),
            },
        };

        var result = Serializer.Serialize(sequence);
        var lines = result.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToArray();

        int callB = Array.FindIndex(lines, l => l.StartsWith("A->>+B"));
        int callC = Array.FindIndex(lines, l => l.StartsWith("B->>+C"));
        int callD = Array.FindIndex(lines, l => l.StartsWith("C->>D"));
        int retB  = Array.FindIndex(lines, l => l.StartsWith("C-->>-B"));
        int retA  = Array.FindIndex(lines, l => l.StartsWith("B-->>-A"));

        Assert.IsTrue(callB >= 0, "Missing A->>+B");
        Assert.IsTrue(callC >= 0, "Missing B->>+C");
        Assert.IsTrue(callD >= 0, "Missing C->>D (leaf, no +)");
        Assert.IsTrue(retB  >= 0, "Missing C-->>-B");
        Assert.IsTrue(retA  >= 0, "Missing B-->>-A");

        Assert.IsTrue(callB < callC, "A→B must precede B→C");
        Assert.IsTrue(callC < callD, "B→C must precede C→D");
        Assert.IsTrue(callD < retB,  "C→D must precede deactivation of C");
        Assert.IsTrue(retB  < retA,  "C deactivation must precede B deactivation");
    }

    [TestMethod]
    public void Serialize_MultipleRootCallsWithNesting_OrderPreserved()
    {
        // A calls B (nested: B calls C), then A calls D
        var sequence = new CallSequence
        {
            Participants = new[] { P("A", "A"), P("B", "B"), P("C", "C"), P("D", "D") },
            RootCalls = new[]
            {
                Nested("A", "B", "WithNested", Leaf("B", "C", "Inner")),
                Leaf("A", "D", "After"),
            },
        };

        var result = Serializer.Serialize(sequence);
        var lines = result.Split('\n').Select(l => l.Trim()).ToArray();

        int callB   = Array.FindIndex(lines, l => l.StartsWith("A->>+B"));
        int inner   = Array.FindIndex(lines, l => l.Contains("Inner"));
        int retA    = Array.FindIndex(lines, l => l.StartsWith("B-->>-A"));
        int callD   = Array.FindIndex(lines, l => l.Contains("After"));

        Assert.IsTrue(callB < inner, "Call B must precede inner call");
        Assert.IsTrue(inner < retA, "Inner call must precede B deactivation");
        Assert.IsTrue(retA < callD, "B deactivation must precede call to D");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Serialize — label escaping
    // ──────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void Serialize_LabelWithColon_IsEscaped()
    {
        var sequence = new CallSequence
        {
            Participants = new[] { P("A", "A"), P("B", "B") },
            RootCalls = new[] { Leaf("A", "B", "Method: special") },
        };

        var result = Serializer.Serialize(sequence);
        StringAssert.Contains(result, "Method#colon; special");
        Assert.IsFalse(result.Contains("Method: special"), "Raw colon must be escaped");
    }

    [TestMethod]
    public void Serialize_LabelWithAngleBrackets_IsEscaped()
    {
        var sequence = new CallSequence
        {
            Participants = new[] { P("A", "A"), P("B", "B") },
            RootCalls = new[] { Leaf("A", "B", "Get<T>") },
        };

        var result = Serializer.Serialize(sequence);
        StringAssert.Contains(result, "Get#lt;T#gt;");
        Assert.IsFalse(result.Contains("Get<T>"), "Raw angle brackets must be escaped");
    }

    [TestMethod]
    public void Serialize_LabelWithAmpersand_IsEscaped()
    {
        var sequence = new CallSequence
        {
            Participants = new[] { P("A", "A"), P("B", "B") },
            RootCalls = new[] { Leaf("A", "B", "A&B") },
        };

        var result = Serializer.Serialize(sequence);
        StringAssert.Contains(result, "A#amp;B");
        Assert.IsFalse(result.Contains("A&B"), "Raw ampersand must be escaped");
    }

    [TestMethod]
    public void Serialize_ParticipantLabelWithColon_IsEscaped()
    {
        var sequence = new CallSequence
        {
            Participants = new[] { P("A", "Label:With:Colons") },
        };

        var result = Serializer.Serialize(sequence);
        StringAssert.Contains(result, "as Label#colon;With#colon;Colons");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // BuildMarkdown
    // ──────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void BuildMarkdown_ContainsTitleHeading()
    {
        var sequence = new CallSequence { Title = "MyClass.DoWork" };
        var result = Serializer.BuildMarkdown(sequence);
        StringAssert.Contains(result, "# Sequence: MyClass.DoWork");
    }

    [TestMethod]
    public void BuildMarkdown_ContainsMermaidFenceBlock()
    {
        var result = Serializer.BuildMarkdown(new CallSequence());
        StringAssert.Contains(result, "```mermaid");
        StringAssert.Contains(result, "sequenceDiagram");
        // At least two ``` markers (open + close)
        Assert.IsTrue(result.Split(new[] { "```" }, StringSplitOptions.None).Length >= 3);
    }

    [TestMethod]
    public void BuildMarkdown_MermaidContentInsideFence()
    {
        var sequence = new CallSequence
        {
            Title = "X",
            Participants = new[] { P("A", "A"), P("B", "B") },
            RootCalls = new[] { Leaf("A", "B", "Go") },
        };

        var result = Serializer.BuildMarkdown(sequence);
        int fenceOpen = result.IndexOf("```mermaid");
        int fenceClose = result.LastIndexOf("```");
        int callLine  = result.IndexOf("A->>B: Go");

        Assert.IsTrue(callLine > fenceOpen, "Call line must be inside the opening fence");
        Assert.IsTrue(callLine < fenceClose, "Call line must be before the closing fence");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // BuildHtml — structure
    // ──────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void BuildHtml_IsWellFormedHtmlDocument()
    {
        var result = Serializer.BuildHtml(new CallSequence { Title = "Test" });

        Assert.IsTrue(result.TrimStart().StartsWith("<!DOCTYPE html>"));
        StringAssert.Contains(result, "<html");
        StringAssert.Contains(result, "</html>");
        StringAssert.Contains(result, "<head>");
        StringAssert.Contains(result, "</head>");
        StringAssert.Contains(result, "<body>");
        StringAssert.Contains(result, "</body>");
    }

    [TestMethod]
    public void BuildHtml_ContainsMermaidCdnImport()
    {
        var result = Serializer.BuildHtml(new CallSequence { Title = "Test" });
        StringAssert.Contains(result, "cdn.jsdelivr.net/npm/mermaid@11");
    }

    [TestMethod]
    public void BuildHtml_ContainsDiagramSourceAsJsVariable()
    {
        var result = Serializer.BuildHtml(new CallSequence { Title = "Test" });
        StringAssert.Contains(result, "const DIAGRAM_SOURCE =");
    }

    [TestMethod]
    public void BuildHtml_ContainsZoomControls()
    {
        var result = Serializer.BuildHtml(new CallSequence { Title = "Test" });
        StringAssert.Contains(result, "zoom(");
        StringAssert.Contains(result, "fitWidth");
        StringAssert.Contains(result, "resetZoom");
    }

    [TestMethod]
    public void BuildHtml_ContainsDiagramContainer()
    {
        var result = Serializer.BuildHtml(new CallSequence { Title = "Test" });
        StringAssert.Contains(result, "diagram-container");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // BuildHtml — security / encoding
    // ──────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void BuildHtml_TitleWithHtmlSpecialChars_IsEscaped()
    {
        var sequence = new CallSequence { Title = "<script>alert('xss')</script>" };
        var result = Serializer.BuildHtml(sequence);

        Assert.IsFalse(result.Contains("<script>alert"), "Raw XSS script tag must not appear in HTML");
        StringAssert.Contains(result, "&lt;script&gt;");
    }

    [TestMethod]
    public void BuildHtml_TitleWithAmpersand_IsHtmlEscaped()
    {
        var sequence = new CallSequence { Title = "A & B" };
        var result = Serializer.BuildHtml(sequence);

        StringAssert.Contains(result, "A &amp; B");
    }

    [TestMethod]
    public void BuildHtml_ScriptBlockDoesNotContainRawScriptCloseTag()
    {
        // If mermaid source contained </script>, it would break HTML parsing.
        var result = Serializer.BuildHtml(new CallSequence { Title = "Test" });

        const string scriptOpen = "<script type=\"module\">";
        int start = result.IndexOf(scriptOpen) + scriptOpen.Length;
        int end   = result.LastIndexOf("</script>");
        var scriptContent = result.Substring(start, end - start);

        Assert.IsFalse(scriptContent.Contains("</script"), "Script block must not contain </script");
    }

    [TestMethod]
    public void BuildHtml_MermaidArrowsEncodedAsUnicodeEscapesInJsString()
    {
        // Mermaid arrows (->> -->>-) contain '>' which must be > in the JS string
        // to prevent the HTML parser from finding </script> inside the script tag.
        var sequence = new CallSequence
        {
            Participants = new[] { P("A", "A"), P("B", "B") },
            RootCalls = new[] { Leaf("A", "B", "M") },
        };

        var html = Serializer.BuildHtml(sequence);

        const string varPrefix = "const DIAGRAM_SOURCE = \"";
        int markerIdx = html.IndexOf(varPrefix);
        Assert.IsTrue(markerIdx >= 0, "DIAGRAM_SOURCE variable not found");

        int contentStart = markerIdx + varPrefix.Length;
        int contentEnd   = html.IndexOf("\";", contentStart);
        var jsStringContent = html.Substring(contentStart, contentEnd - contentStart);

        Assert.IsTrue(jsStringContent.Contains("\\u003e"), "Arrow '>' must be unicode-escaped to \\u003e");
        Assert.IsFalse(jsStringContent.Contains('>'), "Raw '>' must not appear in the JS string literal");
    }

    [TestMethod]
    public void BuildHtml_AngleBracketsInDiagramSource_NeverRaw()
    {
        // Even when the mermaid source has angle-bracket escapes (#lt; #gt;),
        // no raw '<' or '>' should appear inside the JS string in the HTML.
        var sequence = new CallSequence
        {
            Participants = new[] { P("A", "A"), P("B", "B") },
            RootCalls = new[] { Leaf("A", "B", "Get<T>") },  // label will become Get#lt;T#gt;
        };

        var html = Serializer.BuildHtml(sequence);

        const string varPrefix = "const DIAGRAM_SOURCE = \"";
        int contentStart = html.IndexOf(varPrefix) + varPrefix.Length;
        int contentEnd   = html.IndexOf("\";", contentStart);
        var jsString = html.Substring(contentStart, contentEnd - contentStart);

        Assert.IsFalse(jsString.Contains('<'), "No raw '<' in JS string literal");
        Assert.IsFalse(jsString.Contains('>'), "No raw '>' in JS string literal");
    }

    [TestMethod]
    public void BuildHtml_BackslashInLabel_IsDoubleEscapedInJsString()
    {
        // A backslash in the mermaid label must become \\ in the JS string.
        var sequence = new CallSequence
        {
            Participants = new[] { P("A", "A"), P("B", "B") },
            RootCalls = new[] { Leaf("A", "B", @"Path\File") },
        };

        var html = Serializer.BuildHtml(sequence);

        const string varPrefix = "const DIAGRAM_SOURCE = \"";
        int contentStart = html.IndexOf(varPrefix) + varPrefix.Length;
        int contentEnd   = html.IndexOf("\";", contentStart);
        var jsString = html.Substring(contentStart, contentEnd - contentStart);

        // In HTML the JS string literal: "...Path\\File..." — C# sees it as "...Path\\File..."
        StringAssert.Contains(jsString, @"\\");
    }

    [TestMethod]
    public void BuildHtml_NewlinesInMermaidSource_EncodedAsLiteralEscape()
    {
        // Need a multi-line mermaid source (participants + calls) to have newlines to encode.
        var sequence = new CallSequence
        {
            Title = "Test",
            Participants = new[] { P("A", "A"), P("B", "B") },
            RootCalls = new[] { Leaf("A", "B", "Go") },
        };
        var result = Serializer.BuildHtml(sequence);

        const string varPrefix = "const DIAGRAM_SOURCE = \"";
        int contentStart = result.IndexOf(varPrefix) + varPrefix.Length;
        int contentEnd   = result.IndexOf("\";", contentStart);
        var jsString = result.Substring(contentStart, contentEnd - contentStart);

        // Newlines must be encoded as \n, not embedded literally
        Assert.IsFalse(jsString.Contains('\n'), "Literal newlines in JS string literal are invalid");
        StringAssert.Contains(jsString, "\\n", "Newlines must be encoded as \\n");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // CallSequence defaults
    // ──────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void CallSequence_DefaultParticipants_IsEmpty()
    {
        var seq = new CallSequence();
        Assert.AreEqual(0, seq.Participants.Count);
    }

    [TestMethod]
    public void CallSequence_DefaultRootCalls_IsEmpty()
    {
        var seq = new CallSequence();
        Assert.AreEqual(0, seq.RootCalls.Count);
    }

    [TestMethod]
    public void CallSequenceParticipant_EqualityByValue()
    {
        var a = new CallSequenceParticipant("ID", "Label");
        var b = new CallSequenceParticipant("ID", "Label");
        Assert.AreEqual(a, b);
    }

    [TestMethod]
    public void CallSequenceCallNode_NestedCallsIsReadOnly()
    {
        var node = Leaf("A", "B", "M");
        Assert.IsNotNull(node.NestedCalls);
        Assert.AreEqual(0, node.NestedCalls.Count);
    }
}
