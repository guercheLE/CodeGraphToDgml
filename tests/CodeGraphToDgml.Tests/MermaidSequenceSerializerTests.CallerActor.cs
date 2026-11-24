using System.Linq;
using CodeGraphToDgml.Core;

namespace CodeGraphToDgml.Tests;

// The synthetic «Caller» actor and balanced activations across split segments.
public sealed partial class MermaidSequenceSerializerTests
{
    // ──────────────────────────────────────────────────────────────────────────
    // Item 2: root/entry-method activation bar via synthetic «Caller» actor
    // ──────────────────────────────────────────────────────────────────────────

    private static CallSequence LinearChainWithRoot() => new()
    {
        Title = "ClassA.btnLiberar_Click",
        Participants = [P("ClassA", "ClassA"), P("ClassB", "ClassB"), P("ClassC", "ClassC")],
        RootCalls =
        [
            Nested("ClassA", "ClassB", "DoB",
                Leaf("ClassB", "ClassC", "DoC")),
        ],
        RootParticipantId = "ClassA",
        RootMethodLabel = "btnLiberar_Click",
    };

    [TestMethod]
    public void Serialize_WithoutRootParticipant_NoCallerActorEmitted()
    {
        // Default/legacy CallSequences (RootParticipantId unset) render exactly as before item 2.
        var result = Serializer.Serialize(LinearChain());
        Assert.DoesNotContain("actor ", result);
        Assert.DoesNotContain("Caller", result);
    }

    [TestMethod]
    public void Serialize_WithRootParticipant_EmitsCallerActorBeforeParticipants()
    {
        var result = Serializer.Serialize(LinearChainWithRoot());
        Assert.Contains("actor Caller as «Caller»", result);

        int actorIdx = result.IndexOf("actor Caller", StringComparison.Ordinal);
        int firstParticipantIdx = result.IndexOf("participant ClassA", StringComparison.Ordinal);
        Assert.IsLessThan(firstParticipantIdx, actorIdx, "Caller actor should be declared before real participants.");
    }

    [TestMethod]
    public void Serialize_WithRootParticipant_StackedBarsOn_WrapsRootMethodWithActivation()
    {
        var result = Serializer.Serialize(LinearChainWithRoot(), stackedActivationBars: true);

        Assert.Contains("Caller->>+ClassA: btnLiberar_Click", result);
        Assert.Contains("ClassA-->>-Caller: ", result);

        var lines = result.Split('\n').Select(l => l.Trim()).ToArray();
        int open = Array.FindIndex(lines, l => l.StartsWith("Caller->>+ClassA"));
        int inner = Array.FindIndex(lines, l => l.StartsWith("ClassA->>+ClassB"));
        int close = Array.FindIndex(lines, l => l.StartsWith("ClassA-->>-Caller"));

        Assert.IsGreaterThanOrEqualTo(0, open, "Missing Caller opening call");
        Assert.IsGreaterThanOrEqualTo(0, inner, "Missing inner ClassA->>ClassB call");
        Assert.IsGreaterThanOrEqualTo(0, close, "Missing Caller closing return");
        Assert.IsLessThan(inner, open, "Caller must call the entry method before it calls anything else");
        Assert.IsLessThan(close, inner, "Entry method's own calls must finish before Caller's activation closes");
    }

    [TestMethod]
    public void Serialize_WithRootParticipant_StackedBarsOff_PlainArrowNoReturnLine()
    {
        var result = Serializer.Serialize(LinearChainWithRoot(), stackedActivationBars: false);

        Assert.Contains("Caller->>ClassA: btnLiberar_Click", result);
        Assert.DoesNotContain("Caller->>+", result, "No activation marker when stacked bars off");
        Assert.DoesNotContain("-->>-Caller", result, "No return line for Caller when stacked bars off");
    }

    [TestMethod]
    public void Serialize_WithRootParticipant_RealParticipantNamedCaller_SyntheticActorGetsDistinctId()
    {
        var sequence = new CallSequence
        {
            Participants = [P("Caller", "Caller"), P("B", "B")],
            RootCalls = [Leaf("Caller", "B", "DoWork")],
            RootParticipantId = "Caller",
            RootMethodLabel = "Entry",
        };

        var result = Serializer.Serialize(sequence);

        Assert.Contains("actor Caller_1 as «Caller»", result, "Synthetic actor must not collide with the real 'Caller' participant");
        Assert.Contains("participant Caller as Caller", result, "Real participant named 'Caller' must remain untouched");
        Assert.Contains("Caller_1->>+Caller: Entry", result);
    }

    // Three root calls sharing the same caller "Entry" (as real traversal data always does —
    // every RootCalls entry is a direct call FROM the entry method), each with its own disjoint
    // pair of participants, forced into 3 separate phases by a tight participant cap.
    private static CallSequence ThreePhaseSequenceWithRoot() => new()
    {
        Title = "Root",
        Participants =
        [
            P("Entry", "Entry"),
            P("A", "A"), P("B", "B"),
            P("C", "C"), P("D", "D"),
            P("E", "E"), P("F", "F"),
        ],
        RootCalls =
        [
            Nested("Entry", "A", "Call1", Leaf("A", "B", "Sub1")),
            Nested("Entry", "C", "Call2", Leaf("C", "D", "Sub2")),
            Nested("Entry", "E", "Call3", Leaf("E", "F", "Sub3")),
        ],
        RootParticipantId = "Entry",
        RootMethodLabel = "EntryMethod",
    };

    [TestMethod]
    public void BuildMarkdown_MultiSection_CallerActorDeclaredInEverySegment()
    {
        var result = Serializer.BuildMarkdown(ThreePhaseSequenceWithRoot(),
            stackedActivationBars: true, autoNumber: false, maxParticipantsPerDiagram: 3);

        var headingCount = result.Split('\n').Count(l => l.TrimStart().StartsWith("## Part"));
        Assert.AreEqual(3, headingCount, "Expected exactly 3 phases given the tight participant cap.");

        int actorDeclCount = result.Split('\n').Count(l => l.Trim() == "actor Caller as «Caller»");
        Assert.AreEqual(3, actorDeclCount, "The «Caller» actor must be declared in every segment.");
    }

    [TestMethod]
    public void BuildMarkdown_MultiSection_FirstSegmentHasOpeningCallOnly()
    {
        var result = Serializer.BuildMarkdown(ThreePhaseSequenceWithRoot(),
            stackedActivationBars: true, autoNumber: false, maxParticipantsPerDiagram: 3);

        var sections = result.Split(new[] { "## Part" }, StringSplitOptions.None);
        // sections[0] is the markdown preamble before "## Part 1"; sections[1] is Part 1's content.
        var firstSection = sections[1];

        Assert.Contains("Caller->>+Entry: EntryMethod", firstSection);
        Assert.DoesNotContain("Entry-->>-Caller", firstSection, "First segment must not close the Caller activation.");
    }

    [TestMethod]
    public void BuildMarkdown_MultiSection_LastSegmentHasClosingReturnOnly()
    {
        var result = Serializer.BuildMarkdown(ThreePhaseSequenceWithRoot(),
            stackedActivationBars: true, autoNumber: false, maxParticipantsPerDiagram: 3);

        var sections = result.Split(new[] { "## Part" }, StringSplitOptions.None);
        var lastSection = sections[^1];

        Assert.Contains("Entry-->>-Caller: ", lastSection);
        Assert.DoesNotContain("Caller->>+Entry", lastSection, "Last segment must not re-open the Caller activation.");
    }

    [TestMethod]
    public void BuildMarkdown_MultiSection_MiddleSegmentOnlyActivatesAndDeactivatesCaller()
    {
        var result = Serializer.BuildMarkdown(ThreePhaseSequenceWithRoot(),
            stackedActivationBars: true, autoNumber: false, maxParticipantsPerDiagram: 3);

        var sections = result.Split(new[] { "## Part" }, StringSplitOptions.None);
        var middleSection = sections[2]; // "## Part 2" content

        Assert.Contains("activate Caller", middleSection);
        Assert.Contains("deactivate Caller", middleSection);
        Assert.DoesNotContain("Caller->>+Entry", middleSection, "Middle segment must not re-open the Caller activation with an arrow.");
        Assert.DoesNotContain("Entry-->>-Caller", middleSection, "Middle segment must not close the Caller activation with an arrow.");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Item 2 regression: every split segment must keep Mermaid's activation
    // stack balanced on its own, or Mermaid renders "Trying to inactivate an
    // inactive participant" instead of the diagram.
    // ──────────────────────────────────────────────────────────────────────────

    private static List<string> ExtractMermaidBlocks(string markdown)
    {
        var blocks = new List<string>();
        List<string>? current = null;
        foreach (var raw in markdown.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (line.Trim() == "```mermaid") { current = []; continue; }
            if (line.Trim() == "```" && current != null) { blocks.Add(string.Join("\n", current)); current = null; continue; }
            current?.Add(line);
        }
        return blocks;
    }

    // Simulates Mermaid's activation bookkeeping for one diagram: 'activate X' and
    // '->>+' push a bar; 'deactivate X' and '-->>-' pop one (the '+' applies to the
    // receiver, the '-' to the sender). Mermaid fails the whole render the moment a
    // pop has no matching bar, so depth must never go negative — and any bar left
    // open at the end means the segment leaked an activation into nowhere.
    private static void AssertActivationsBalanced(string diagram)
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

    private static CallSequence OversizedSingleCallSequenceWithRoot()
    {
        var seq = OversizedSingleCallSequence();
        return new CallSequence
        {
            Title = seq.Title,
            Participants = seq.Participants,
            RootCalls = seq.RootCalls,
            RootParticipantId = "A",
            RootMethodLabel = "btnBig_Click",
        };
    }

    [TestMethod]
    public void BuildMarkdown_MultiSection_WithRoot_EverySegmentHasBalancedActivations()
    {
        var result = Serializer.BuildMarkdown(ThreePhaseSequenceWithRoot(),
            stackedActivationBars: true, autoNumber: false, maxParticipantsPerDiagram: 3);

        var blocks = ExtractMermaidBlocks(result);
        Assert.IsGreaterThan(1, blocks.Count, "Expected a multi-section split.");
        foreach (var block in blocks)
            AssertActivationsBalanced(block);
    }

    [TestMethod]
    public void BuildMarkdown_MultiSection_FirstSegmentDoesNotDeactivateUnactivatedCaller()
    {
        var result = Serializer.BuildMarkdown(ThreePhaseSequenceWithRoot(),
            stackedActivationBars: true, autoNumber: false, maxParticipantsPerDiagram: 3);

        var sections = result.Split(new[] { "## Part" }, StringSplitOptions.None);
        var firstSection = sections[1];

        Assert.DoesNotContain("deactivate Caller", firstSection,
            "First segment never activates Caller (the opening arrow's '+' activates the root), so it must not deactivate it.");
        Assert.Contains("deactivate Entry", firstSection,
            "First segment must silently close the root activation opened by the '+' arrow.");
    }

    [TestMethod]
    public void BuildMarkdown_SubParts_WithRoot_EverySegmentHasBalancedActivations()
    {
        var result = Serializer.BuildMarkdown(OversizedSingleCallSequenceWithRoot(),
            stackedActivationBars: true, autoNumber: false, maxParticipantsPerDiagram: 4);

        var blocks = ExtractMermaidBlocks(result);
        Assert.IsGreaterThan(1, blocks.Count, "Expected a sub-part split.");
        foreach (var block in blocks)
            AssertActivationsBalanced(block);
    }

    [TestMethod]
    public void BuildMarkdown_SubParts_NoRoot_EverySegmentHasBalancedActivations()
    {
        var result = Serializer.BuildMarkdown(OversizedSingleCallSequence(),
            stackedActivationBars: true, autoNumber: false, maxParticipantsPerDiagram: 4);

        var blocks = ExtractMermaidBlocks(result);
        Assert.IsGreaterThan(1, blocks.Count, "Expected a sub-part split.");
        foreach (var block in blocks)
            AssertActivationsBalanced(block);
    }

    [TestMethod]
    public void BuildMarkdown_SubParts_FirstSubPartShowsParentCallArrow()
    {
        // The split parent call spans its sub-parts exactly like the «Caller» wrap: the first
        // sub-part opens it with the real arrow (label included), not a bare activate.
        var result = Serializer.BuildMarkdown(OversizedSingleCallSequence(),
            stackedActivationBars: true, autoNumber: false, maxParticipantsPerDiagram: 4);

        var sections = result.Split(new[] { "## Part" }, StringSplitOptions.None);
        var firstSubPart = sections[1];

        Assert.Contains("A->>+B: BigEntry", firstSubPart, "First sub-part must show the parent call's opening arrow.");
        Assert.IsFalse(firstSubPart.Split('\n').Any(l => l.Trim() == "activate B"),
            "The '+' on the opening arrow replaces the bare activate.");
        Assert.DoesNotContain("B-->>-A", firstSubPart, "First sub-part must not close the parent call.");
    }

    [TestMethod]
    public void BuildMarkdown_SubParts_LastSubPartShowsParentCallReturn()
    {
        var result = Serializer.BuildMarkdown(OversizedSingleCallSequence(),
            stackedActivationBars: true, autoNumber: false, maxParticipantsPerDiagram: 4);

        var sections = result.Split(new[] { "## Part" }, StringSplitOptions.None);
        var lastSubPart = sections[^1];

        Assert.Contains("B-->>-A: ", lastSubPart, "Last sub-part must close the parent call with its return arrow.");
        Assert.DoesNotContain("A->>+B", lastSubPart, "Last sub-part must not re-open the parent call.");
        Assert.IsFalse(lastSubPart.Split('\n').Any(l => l.Trim() == "deactivate B"),
            "The '-' on the return arrow replaces the bare deactivate.");
    }

    [TestMethod]
    public void BuildMarkdown_SubParts_MiddleSubPartKeepsParentBarsAliveWithBareActivations()
    {
        var result = Serializer.BuildMarkdown(OversizedSingleCallSequence(),
            stackedActivationBars: true, autoNumber: false, maxParticipantsPerDiagram: 4);

        var sections = result.Split(new[] { "## Part" }, StringSplitOptions.None);
        Assert.IsGreaterThanOrEqualTo(4, sections.Length, "Need at least 3 sub-parts for a middle one.");
        var middleSubPart = sections[2];

        Assert.Contains("activate B", middleSubPart);
        Assert.Contains("deactivate B", middleSubPart);
        Assert.DoesNotContain("A->>+B", middleSubPart, "Middle sub-part must not re-open the parent call with an arrow.");
        Assert.DoesNotContain("B-->>-A", middleSubPart, "Middle sub-part must not close the parent call with an arrow.");
    }

    [TestMethod]
    public void BuildMarkdown_SubParts_StackedBarsOff_FirstSubPartShowsPlainParentCallArrow()
    {
        var result = Serializer.BuildMarkdown(OversizedSingleCallSequence(),
            stackedActivationBars: false, autoNumber: false, maxParticipantsPerDiagram: 4);

        var sections = result.Split(new[] { "## Part" }, StringSplitOptions.None);

        Assert.Contains("A->>B: BigEntry", sections[1], "First sub-part must show the plain parent call arrow.");
        for (int i = 2; i < sections.Length; i++)
            Assert.DoesNotContain("A->>B: BigEntry", sections[i], "Only the first sub-part shows the parent call arrow.");
    }

    [TestMethod]
    public void BuildMarkdown_SubParts_WithRoot_AutonumberCountsWrapAndParentArrows()
    {
        // The «Caller» wrap and parent-call arrows are numbered by Mermaid like any other
        // arrow, so segment start numbers must account for them — otherwise adjacent parts
        // render duplicate message numbers.
        var result = Serializer.BuildMarkdown(OversizedSingleCallSequenceWithRoot(),
            stackedActivationBars: true, autoNumber: true, maxParticipantsPerDiagram: 4);

        var autonumbers = result.Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.StartsWith("autonumber"))
            .ToList();

        // 1A: Caller arrow + parent arrow + 2 calls with returns = 6 → 1B starts at 7.
        // 1B: 2 calls with returns = 4 → 1C starts at 11.
        CollectionAssert.AreEqual(new[] { "autonumber", "autonumber 7", "autonumber 11" }, autonumbers);
    }

    [TestMethod]
    public void BuildMarkdown_SubParts_WithRoot_LastSegmentDoesNotDoubleDeactivateRoot()
    {
        // The last sub-part's closing return (A-->>-Caller) already consumes the root
        // activation opened by the split boundary — the boundary teardown must not
        // deactivate the root a second time.
        var result = Serializer.BuildMarkdown(OversizedSingleCallSequenceWithRoot(),
            stackedActivationBars: true, autoNumber: false, maxParticipantsPerDiagram: 4);

        var sections = result.Split(new[] { "## Part" }, StringSplitOptions.None);
        var lastSection = sections[^1];

        Assert.Contains("A-->>-Caller: ", lastSection, "Last sub-part must close with the return to Caller.");
        int bareDeactivateRoot = lastSection.Split('\n').Count(l => l.Trim() == "deactivate A");
        Assert.AreEqual(0, bareDeactivateRoot,
            "Root 'A' is deactivated by the return arrow's '-'; a bare 'deactivate A' would double-pop it.");
    }
}
