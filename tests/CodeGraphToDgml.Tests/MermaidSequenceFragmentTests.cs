using System.Collections.Generic;
using System.Linq;
using CodeGraphToDgml.Core;

namespace CodeGraphToDgml.Tests;

/// <summary>
/// Control-flow fragments (alt/opt/loop/break/par/critical) in <see cref="MermaidSequenceSerializer"/>:
/// emission, nesting, section switching, and how splitting re-opens fragments across parts.
/// </summary>
[TestClass]
public sealed class MermaidSequenceFragmentTests
{
    private static CallSequenceCallNode Leaf(string from, string to, string label, params CallSequenceFragmentScope[] path)
        => new(from, to, label, []) { FragmentPath = path };

    private static CallSequenceCallNode Nested(string from, string to, string label, CallSequenceFragmentScope[] path, params CallSequenceCallNode[] children)
        => new(from, to, label, children) { FragmentPath = path };

    private static CallSequenceFragmentScope Alt(int id, int section, string label) => new(id, SequenceFragmentKind.Alt, section, label);
    private static CallSequenceFragmentScope Opt(int id, string label) => new(id, SequenceFragmentKind.Opt, 0, label);
    private static CallSequenceFragmentScope Loop(int id, string label) => new(id, SequenceFragmentKind.Loop, 0, label);

    private static CallSequence Seq(params CallSequenceCallNode[] roots)
        => new()
        {
            Title = "T",
            Participants = [new("A", "A"), new("B", "B"), new("C", "C")],
            RootCalls = roots,
        };

    private static string Body(string diagram)
    {
        // Drop header and participant declarations; keep the message/fragment lines.
        var lines = diagram.Split('\n').Select(l => l.TrimEnd('\r'))
            .Where(l => l.StartsWith("    ") && !l.TrimStart().StartsWith("participant ") && !l.TrimStart().StartsWith("actor "))
            .Select(l => l.Trim());
        return string.Join("\n", lines);
    }

    private static string Render(params CallSequenceCallNode[] roots)
        => Body(new MermaidSequenceSerializer().Serialize(Seq(roots), stackedActivationBars: false));

    // ── Emission ──────────────────────────────────────────────────────────────

    [TestMethod]
    public void Opt_WrapsSingleCall()
    {
        var text = Render(Leaf("A", "B", "m1", Opt(1, "x > 0")));

        Assert.AreEqual("opt x #gt; 0\nA->>B: m1\nend", text);
    }

    [TestMethod]
    public void Alt_TwoSections_EmitsElseBetweenThem()
    {
        var text = Render(
            Leaf("A", "B", "m1", Alt(1, 0, "x")),
            Leaf("A", "C", "m2", Alt(1, 1, "else")));

        Assert.AreEqual("alt x\nA->>B: m1\nelse else\nA->>C: m2\nend", text);
    }

    [TestMethod]
    public void Alt_SkippedMiddleSection_JumpsToTheNextLabel()
    {
        var text = Render(
            Leaf("A", "B", "m1", Alt(1, 0, "a")),
            Leaf("A", "C", "m2", Alt(1, 2, "c")));

        Assert.AreEqual("alt a\nA->>B: m1\nelse c\nA->>C: m2\nend", text);
    }

    [TestMethod]
    public void Loop_WrapsConsecutiveSiblingsInOneFragment()
    {
        var text = Render(
            Leaf("A", "B", "m1", Loop(1, "foreach x in xs")),
            Leaf("A", "C", "m2", Loop(1, "foreach x in xs")));

        Assert.AreEqual("loop foreach x in xs\nA->>B: m1\nA->>C: m2\nend", text);
    }

    [TestMethod]
    public void ConsecutiveDistinctInstances_SameLabel_AreClosedAndReopened()
    {
        var text = Render(
            Leaf("A", "B", "m1", Opt(1, "x")),
            Leaf("A", "C", "m2", Opt(2, "x")));

        Assert.AreEqual("opt x\nA->>B: m1\nend\nopt x\nA->>C: m2\nend", text);
    }

    [TestMethod]
    public void NestedFragments_CloseInnermostFirst()
    {
        var text = Render(
            Leaf("A", "B", "m1", Loop(1, "for"), Alt(2, 0, "y")),
            Leaf("A", "C", "m2", Loop(1, "for")));

        Assert.AreEqual("loop for\nalt y\nA->>B: m1\nend\nA->>C: m2\nend", text);
    }

    [TestMethod]
    public void FragmentThenPlainCall_ClosesBeforeTheCall()
    {
        var text = Render(
            Leaf("A", "B", "m1", Opt(1, "x")),
            Leaf("A", "C", "m2"));

        Assert.AreEqual("opt x\nA->>B: m1\nend\nA->>C: m2", text);
    }

    [TestMethod]
    public void FragmentInsideCalleeBody_SitsBetweenCallAndReturn()
    {
        var call = Nested("A", "B", "m1", [], Leaf("B", "C", "m2", Opt(1, "x")));

        var plain = Render(call);
        Assert.AreEqual("A->>B: m1\nopt x\nB->>C: m2\nend", plain);

        var stacked = Body(new MermaidSequenceSerializer().Serialize(Seq(call), stackedActivationBars: true));
        Assert.AreEqual("A->>+B: m1\nopt x\nB->>+C: m2\nC-->>-B: m2\nend\nB-->>-A: m1", stacked);
    }

    [TestMethod]
    public void BreakParAndCritical_UseTheirKeywordsAndSeparators()
    {
        var breakText = Render(Leaf("A", "B", "m1", new CallSequenceFragmentScope(1, SequenceFragmentKind.Break, 0, "catch Exception")));
        Assert.AreEqual("break catch Exception\nA->>B: m1\nend", breakText);

        var parText = Render(
            Leaf("A", "B", "m1", new CallSequenceFragmentScope(1, SequenceFragmentKind.Par, 0, "first")),
            Leaf("A", "C", "m2", new CallSequenceFragmentScope(1, SequenceFragmentKind.Par, 1, "second")));
        Assert.AreEqual("par first\nA->>B: m1\nand second\nA->>C: m2\nend", parText);

        var criticalText = Render(
            Leaf("A", "B", "m1", new CallSequenceFragmentScope(1, SequenceFragmentKind.Critical, 0, "main")),
            Leaf("A", "C", "m2", new CallSequenceFragmentScope(1, SequenceFragmentKind.Critical, 1, "fallback")));
        Assert.AreEqual("critical main\nA->>B: m1\noption fallback\nA->>C: m2\nend", criticalText);
    }

    [TestMethod]
    public void Label_IsEscapedForKeywordLines()
    {
        var text = Render(Leaf("A", "B", "m1", Opt(1, "a<b; c>d: 50% #1")));

        Assert.AreEqual("opt a#lt;b#semi; c#gt;d#colon; 50#37; #35;1\nA->>B: m1\nend", text);
    }

    [TestMethod]
    public void NoFragmentPaths_OutputIsIdenticalToPlainSerialization()
    {
        var seq = Seq(Nested("A", "B", "m1", [], Leaf("B", "C", "m2")), Leaf("A", "C", "m3"));
        var text = new MermaidSequenceSerializer().Serialize(seq, stackedActivationBars: true, autoNumber: true);

        Assert.IsFalse(text.Contains("alt ") || text.Contains("opt ") || text.Contains("loop ") || text.Contains("\n    end"), text);
        AssertFragmentsBalanced(text);
        AssertActivationsBalanced(text);
    }

    [TestMethod]
    public void AutoNumber_FragmentLinesDoNotCountAsMessages()
    {
        var seq = new CallSequence
        {
            Title = "T",
            Participants = [new("A", "A"), new("B", "B"), new("C", "C")],
            RootParticipantId = "A",
            RootMethodLabel = "Run",
            RootCalls = Enumerable.Range(0, 6).Select(i => Leaf("A", i % 2 == 0 ? "B" : "C", "m" + i, Opt(i + 1, "c" + i))).ToArray(),
        };

        var markdown = new MermaidSequenceSerializer().BuildMarkdown(seq, stackedActivationBars: false, autoNumber: true, maxParticipantsPerDiagram: 0, maxMessagesPerDiagram: 3);

        // Part 1 holds the Caller arrow + 3 calls (messages 1-4), so Part 2 starts at 5 regardless of fragment lines.
        Assert.IsTrue(markdown.Contains("autonumber 5") || markdown.Contains("autonumber 4"), markdown);
        Assert.IsFalse(markdown.Contains("autonumber 7") || markdown.Contains("autonumber 8"), markdown);
    }

    // ── Splitting ─────────────────────────────────────────────────────────────

    private static List<string> Sections(string markdown)
    {
        var parts = new List<string>();
        var current = new List<string>();
        bool inFence = false;
        foreach (var raw in markdown.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (line == "```mermaid") { inFence = true; current = new List<string>(); continue; }
            if (line == "```" && inFence) { inFence = false; parts.Add(string.Join("\n", current)); continue; }
            if (inFence) current.Add(line);
        }
        return parts;
    }

    [TestMethod]
    public void CapCutInsideFragment_ContinuationReopensWithContMarker()
    {
        // Four calls in one loop, cap of 2 messages per part => the loop is cut in two.
        var seq = Seq(
            Leaf("A", "B", "m1", Loop(1, "for")),
            Leaf("A", "C", "m2", Loop(1, "for")),
            Leaf("A", "B", "m3", Loop(1, "for")),
            Leaf("A", "C", "m4", Loop(1, "for")));

        var markdown = new MermaidSequenceSerializer().BuildMarkdown(seq, stackedActivationBars: false, autoNumber: false, maxParticipantsPerDiagram: 0, maxMessagesPerDiagram: 2);
        var sections = Sections(markdown);

        Assert.IsGreaterThanOrEqualTo(2, sections.Count, markdown);
        Assert.IsTrue(sections[0].Contains("    loop for\n"), sections[0]);
        Assert.IsFalse(sections[0].Contains("(cont.)"), sections[0]);
        Assert.IsTrue(sections[1].Contains("    loop for (cont.)\n"), sections[1]);
        foreach (var section in sections)
            AssertFragmentsBalanced(section);
    }

    [TestMethod]
    public void CapCutBetweenSectionsOfOneAlt_ReopensWithTheNewSectionLabel()
    {
        var seq = Seq(
            Leaf("A", "B", "m1", Alt(1, 0, "x")),
            Leaf("A", "C", "m2", Alt(1, 0, "x")),
            Leaf("A", "B", "m3", Alt(1, 1, "else")),
            Leaf("A", "C", "m4", Alt(1, 1, "else")));

        var markdown = new MermaidSequenceSerializer().BuildMarkdown(seq, stackedActivationBars: false, autoNumber: false, maxParticipantsPerDiagram: 0, maxMessagesPerDiagram: 2);
        var sections = Sections(markdown);

        Assert.IsGreaterThanOrEqualTo(2, sections.Count, markdown);
        Assert.IsTrue(sections[1].Contains("    alt else (cont.)\n"), sections[1]);
        foreach (var section in sections)
            AssertFragmentsBalanced(section);
    }

    [TestMethod]
    public void SoftSeam_NeverCutsInsideAFragment_ButCapDoes()
    {
        // Two unrelated participant sets that would normally be split by the business-flow
        // seam (low overlap, >= 3 meaningful participants): sharing a loop keeps them together.
        var seq = new CallSequence
        {
            Title = "T",
            Participants = [new("A", "A"), new("B", "B"), new("C", "C"), new("D", "D"), new("E", "E"), new("F", "F"), new("G", "G")],
            RootCalls =
            [
                Nested("A", "B", "m1", [Loop(1, "for")], Leaf("B", "C", "n1"), Leaf("B", "D", "n2")),
                Nested("A", "E", "m2", [Loop(1, "for")], Leaf("E", "F", "n3"), Leaf("E", "G", "n4")),
            ],
        };

        var serializer = new MermaidSequenceSerializer();
        var lenient = Sections(serializer.BuildMarkdown(seq, stackedActivationBars: false, autoNumber: false, maxParticipantsPerDiagram: 5, maxMessagesPerDiagram: 0));
        var withoutLoop = new CallSequence
        {
            Title = seq.Title,
            Participants = seq.Participants,
            RootCalls = seq.RootCalls.Select(c => c with { FragmentPath = [] }).ToList(),
        };
        var lenientNoLoop = Sections(serializer.BuildMarkdown(withoutLoop, stackedActivationBars: false, autoNumber: false, maxParticipantsPerDiagram: 5, maxMessagesPerDiagram: 0));

        // The participant cap (5) forces a cut in both cases; the seam alone would too, so make
        // the cap generous to isolate the seam behaviour:
        var seamOnly = Sections(serializer.BuildMarkdown(seq, stackedActivationBars: false, autoNumber: false, maxParticipantsPerDiagram: 100, maxMessagesPerDiagram: 0));
        Assert.AreEqual(1, seamOnly.Count, "Sharing a fragment must suppress the soft seam.");

        Assert.IsGreaterThanOrEqualTo(2, lenient.Count, "The participant cap must still cut inside the fragment.");
        Assert.IsGreaterThanOrEqualTo(2, lenientNoLoop.Count);
        foreach (var section in lenient)
            AssertFragmentsBalanced(section);
    }

    [TestMethod]
    public void OversizedCallInsideFragment_SubPartsReopenTheEnclosingFragment()
    {
        // One root call inside an opt whose body is too big for the message cap: the call is
        // split into sub-parts and the opt wraps the split frame in every sub-part.
        var children = Enumerable.Range(0, 6).Select(i => Leaf("B", i % 2 == 0 ? "C" : "A", "n" + i)).ToArray();
        var seq = new CallSequence
        {
            Title = "T",
            Participants = [new("A", "A"), new("B", "B"), new("C", "C")],
            RootParticipantId = "A",
            RootMethodLabel = "Run",
            RootCalls = [Nested("A", "B", "big", [Opt(1, "enabled")], children)],
        };

        var markdown = new MermaidSequenceSerializer().BuildMarkdown(seq, stackedActivationBars: true, autoNumber: true, maxParticipantsPerDiagram: 0, maxMessagesPerDiagram: 4);
        var sections = Sections(markdown);

        Assert.IsGreaterThanOrEqualTo(2, sections.Count, markdown);
        Assert.IsTrue(sections[0].Contains("    opt enabled\n    A->>+B: big\n"), sections[0]);
        Assert.IsTrue(sections[1].Contains("    opt enabled (cont.)\n    activate B\n"), sections[1]);
        Assert.IsTrue(sections[sections.Count - 1].Contains("B-->>-A: big\n    end"), sections[sections.Count - 1]);
        foreach (var section in sections)
        {
            AssertFragmentsBalanced(section);
            AssertActivationsBalanced(section);
        }
    }

    [TestMethod]
    public void DoublyOversized_FragmentsAtBothFrameLevelsReopen()
    {
        var grandChildren = Enumerable.Range(0, 6).Select(i => Leaf("C", i % 2 == 0 ? "A" : "B", "g" + i)).ToArray();
        var seq = new CallSequence
        {
            Title = "T",
            Participants = [new("A", "A"), new("B", "B"), new("C", "C")],
            RootParticipantId = "A",
            RootMethodLabel = "Run",
            RootCalls = [Nested("A", "B", "outer", [Loop(1, "for")], Nested("B", "C", "inner", [Opt(1, "ok")], grandChildren))],
        };

        var markdown = new MermaidSequenceSerializer().BuildMarkdown(seq, stackedActivationBars: true, autoNumber: false, maxParticipantsPerDiagram: 0, maxMessagesPerDiagram: 4);
        var sections = Sections(markdown);

        Assert.IsGreaterThanOrEqualTo(2, sections.Count, markdown);
        Assert.IsTrue(sections[1].Contains("    loop for (cont.)\n") && sections[1].Contains("    opt ok (cont.)\n"), sections[1]);
        foreach (var section in sections)
        {
            AssertFragmentsBalanced(section);
            AssertActivationsBalanced(section);
        }
    }

    [TestMethod]
    public void MessageCap_CountsArrowsOnly()
    {
        var seq = Seq(
            Leaf("A", "B", "m1", Opt(1, "a")),
            Leaf("A", "C", "m2", Opt(2, "b")),
            Leaf("A", "B", "m3", Opt(3, "c")));

        // 3 arrows, cap 3: fragments add 6 extra lines but must not trigger a split.
        var markdown = new MermaidSequenceSerializer().BuildMarkdown(seq, stackedActivationBars: false, autoNumber: false, maxParticipantsPerDiagram: 0, maxMessagesPerDiagram: 3);

        Assert.AreEqual(1, Sections(markdown).Count, markdown);
    }

    // ── Simulators ────────────────────────────────────────────────────────────

    internal static void AssertFragmentsBalanced(string diagram)
    {
        var stack = new Stack<string>();
        foreach (var raw in diagram.Split('\n'))
        {
            var line = raw.Trim();
            var keyword = line.Split(' ')[0];
            switch (keyword)
            {
                case "alt":
                case "opt":
                case "loop":
                case "break":
                case "par":
                case "critical":
                    stack.Push(keyword);
                    break;
                case "else":
                    Assert.IsTrue(stack.Count > 0 && stack.Peek() == "alt", $"'else' outside alt at '{line}' in:\n{diagram}");
                    break;
                case "and":
                    Assert.IsTrue(stack.Count > 0 && stack.Peek() == "par", $"'and' outside par at '{line}' in:\n{diagram}");
                    break;
                case "option":
                    Assert.IsTrue(stack.Count > 0 && stack.Peek() == "critical", $"'option' outside critical at '{line}' in:\n{diagram}");
                    break;
                case "end":
                    Assert.IsTrue(stack.Count > 0, $"'end' without an open fragment in:\n{diagram}");
                    stack.Pop();
                    break;
            }
        }

        Assert.AreEqual(0, stack.Count, $"{stack.Count} fragment(s) left open in:\n{diagram}");
    }

    internal static void AssertActivationsBalanced(string diagram)
    {
        var depth = new Dictionary<string, int>(StringComparer.Ordinal);
        void Push(string p) { depth.TryGetValue(p, out var d); depth[p] = d + 1; }
        void Pop(string p, string line)
        {
            depth.TryGetValue(p, out var d);
            Assert.IsGreaterThan(0, d, $"Deactivating inactive participant '{p}' at line '{line}' in:\n{diagram}");
            depth[p] = d - 1;
        }

        foreach (var raw in diagram.Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith("activate ")) { Push(line.Substring("activate ".Length).Trim()); continue; }
            if (line.StartsWith("deactivate ")) { Pop(line.Substring("deactivate ".Length).Trim(), line); continue; }

            int arrowIdx = line.IndexOf("-->>", StringComparison.Ordinal);
            bool dashed = arrowIdx >= 0;
            if (!dashed) arrowIdx = line.IndexOf("->>", StringComparison.Ordinal);
            if (arrowIdx < 0) continue;

            var sender = line.Substring(0, arrowIdx).Trim();
            var rest = line.Substring(arrowIdx + (dashed ? 4 : 3));
            char marker = rest.Length > 0 ? rest[0] : '\0';
            var receiverPart = marker is '+' or '-' ? rest.Substring(1) : rest;
            int colon = receiverPart.IndexOf(':');
            var receiver = (colon >= 0 ? receiverPart.Substring(0, colon) : receiverPart).Trim();

            if (marker == '+') Push(receiver);
            else if (marker == '-') Pop(sender, line);
        }

        foreach (var kv in depth)
            Assert.AreEqual(0, kv.Value, $"Participant '{kv.Key}' left with {kv.Value} open activation(s) in:\n{diagram}");
    }
}
