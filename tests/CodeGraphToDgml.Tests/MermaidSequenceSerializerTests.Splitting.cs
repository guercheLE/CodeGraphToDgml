using System.Linq;
using CodeGraphToDgml.Core;

namespace CodeGraphToDgml.Tests;

// Multi-diagram splitting: numbering, titles, banners, sub-parts, business-flow seams.
public sealed partial class MermaidSequenceSerializerTests
{
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

    // ──────────────────────────────────────────────────────────────────────────
    // Item 3: business-flow-aware splitting (low-overlap heuristic + message cap)
    // ──────────────────────────────────────────────────────────────────────────

    // Three root calls, each with 3 distinct participants and zero participant overlap with
    // the others. A pure participant-count-only greedy split at maxParticipants=6 would merge
    // Call1+Call2 into one phase (3+3=6, not over cap) then Call3 alone — 2 phases. The
    // low-overlap heuristic should instead cut between every pair (0% overlap, 3 "meaningful"
    // participants already accumulated each time), producing 3 phases.
    private static CallSequence LowOverlapPhaseSequence() => new()
    {
        Title = "Root",
        Participants =
        [
            P("A", "A"), P("B", "B"), P("C", "C"),
            P("D", "D"), P("E", "E"), P("F", "F"),
            P("G", "G"), P("H", "H"), P("I", "I"),
        ],
        RootCalls =
        [
            // Each root call has its own caller (A/D/G), so the three calls share no
            // participants at all — a genuinely disjoint fixture, unlike a shared-root-caller
            // design that would accidentally keep overlap non-zero.
            Nested("A", "B", "Call1", Leaf("B", "C", "Sub1")),
            Nested("D", "E", "Call2", Leaf("E", "F", "Sub2")),
            Nested("G", "H", "Call3", Leaf("H", "I", "Sub3")),
        ],
    };

    [TestMethod]
    public void BuildMarkdown_LowOverlapCalls_SplitsMoreThanParticipantCapAlone()
    {
        // maxParticipantsPerDiagram=6 would allow Call1+Call2 to share a phase by count alone
        // (3+3=6, not over cap) — the low-overlap heuristic should still split them apart.
        var result = Serializer.BuildMarkdown(LowOverlapPhaseSequence(),
            stackedActivationBars: true, autoNumber: false, maxParticipantsPerDiagram: 6);

        var headingCount = result.Split('\n').Count(l => l.TrimStart().StartsWith("## Part"));
        Assert.AreEqual(3, headingCount,
            "Three mutually disjoint calls should each land in their own phase due to low participant overlap, " +
            "not be greedily packed two-per-phase by participant count alone.");
    }

    [TestMethod]
    public void BuildMarkdown_LowOverlapCalls_EachPartHasOwnDistinctParticipants()
    {
        var result = Serializer.BuildMarkdown(LowOverlapPhaseSequence(),
            stackedActivationBars: false, autoNumber: false, maxParticipantsPerDiagram: 6);

        Assert.Contains("Call1", result);
        Assert.Contains("Call2", result);
        Assert.Contains("Call3", result);
    }

    // A handful of small calls sharing a common caller "A" (so the accumulating segment never
    // exceeds MinPhaseParticipantsForSplit=3 "meaningful" participants by the time the third call
    // is evaluated, and "A" itself keeps overlap non-trivial) must not be fragmented purely by
    // the low-overlap heuristic. Participants list is padded with unused entries so the sequence
    // total exceeds maxParticipantsPerDiagram and genuinely enters segmentation, rather than the
    // test passing trivially via BuildSections' early single-diagram bypass.
    private static CallSequence SmallDisjointCallsUnderThresholdSequence() => new()
    {
        Title = "Root",
        Participants =
        [
            P("A", "A"), P("B", "B"), P("C", "C"), P("D", "D"),
            P("X1", "X1"), P("X2", "X2"), P("X3", "X3"), P("X4", "X4"), P("X5", "X5"),
        ],
        RootCalls =
        [
            Leaf("A", "B", "First"),
            Leaf("A", "C", "Second"),
            Leaf("A", "D", "Third"),
        ],
    };

    [TestMethod]
    public void BuildMarkdown_FewSmallCallsSharingCaller_NotFragmentedByOverlapHeuristic()
    {
        // 9 declared participants > maxParticipantsPerDiagram(8) forces entry into segmentation,
        // but First/Second/Third (only ever touching A,B / A,C / A,D) never individually exceed
        // the cap and share caller A, so neither the participant cap nor the low-overlap
        // heuristic should fragment them.
        var result = Serializer.BuildMarkdown(SmallDisjointCallsUnderThresholdSequence(),
            stackedActivationBars: false, autoNumber: false, maxParticipantsPerDiagram: 8);

        var headingCount = result.Split('\n').Count(l => l.TrimStart().StartsWith("## Part"));
        Assert.AreEqual(0, headingCount, "Should remain a single (unsplit) diagram — no ## Part headings.");
    }

    // Many calls between the same two participants (100% overlap every time, so the low-overlap
    // heuristic never fires) — isolates the message-count cap as the sole driver of splitting.
    private static CallSequence MessageCountSequence()
    {
        var calls = Enumerable.Range(1, 10)
            .Select(i => Leaf("A", "B", $"Call{i}"))
            .ToArray();

        return new CallSequence
        {
            Title = "Root",
            Participants = [P("A", "A"), P("B", "B")],
            RootCalls = calls,
        };
    }

    [TestMethod]
    public void BuildMarkdown_MessageCountExceedsCap_SplitsEvenWithoutParticipantCap()
    {
        // stackedBars=true: each of the 10 calls contributes 2 arrows (call+return) = 20 total.
        // maxMessagesPerDiagram=8 should force a split despite only 2 participants total
        // (well under any reasonable participant cap, and no participant cap is even set here).
        var result = Serializer.BuildMarkdown(MessageCountSequence(),
            stackedActivationBars: true, autoNumber: false, maxParticipantsPerDiagram: 0, maxMessagesPerDiagram: 8);

        var headingCount = result.Split('\n').Count(l => l.TrimStart().StartsWith("## Part"));
        Assert.IsGreaterThan(1, headingCount, "Message cap alone should force multiple sections.");
    }

    [TestMethod]
    public void BuildMarkdown_MessageCountUnderCap_RemainsSingleDiagram()
    {
        var result = Serializer.BuildMarkdown(MessageCountSequence(),
            stackedActivationBars: true, autoNumber: false, maxParticipantsPerDiagram: 0, maxMessagesPerDiagram: 100);

        var headingCount = result.Split('\n').Count(l => l.TrimStart().StartsWith("## Part"));
        Assert.AreEqual(0, headingCount, "Should remain a single (unsplit) diagram when under the message cap.");
    }

    [TestMethod]
    public void BuildMarkdown_NoCapsConfigured_RemainsSingleDiagram()
    {
        // Sanity check: with neither cap set (both default to 0/off), splitting must never occur,
        // regardless of how large the sequence is.
        var result = Serializer.BuildMarkdown(MessageCountSequence(), stackedActivationBars: true, autoNumber: false);

        var headingCount = result.Split('\n').Count(l => l.TrimStart().StartsWith("## Part"));
        Assert.AreEqual(0, headingCount);
    }
}
