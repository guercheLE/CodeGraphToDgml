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

    // ──────────────────────────────────────────────────────────────────────────
    // Multi-section: global numbering
    // ──────────────────────────────────────────────────────────────────────────

    // Builds a sequence with 4 distinct participants per root call so the total
    // forces a split when maxParticipantsPerDiagram=3.
    private static CallSequence TwoPhaseSequence() => new()
    {
        Title = "Root",
        Participants =
        [
            P("A","A"), P("B","B"), P("C","C"),
            P("D","D"), P("E","E"), P("F","F"),
        ],
        RootCalls =
        [
            // Phase 1: participants A, B, C
            Nested("A", "B", "Phase1Call", Leaf("B", "C", "Inner1")),
            // Phase 2: participants D, E, F
            Nested("D", "E", "Phase2Call", Leaf("E", "F", "Inner2")),
        ],
    };

    [TestMethod]
    public void BuildMarkdown_MultiSection_SecondSectionHasHigherAutonumber()
    {
        // maxParticipantsPerDiagram=3 forces a split between the two phases
        var result = Serializer.BuildMarkdown(TwoPhaseSequence(),
            stackedActivationBars: true, autoNumber: true, maxParticipantsPerDiagram: 3);

        // Both sections must contain autonumber
        var lines = result.Split('\n').Select(l => l.Trim()).ToArray();
        var autonumberLines = lines.Where(l => l.StartsWith("autonumber")).ToArray();

        Assert.IsGreaterThanOrEqualTo(2, autonumberLines.Length, "Expected at least 2 autonumber directives");

        // First autonumber starts at 1 (or just "autonumber" without a number)
        var first = autonumberLines[0];
        Assert.IsTrue(first == "autonumber" || first == "autonumber 1",
            "First autonumber should start at 1, got: " + first);

        // Second autonumber must be > 1 (global continuation)
        var second = autonumberLines[1];
        Assert.IsTrue(second.StartsWith("autonumber ") && second != "autonumber 1",
            "Second section autonumber must continue from where first left off, got: " + second);
    }

    [TestMethod]
    public void BuildMarkdown_MultiSection_SecondSectionStartNumberEqualsFirstSectionArrowCount()
    {
        // With stackedBars=true: each call contributes 1 call + 1 return = 2 arrows.
        // Phase 1: A→B (call+return) + B→C (call+return) = 4 arrows.
        // Phase 2 should start at autonumber 5.
        var result = Serializer.BuildMarkdown(TwoPhaseSequence(),
            stackedActivationBars: true, autoNumber: true, maxParticipantsPerDiagram: 3);

        var lines = result.Split('\n').Select(l => l.Trim()).ToArray();
        var autonumberLines = lines.Where(l => l.StartsWith("autonumber")).ToArray();

        Assert.IsGreaterThanOrEqualTo(2, autonumberLines.Length);
        Assert.AreEqual("autonumber 5", autonumberLines[1],
            "Phase 2 should start at message 5 (4 arrows in phase 1 + 1)");
    }

    [TestMethod]
    public void BuildMarkdown_MultiSection_SingleDiagramWhenBelowLimit()
    {
        // With maxParticipantsPerDiagram=10, all 6 participants fit; no split expected.
        var result = Serializer.BuildMarkdown(TwoPhaseSequence(),
            stackedActivationBars: true, autoNumber: true, maxParticipantsPerDiagram: 10);

        var autonumberLines = result.Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.StartsWith("autonumber"))
            .ToArray();

        Assert.HasCount(1, autonumberLines, "Should be a single diagram when all participants fit");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Multi-section: business titles
    // ──────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void BuildMarkdown_MultiSection_HeadingsContainCallLabels()
    {
        var result = Serializer.BuildMarkdown(TwoPhaseSequence(),
            stackedActivationBars: true, autoNumber: false, maxParticipantsPerDiagram: 3);

        // Section headings should reference actual method names, not "Levels N-M"
        Assert.Contains("Phase1Call", result, "First section heading should mention Phase1Call");
        Assert.Contains("Phase2Call", result, "Second section heading should mention Phase2Call");
        Assert.DoesNotContain("Levels", result, "Headings must not use depth-level notation");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Multi-section: HTML continuation banners
    // ──────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void BuildHtml_MultiSection_ContainsContinuationBanners()
    {
        var result = Serializer.BuildHtml(TwoPhaseSequence(),
            stackedActivationBars: true, autoNumber: true, maxParticipantsPerDiagram: 3);

        Assert.Contains("continuation-top", result, "Should have top continuation banner");
        Assert.Contains("continuation-bottom", result, "Should have bottom continuation banner");
    }

    [TestMethod]
    public void BuildHtml_MultiSection_FirstSectionHasStartOfSequence()
    {
        var result = Serializer.BuildHtml(TwoPhaseSequence(),
            stackedActivationBars: true, autoNumber: false, maxParticipantsPerDiagram: 3);

        Assert.Contains("Start of sequence", result, "First section should say 'Start of sequence'");
    }

    [TestMethod]
    public void BuildHtml_MultiSection_LastSectionHasEndOfSequence()
    {
        var result = Serializer.BuildHtml(TwoPhaseSequence(),
            stackedActivationBars: true, autoNumber: false, maxParticipantsPerDiagram: 3);

        Assert.Contains("End of sequence", result, "Last section should say 'End of sequence'");
    }

    [TestMethod]
    public void BuildHtml_MultiSection_BannersMentionContinuesFrom()
    {
        var result = Serializer.BuildHtml(TwoPhaseSequence(),
            stackedActivationBars: true, autoNumber: false, maxParticipantsPerDiagram: 3);

        Assert.Contains("Continues from", result, "Second section banner should say 'Continues from'");
        Assert.Contains("Continues in", result, "First section banner should say 'Continues in'");
    }

    [TestMethod]
    public void BuildHtml_MultiSection_MessageRangeInSectionHeader()
    {
        var result = Serializer.BuildHtml(TwoPhaseSequence(),
            stackedActivationBars: true, autoNumber: true, maxParticipantsPerDiagram: 3);

        Assert.Contains("Messages 1", result, "Section header should show message range starting at 1");
        Assert.Contains("Messages 5", result, "Second section header should show range starting at 5");
    }

    [TestMethod]
    public void BuildHtml_MultiSection_ParticipantCountInSectionHeader()
    {
        var result = Serializer.BuildHtml(TwoPhaseSequence(),
            stackedActivationBars: true, autoNumber: false, maxParticipantsPerDiagram: 3);

        Assert.Contains("participants", result, "Section header should show participant count");
    }

    [TestMethod]
    public void BuildHtml_MultiSection_TocContainsPartLinks()
    {
        var result = Serializer.BuildHtml(TwoPhaseSequence(),
            stackedActivationBars: false, autoNumber: false, maxParticipantsPerDiagram: 3);

        Assert.Contains("Part 1", result, "TOC should contain Part 1 link");
        Assert.Contains("Part 2", result, "TOC should contain Part 2 link");
    }

    [TestMethod]
    public void BuildHtml_MultiSection_SectionsHavePartIds()
    {
        var result = Serializer.BuildHtml(TwoPhaseSequence(),
            stackedActivationBars: false, autoNumber: false, maxParticipantsPerDiagram: 3);

        Assert.Contains("id=\"part-1\"", result, "First section should have id='part-1'");
        Assert.Contains("id=\"part-2\"", result, "Second section should have id='part-2'");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Sub-part segmentation
    // ──────────────────────────────────────────────────────────────────────────

    private static CallSequence OversizedSingleCallSequence()
    {
        // One root call A→B with many nested calls each introducing unique participants
        // B→C, B→D, B→E, B→F, B→G — forcing sub-part split at maxParticipants=4
        return new CallSequence
        {
            Title = "BigCall",
            Participants =
            [
                P("A","A"), P("B","B"), P("C","C"), P("D","D"),
                P("E","E"), P("F","F"), P("G","G"),
            ],
            RootCalls =
            [
                new CallSequenceCallNode("A", "B", "BigEntry",
                [
                    Leaf("B", "C", "Sub1"),
                    Leaf("B", "D", "Sub2"),
                    Leaf("B", "E", "Sub3"),
                    Leaf("B", "F", "Sub4"),
                    Leaf("B", "G", "Sub5"),
                ]),
            ],
        };
    }

    [TestMethod]
    public void BuildMarkdown_OversizedSingleCall_ProducesMultipleSections()
    {
        var result = Serializer.BuildMarkdown(OversizedSingleCallSequence(),
            stackedActivationBars: false, autoNumber: false, maxParticipantsPerDiagram: 4);

        // Should produce at least 2 sections (sub-parts)
        var headingCount = result.Split('\n').Count(l => l.TrimStart().StartsWith("## Part"));
        Assert.IsGreaterThan(1, headingCount, "Oversized single call should be split into sub-parts");
    }

    [TestMethod]
    public void BuildMarkdown_OversizedSingleCall_BoundaryParticipantsRepeatAcrossSubParts()
    {
        var result = Serializer.BuildMarkdown(OversizedSingleCallSequence(),
            stackedActivationBars: false, autoNumber: false, maxParticipantsPerDiagram: 4);

        // B (the callee of the parent call) should appear in all sub-segments
        int bDeclarationCount = result.Split('\n').Count(l => l.Trim().StartsWith("participant B"));
        Assert.IsGreaterThan(1, bDeclarationCount, "Boundary participant B should appear in multiple sub-segments");
    }

    [TestMethod]
    public void BuildMarkdown_OversizedSingleCall_SubPartNumbersUsedInHeadings()
    {
        var result = Serializer.BuildMarkdown(OversizedSingleCallSequence(),
            stackedActivationBars: false, autoNumber: false, maxParticipantsPerDiagram: 4);

        // Sub-parts should use A/B/C notation like "Part 1A", "Part 1B"
        Assert.Contains("Part 1A", result, "First sub-part should be numbered 1A");
        Assert.Contains("Part 1B", result, "Second sub-part should be numbered 1B");
    }

    [TestMethod]
    public void BuildMarkdown_OversizedSingleCall_GlobalNumberingContinuesAcrossSubParts()
    {
        var result = Serializer.BuildMarkdown(OversizedSingleCallSequence(),
            stackedActivationBars: false, autoNumber: true, maxParticipantsPerDiagram: 4);

        var lines = result.Split('\n').Select(l => l.Trim()).ToArray();
        var autonumberLines = lines.Where(l => l.StartsWith("autonumber")).ToArray();

        Assert.IsGreaterThanOrEqualTo(2, autonumberLines.Length);

        // Parse the second autonumber value — must be > 1
        var secondParts = autonumberLines[1].Split(' ');
        if (secondParts.Length >= 2 && int.TryParse(secondParts[1], out int secondStart))
        {
            Assert.IsGreaterThan(1, secondStart, "Sub-part B must not restart numbering at 1");
        }
    }

    [TestMethod]
    public void BuildMarkdown_OversizedSingleCall_StackedBarsOn_BoundaryParticipantsHaveActivationBars()
    {
        // With stacked bars, each sub-part must emit activate/deactivate for the
        // caller (A) and callee (B) of the oversized parent call so their lifelines
        // show active execution bars across all sub-segments.
        var result = Serializer.BuildMarkdown(OversizedSingleCallSequence(),
            stackedActivationBars: true, autoNumber: false, maxParticipantsPerDiagram: 4);

        var lines = result.Split('\n').Select(l => l.Trim()).ToArray();

        // Every sub-part should contain both activate A and activate B
        Assert.Contains("activate A", result, "Caller A must be activated in sub-parts");
        Assert.Contains("activate B", result, "Callee B must be activated in sub-parts");
        Assert.Contains("deactivate A", result, "Caller A must be deactivated in sub-parts");
        Assert.Contains("deactivate B", result, "Callee B must be deactivated in sub-parts");
    }

    [TestMethod]
    public void BuildMarkdown_OversizedSingleCall_StackedBarsOff_NoBoundaryActivationLines()
    {
        // When stacked bars are off, no activate/deactivate lines should be emitted
        // for the split boundary (or at all).
        var result = Serializer.BuildMarkdown(OversizedSingleCallSequence(),
            stackedActivationBars: false, autoNumber: false, maxParticipantsPerDiagram: 4);

        Assert.DoesNotContain("activate ", result, "No activate lines when stacked bars are off");
        Assert.DoesNotContain("deactivate ", result, "No deactivate lines when stacked bars are off");
    }

    [TestMethod]
    public void BuildMarkdown_OversizedSingleCall_BoundaryActivateBeforeFirstCall()
    {
        // The activate lines for the split boundary must appear before the first call
        // arrow in each sub-part so they form the outer activation frame.
        var result = Serializer.BuildMarkdown(OversizedSingleCallSequence(),
            stackedActivationBars: true, autoNumber: false, maxParticipantsPerDiagram: 4);

        int activateIdx = result.IndexOf("activate A", StringComparison.Ordinal);
        int firstCallIdx = result.IndexOf("->>+", StringComparison.Ordinal);

        Assert.IsGreaterThanOrEqualTo(0, activateIdx, "Missing 'activate A'");
        Assert.IsGreaterThanOrEqualTo(0, firstCallIdx, "Missing first call arrow");
        Assert.IsLessThan(firstCallIdx, activateIdx, "'activate A' must precede the first call arrow");
    }
}
