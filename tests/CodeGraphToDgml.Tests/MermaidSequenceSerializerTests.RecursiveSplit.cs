using System.Linq;
using CodeGraphToDgml.Core;

namespace CodeGraphToDgml.Tests;

// Recursive sub-part splitting of a sub-part that is itself one oversized call.
public sealed partial class MermaidSequenceSerializerTests
{
    // ──────────────────────────────────────────────────────────────────────────
    // Recursive sub-part splitting: a sub-part that is itself one oversized call
    // (e.g. "1C") must keep splitting into 1C1/1C2/... instead of being emitted
    // as one big chunk.
    // ──────────────────────────────────────────────────────────────────────────

    // A→B (BigEntry) has four nested calls: Sub1, Sub2, Sub3, and BigMid. At
    // maxParticipantsPerDiagram=4 this phase first splits into 1A=[Sub1,Sub2],
    // 1B=[Sub3], 1C=[BigMid] — but BigMid (B→H) itself fans out into five more
    // calls (H→I..H→M), so 1C alone still exceeds the cap and must recurse into
    // 1C1/1C2/1C3.
    private static CallSequence DoublyOversizedCallSequence() => new()
    {
        Title = "BigCall",
        Participants =
        [
            P("A", "A"), P("B", "B"), P("C", "C"), P("D", "D"), P("E", "E"),
            P("H", "H"), P("I", "I"), P("J", "J"), P("K", "K"), P("L", "L"), P("M", "M"),
        ],
        RootCalls =
        [
            Nested("A", "B", "BigEntry",
            [
                Leaf("B", "C", "Sub1"),
                Leaf("B", "D", "Sub2"),
                Leaf("B", "E", "Sub3"),
                Nested("B", "H", "BigMid",
                [
                    Leaf("H", "I", "SubI"),
                    Leaf("H", "J", "SubJ"),
                    Leaf("H", "K", "SubK"),
                    Leaf("H", "L", "SubL"),
                    Leaf("H", "M", "SubM"),
                ]),
            ]),
        ],
    };

    [TestMethod]
    public void BuildMarkdown_DoublyOversizedCall_RecursesIntoSecondLevelSubParts()
    {
        var result = Serializer.BuildMarkdown(DoublyOversizedCallSequence(),
            stackedActivationBars: true, autoNumber: false, maxParticipantsPerDiagram: 4);

        Assert.Contains("Part 1A", result);
        Assert.Contains("Part 1B", result);
        Assert.Contains("Part 1C1", result, "The oversized sub-part 1C must split further using digit suffixes.");
        Assert.Contains("Part 1C2", result);
        Assert.Contains("Part 1C3", result);
        Assert.DoesNotContain("Part 1C ", result, "1C must not be emitted on its own once it recurses.");

        var headingCount = result.Split('\n').Count(l => l.TrimStart().StartsWith("## Part"));
        Assert.AreEqual(5, headingCount);
    }

    [TestMethod]
    public void BuildMarkdown_DoublyOversizedCall_EverySegmentHasBalancedActivations()
    {
        var result = Serializer.BuildMarkdown(DoublyOversizedCallSequence(),
            stackedActivationBars: true, autoNumber: false, maxParticipantsPerDiagram: 4);

        var blocks = ExtractMermaidBlocks(result);
        Assert.HasCount(5, blocks);
        foreach (var block in blocks)
            AssertActivationsBalanced(block);
    }

    [TestMethod]
    public void BuildMarkdown_DoublyOversizedCall_OuterParentSpansAllFiveSubParts()
    {
        // BigEntry (A→B) must stay "in flight" across every leaf, including the
        // second-level 1C1/1C2/1C3 sub-parts: opened only in 1A, kept alive with a
        // bare activation everywhere in between, closed only in 1C3.
        var result = Serializer.BuildMarkdown(DoublyOversizedCallSequence(),
            stackedActivationBars: true, autoNumber: false, maxParticipantsPerDiagram: 4);

        var sections = result.Split(new[] { "## Part" }, StringSplitOptions.None);
        Assert.HasCount(6, sections); // preamble + 5 parts

        Assert.Contains("A->>+B: BigEntry", sections[1], "1A must open BigEntry with the real arrow.");
        for (int i = 2; i <= 5; i++)
            Assert.DoesNotContain("A->>+B: BigEntry", sections[i], "Only 1A shows BigEntry's opening arrow.");

        for (int i = 2; i <= 5; i++)
            Assert.Contains("activate B", sections[i], "BigEntry's bar on B must stay alive through every later sub-part.");

        Assert.Contains("B-->>-A: ", sections[^1], "1C3 must close BigEntry with its return arrow.");
        for (int i = 1; i <= 4; i++)
            Assert.DoesNotContain("B-->>-A", sections[i], "Only the last sub-part (1C3) closes BigEntry.");
    }

    [TestMethod]
    public void BuildMarkdown_DoublyOversizedCall_InnerParentSpansOnlyItsOwnThreeSubParts()
    {
        // BigMid (B→H) only spans 1C1/1C2/1C3 — it must not appear at all in 1A/1B.
        var result = Serializer.BuildMarkdown(DoublyOversizedCallSequence(),
            stackedActivationBars: true, autoNumber: false, maxParticipantsPerDiagram: 4);

        var sections = result.Split(new[] { "## Part" }, StringSplitOptions.None);

        Assert.DoesNotContain("B->>+H: BigMid", sections[1], "1A must not reference BigMid at all.");
        Assert.DoesNotContain("B->>+H: BigMid", sections[2], "1B must not reference BigMid at all.");

        Assert.Contains("B->>+H: BigMid", sections[3], "1C1 must open BigMid with the real arrow.");
        Assert.Contains("activate H", sections[4], "1C2 must keep BigMid's bar on H alive.");
        Assert.Contains("H-->>-B: ", sections[^1], "1C3 must close BigMid with its return arrow.");
    }

    [TestMethod]
    public void BuildMarkdown_DoublyOversizedCall_GlobalNumberingContinuesAcrossAllFiveSubParts()
    {
        var result = Serializer.BuildMarkdown(DoublyOversizedCallSequence(),
            stackedActivationBars: true, autoNumber: true, maxParticipantsPerDiagram: 4);

        var autonumberLines = result.Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.StartsWith("autonumber"))
            .ToArray();

        Assert.HasCount(5, autonumberLines);
        Assert.AreEqual("autonumber", autonumberLines[0]);

        var starts = autonumberLines
            .Select(l => l.Split(' ') is [_, var n] ? int.Parse(n) : 1)
            .ToArray();

        for (int i = 1; i < starts.Length; i++)
            Assert.IsGreaterThan(starts[i - 1], starts[i], "Each sub-part must start numbering strictly after the previous one.");
    }
}
