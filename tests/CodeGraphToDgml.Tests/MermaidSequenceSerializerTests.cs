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
        => new(from, to, label, []);

    private static CallSequenceCallNode Nested(string from, string to, string label, params CallSequenceCallNode[] children)
        => new(from, to, label, children);

    private static CallSequenceParticipant P(string id, string label) => new(id, label);

    /// <summary>A→B→C linear chain with participant declarations.</summary>
    private static CallSequence LinearChain() => new()
    {
        Title = "ClassA.Root",
        Participants = [P("ClassA", "ClassA"), P("ClassB", "ClassB"), P("ClassC", "ClassC")],
        RootCalls =
        [
            Nested("ClassA", "ClassB", "DoB",
                Leaf("ClassB", "ClassC", "DoC")),
        ],
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
        Assert.StartsWith("sequenceDiagram", result.TrimStart(), result);
    }

    [TestMethod]
    public void Serialize_ParticipantsOnly_ProducesDeclarations()
    {
        var sequence = new CallSequence
        {
            Participants = [P("SvcA", "ServiceA"), P("SvcB", "ServiceB")],
        };

        var result = Serializer.Serialize(sequence);
        Assert.Contains("participant SvcA as ServiceA", result);
        Assert.Contains("participant SvcB as ServiceB", result);
    }

    [TestMethod]
    public void Serialize_ParticipantsAppearsInDeclarationOrder()
    {
        var sequence = new CallSequence
        {
            Participants = [P("First", "First"), P("Second", "Second"), P("Third", "Third")],
        };

        var result = Serializer.Serialize(sequence);
        Assert.IsLessThan(result.IndexOf("participant Second"), result.IndexOf("participant First"));
        Assert.IsLessThan(result.IndexOf("participant Third"), result.IndexOf("participant Second"));
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Serialize — arrow types
    // ──────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void Serialize_LeafCall_StackedBarsOff_UsesSimpleArrow()
    {
        var sequence = new CallSequence
        {
            Participants = [P("A", "A"), P("B", "B")],
            RootCalls = [Leaf("A", "B", "Foo")],
        };

        var result = Serializer.Serialize(sequence, stackedActivationBars: false);
        Assert.Contains("A->>B: Foo", result);
        Assert.DoesNotContain("->>+", result, "Leaf call must not activate when stacked bars are off");
        Assert.DoesNotContain("-->>-", result, "Leaf call must not deactivate when stacked bars are off");
    }

    [TestMethod]
    public void Serialize_LeafCall_StackedBarsOn_UsesActivationArrows()
    {
        var sequence = new CallSequence
        {
            Participants = [P("A", "A"), P("B", "B")],
            RootCalls = [Leaf("A", "B", "Foo")],
        };

        var result = Serializer.Serialize(sequence, stackedActivationBars: true);
        Assert.Contains("A->>+B: Foo", result);
        Assert.Contains("B-->>-A: ", result);
    }

    [TestMethod]
    public void Serialize_NestedCall_UsesActivationArrows()
    {
        var result = Serializer.Serialize(LinearChain());

        Assert.Contains("ClassA->>+ClassB: DoB", result);
        Assert.Contains("ClassB->>+ClassC: DoC", result);
        Assert.Contains("ClassC-->>-ClassB: ", result);
        Assert.Contains("ClassB-->>-ClassA: ", result);
    }

    [TestMethod]
    public void Serialize_NestedCall_StackedBarsOff_UsesPlainArrows()
    {
        var result = Serializer.Serialize(LinearChain(), stackedActivationBars: false);

        Assert.Contains("ClassA->>ClassB: DoB", result);
        Assert.Contains("ClassB->>ClassC: DoC", result);
        Assert.DoesNotContain("->>+", result, "No activation markers when stacked bars off");
        Assert.DoesNotContain("-->>-", result, "No deactivation markers when stacked bars off");
    }

    [TestMethod]
    public void Serialize_SelfCall_SameParticipantBothEnds()
    {
        var sequence = new CallSequence
        {
            Participants = [P("MyClass", "MyClass")],
            RootCalls = [Leaf("MyClass", "MyClass", "Helper")],
        };

        var result = Serializer.Serialize(sequence);
        Assert.Contains("MyClass->>+MyClass: Helper", result);
        Assert.Contains("MyClass-->>-MyClass: ", result);
    }

    [TestMethod]
    public void Serialize_NestedSelfCall_StackedBarsOff_NoActivationMarkers()
    {
        var sequence = new CallSequence
        {
            Participants = [P("A", "A"), P("B", "B")],
            RootCalls =
            [
                Nested("A", "A", "SelfWithNested",
                    Leaf("A", "B", "Inner")),
            ],
        };

        var result = Serializer.Serialize(sequence, stackedActivationBars: false);
        Assert.Contains("A->>A: SelfWithNested", result);
        Assert.Contains("A->>B: Inner", result);
        Assert.DoesNotContain("->>+", result, "No activation markers when stacked bars off");
        Assert.DoesNotContain("-->>-", result, "No deactivation markers when stacked bars off");
    }

    [TestMethod]
    public void Serialize_NestedSelfCall_StackedBarsOn_UsesActivationMarkers()
    {
        var sequence = new CallSequence
        {
            Participants = [P("A", "A"), P("B", "B")],
            RootCalls =
            [
                Nested("A", "A", "SelfWithNested",
                    Leaf("A", "B", "Inner")),
            ],
        };

        var result = Serializer.Serialize(sequence, stackedActivationBars: true);
        Assert.Contains("A->>+A: SelfWithNested", result);
        Assert.Contains("A->>+B: Inner", result);
        Assert.Contains("B-->>-A: ", result);
        Assert.Contains("A-->>-A: ", result);
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
        int callC = Array.FindIndex(lines, l => l.StartsWith("ClassB->>+ClassC"));
        int retB  = Array.FindIndex(lines, l => l.StartsWith("ClassC-->>-ClassB"));
        int retA  = Array.FindIndex(lines, l => l.StartsWith("ClassB-->>-ClassA"));

        Assert.IsLessThan(callC, callB, "Call to B should precede call to C");
        Assert.IsLessThan(retB, callC,  "Call to C should precede deactivation of C→B");
        Assert.IsLessThan(retA, retB,   "Deactivation of C→B should precede deactivation of B→A");
    }

    [TestMethod]
    public void Serialize_SiblingCalls_BothArrowsPresent()
    {
        var sequence = new CallSequence
        {
            Participants = [P("A", "A"), P("B", "B"), P("C", "C")],
            RootCalls =
            [
                Leaf("A", "B", "Foo"),
                Leaf("A", "C", "Bar"),
            ],
        };

        var result = Serializer.Serialize(sequence, stackedActivationBars: false);
        Assert.Contains("A->>B: Foo", result);
        Assert.Contains("A->>C: Bar", result);
    }

    [TestMethod]
    public void Serialize_SiblingCallOrder_IsPreserved()
    {
        var sequence = new CallSequence
        {
            Participants = [P("A", "A"), P("B", "B"), P("C", "C")],
            RootCalls =
            [
                Leaf("A", "B", "First"),
                Leaf("A", "C", "Second"),
            ],
        };

        var result = Serializer.Serialize(sequence);
        Assert.IsLessThan(result.IndexOf("Second"), result.IndexOf("First"));
    }

    [TestMethod]
    public void Serialize_DeepNesting_ProducesCorrectActivationStack()
    {
        // A→B→C→D (all nested)
        var sequence = new CallSequence
        {
            Title = "A.Entry",
            Participants = [P("A", "A"), P("B", "B"), P("C", "C"), P("D", "D")],
            RootCalls =
            [
                Nested("A", "B", "Top",
                    Nested("B", "C", "Mid",
                        Leaf("C", "D", "Deep"))),
            ],
        };

        var result = Serializer.Serialize(sequence);
        var lines = result.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToArray();

        int callB = Array.FindIndex(lines, l => l.StartsWith("A->>+B"));
        int callC = Array.FindIndex(lines, l => l.StartsWith("B->>+C"));
        int callD = Array.FindIndex(lines, l => l.StartsWith("C->>+D"));
        int retC  = Array.FindIndex(lines, l => l.StartsWith("D-->>-C"));
        int retB  = Array.FindIndex(lines, l => l.StartsWith("C-->>-B"));
        int retA  = Array.FindIndex(lines, l => l.StartsWith("B-->>-A"));

        Assert.IsGreaterThanOrEqualTo(0, callB, "Missing A->>+B");
        Assert.IsGreaterThanOrEqualTo(0, callC, "Missing B->>+C");
        Assert.IsGreaterThanOrEqualTo(0, callD, "Missing C->>+D");
        Assert.IsGreaterThanOrEqualTo(0, retC, "Missing D-->>-C");
        Assert.IsGreaterThanOrEqualTo(0, retB, "Missing C-->>-B");
        Assert.IsGreaterThanOrEqualTo(0, retA, "Missing B-->>-A");

        Assert.IsLessThan(callC, callB, "A→B must precede B→C");
        Assert.IsLessThan(callD, callC, "B→C must precede C→D");
        Assert.IsLessThan(retC, callD,  "C→D must precede deactivation of D");
        Assert.IsLessThan(retB, retC,   "D deactivation must precede C deactivation");
        Assert.IsLessThan(retA, retB,   "C deactivation must precede B deactivation");
    }

    [TestMethod]
    public void Serialize_MultipleRootCallsWithNesting_OrderPreserved()
    {
        // A calls B (nested: B calls C), then A calls D
        var sequence = new CallSequence
        {
            Participants = [P("A", "A"), P("B", "B"), P("C", "C"), P("D", "D")],
            RootCalls =
            [
                Nested("A", "B", "WithNested", Leaf("B", "C", "Inner")),
                Leaf("A", "D", "After"),
            ],
        };

        var result = Serializer.Serialize(sequence);
        var lines = result.Split('\n').Select(l => l.Trim()).ToArray();

        int callB   = Array.FindIndex(lines, l => l.StartsWith("A->>+B"));
        int inner   = Array.FindIndex(lines, l => l.Contains("Inner"));
        int retA    = Array.FindIndex(lines, l => l.StartsWith("B-->>-A"));
        int callD   = Array.FindIndex(lines, l => l.Contains("After"));

        Assert.IsLessThan(inner, callB, "Call B must precede inner call");
        Assert.IsLessThan(retA, inner, "Inner call must precede B deactivation");
        Assert.IsLessThan(callD, retA, "B deactivation must precede call to D");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Serialize — label escaping
    // ──────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void Serialize_LabelWithColon_IsEscaped()
    {
        var sequence = new CallSequence
        {
            Participants = [P("A", "A"), P("B", "B")],
            RootCalls = [Leaf("A", "B", "Method: special")],
        };

        var result = Serializer.Serialize(sequence);
        Assert.Contains("Method#colon; special", result);
        Assert.DoesNotContain("Method: special", result, "Raw colon must be escaped");
    }

    [TestMethod]
    public void Serialize_LabelWithAngleBrackets_IsEscaped()
    {
        var sequence = new CallSequence
        {
            Participants = [P("A", "A"), P("B", "B")],
            RootCalls = [Leaf("A", "B", "Get<T>")],
        };

        var result = Serializer.Serialize(sequence);
        Assert.Contains("Get#lt;T#gt;", result);
        Assert.DoesNotContain("Get<T>", result, "Raw angle brackets must be escaped");
    }

    [TestMethod]
    public void Serialize_LabelWithAmpersand_IsEscaped()
    {
        var sequence = new CallSequence
        {
            Participants = [P("A", "A"), P("B", "B")],
            RootCalls = [Leaf("A", "B", "A&B")],
        };

        var result = Serializer.Serialize(sequence);
        Assert.Contains("A#amp;B", result);
        Assert.DoesNotContain("A&B", result, "Raw ampersand must be escaped");
    }

    [TestMethod]
    public void Serialize_ParticipantLabelWithColon_IsEscaped()
    {
        var sequence = new CallSequence
        {
            Participants = [P("A", "Label:With:Colons")],
        };

        var result = Serializer.Serialize(sequence);
        Assert.Contains("as Label#colon;With#colon;Colons", result);
    }

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

    // ──────────────────────────────────────────────────────────────────────────
    // Serialize — autonumber
    // ──────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void Serialize_AutoNumberOff_NoDirective()
    {
        var result = Serializer.Serialize(LinearChain(), autoNumber: false);
        Assert.DoesNotContain("autonumber", result, "autonumber directive must not appear when disabled");
    }

    [TestMethod]
    public void Serialize_AutoNumberOn_InsertsDirectiveAfterHeader()
    {
        var result = Serializer.Serialize(LinearChain(), autoNumber: true);
        Assert.Contains("autonumber", result);
        var lines = result.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToArray();
        int header    = Array.FindIndex(lines, l => l == "sequenceDiagram");
        int directive = Array.FindIndex(lines, l => l == "autonumber");
        Assert.IsGreaterThanOrEqualTo(0, header, "Missing sequenceDiagram header");
        Assert.IsGreaterThanOrEqualTo(0, directive, "Missing autonumber directive");
        Assert.IsLessThan(directive, header, "autonumber must follow sequenceDiagram");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // CallSequence defaults
    // ──────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void CallSequence_DefaultParticipants_IsEmpty()
    {
        var seq = new CallSequence();
        Assert.IsEmpty(seq.Participants);
    }

    [TestMethod]
    public void CallSequence_DefaultRootCalls_IsEmpty()
    {
        var seq = new CallSequence();
        Assert.IsEmpty(seq.RootCalls);
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
        Assert.IsEmpty(node.NestedCalls);
    }
}
