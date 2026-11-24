using System.Linq;
using CodeGraphToDgml.Core;

namespace CodeGraphToDgml.Tests;

// Message-cap enforcement inside one oversized call, and tiny-tail merging.
public sealed partial class MermaidSequenceSerializerTests
{
    // ──────────────────────────────────────────────────────────────────────────
    // Message-cap enforcement inside a single oversized call + tiny-tail merge
    // ──────────────────────────────────────────────────────────────────────────

    private static int CountArrowLines(string diagram)
        => diagram.Split('\n').Count(l => l.Contains("->>"));

    // One deep-but-narrow root call: few participants (never near any participant cap) but a
    // subtree whose arrow count vastly exceeds the message cap — the "ExecuteAsync monolith"
    // shape from real controller flows.
    private static CallSequence DeepNarrowMonolithSequence()
    {
        var leaves = Enumerable.Range(1, 40)
            .Select(i => Leaf("B", "C", $"Work{i}"))
            .ToArray();

        return new CallSequence
        {
            Title = "Monolith",
            Participants = [P("A", "A"), P("B", "B"), P("C", "C")],
            RootCalls = [new CallSequenceCallNode("A", "B", "BigEntry", leaves)],
        };
    }

    [TestMethod]
    public void BuildMarkdown_DeepNarrowMonolith_MessageCapSplitsSingleCall()
    {
        // 40 nested calls × 2 arrows + the parent call's own pair = 82 arrows, only 3
        // participants. The message cap alone must force sub-part splitting of the single call.
        var result = Serializer.BuildMarkdown(DeepNarrowMonolithSequence(),
            stackedActivationBars: true, autoNumber: false, maxParticipantsPerDiagram: 0, maxMessagesPerDiagram: 20);

        var headingCount = result.Split('\n').Count(l => l.TrimStart().StartsWith("## Part"));
        Assert.IsGreaterThan(1, headingCount, "A single call exceeding the message cap must split into sub-parts.");
    }

    [TestMethod]
    public void BuildMarkdown_DeepNarrowMonolith_EveryPartRespectsMessageCap()
    {
        var result = Serializer.BuildMarkdown(DeepNarrowMonolithSequence(),
            stackedActivationBars: true, autoNumber: false, maxParticipantsPerDiagram: 0, maxMessagesPerDiagram: 20);

        var blocks = ExtractMermaidBlocks(result);
        Assert.IsGreaterThan(1, blocks.Count);
        foreach (var block in blocks)
        {
            // Each sub-part carries the split parent's open/close arrow on top of its own
            // budgeted calls, so allow the cap plus that frame overhead.
            Assert.IsLessThanOrEqualTo(20 + 2, CountArrowLines(block),
                "Every sub-part must stay within the message cap (plus split-frame overhead):\n" + block);
            AssertActivationsBalanced(block);
        }
    }

    // Linear single-child chain (controller → handler → worker) where only the innermost level
    // has siblings to split between. The slicer must descend through the lone-child levels
    // instead of giving up and emitting the whole chain as one giant part.
    private static CallSequence SingleChildChainSequence()
    {
        var work = Enumerable.Range(1, 20)
            .Select(i => Leaf("D", "E", $"Work{i}"))
            .ToArray();

        return new CallSequence
        {
            Title = "Chain",
            Participants = [P("A", "A"), P("B", "B"), P("C", "C"), P("D", "D"), P("E", "E")],
            RootCalls =
            [
                Nested("A", "B", "Handle",
                    Nested("B", "C", "Execute",
                        new CallSequenceCallNode("C", "D", "Run", work))),
            ],
        };
    }

    [TestMethod]
    public void BuildMarkdown_SingleChildChain_DescendsToSplittableLevel()
    {
        var result = Serializer.BuildMarkdown(SingleChildChainSequence(),
            stackedActivationBars: true, autoNumber: false, maxParticipantsPerDiagram: 0, maxMessagesPerDiagram: 12);

        var headingCount = result.Split('\n').Count(l => l.TrimStart().StartsWith("## Part"));
        Assert.IsGreaterThan(1, headingCount,
            "An oversized single-child chain must be split at the level where siblings exist.");

        // The lone-child pass-through levels add no suffix, so the innermost split still
        // produces first-level sub-part numbers (1A, 1B, …), not 1A1A-style noise.
        Assert.Contains("Part 1A", result, "First sub-part of the chain should be numbered 1A");
        Assert.DoesNotContain("Part 1A1", result, "Pass-through levels must not stack extra suffixes");
    }

    [TestMethod]
    public void BuildMarkdown_SingleChildChain_EverySegmentHasBalancedActivations()
    {
        var result = Serializer.BuildMarkdown(SingleChildChainSequence(),
            stackedActivationBars: true, autoNumber: false, maxParticipantsPerDiagram: 0, maxMessagesPerDiagram: 12);

        var blocks = ExtractMermaidBlocks(result);
        Assert.IsGreaterThan(1, blocks.Count);
        foreach (var block in blocks)
        {
            AssertActivationsBalanced(block);
            // Budget + one open/close arrow per chained split frame (Handle, Execute, Run).
            Assert.IsLessThanOrEqualTo(12 + 3, CountArrowLines(block),
                "Every sub-part must stay within the message cap (plus chain-frame overhead):\n" + block);
        }
    }

    // Mermaid 11 throws "Cannot read properties of undefined (reading 'x')" when a diagram
    // contains "activate X" for a participant it never declared and never messaged — it tries
    // to look up the actor's layout position and finds nothing. Continuation sub-parts keep
    // split-frame lifelines alive with bare activates, so every referenced participant must be
    // explicitly declared in every part.
    private static void AssertAllReferencedParticipantsDeclared(string diagram)
    {
        var declared = new HashSet<string>(StringComparer.Ordinal);
        var referenced = new List<(string Id, string Line)>();

        foreach (var raw in diagram.Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith("participant ") || line.StartsWith("actor "))
            {
                var rest = line.Substring(line.IndexOf(' ') + 1);
                int asIdx = rest.IndexOf(" as ", StringComparison.Ordinal);
                declared.Add((asIdx >= 0 ? rest.Substring(0, asIdx) : rest).Trim());
                continue;
            }
            if (line.StartsWith("activate ")) { referenced.Add((line.Substring("activate ".Length).Trim(), line)); continue; }
            if (line.StartsWith("deactivate ")) { referenced.Add((line.Substring("deactivate ".Length).Trim(), line)); continue; }

            int arrowIdx = line.IndexOf("-->>", StringComparison.Ordinal);
            bool dashed = arrowIdx >= 0;
            if (!dashed) arrowIdx = line.IndexOf("->>", StringComparison.Ordinal);
            if (arrowIdx < 0) continue;

            var sender = line.Substring(0, arrowIdx).Trim();
            var receiverPart = line.Substring(arrowIdx + (dashed ? 4 : 3)).TrimStart('+', '-');
            int colon = receiverPart.IndexOf(':');
            var receiver = (colon >= 0 ? receiverPart.Substring(0, colon) : receiverPart).Trim();
            referenced.Add((sender, line));
            referenced.Add((receiver, line));
        }

        foreach (var (id, line) in referenced)
            Assert.Contains(id, declared,
                $"Participant '{id}' is referenced ('{line}') but never declared in:\n{diagram}");
    }

    [TestMethod]
    public void BuildMarkdown_SingleChildChain_EveryPartDeclaresAllReferencedParticipants()
    {
        // Continuation sub-parts keep the chained split frames (Handle, Execute, Run) alive via
        // bare activates on A/B/C/D — all of which must be declared in those parts even though
        // only D and E exchange real messages there.
        var result = Serializer.BuildMarkdown(SingleChildChainSequence(),
            stackedActivationBars: true, autoNumber: false, maxParticipantsPerDiagram: 0, maxMessagesPerDiagram: 12);

        var blocks = ExtractMermaidBlocks(result);
        Assert.IsGreaterThan(1, blocks.Count);
        foreach (var block in blocks)
            AssertAllReferencedParticipantsDeclared(block);
    }

    [TestMethod]
    public void BuildMarkdown_SingleChildChainWithRoot_EveryPartDeclaresAllReferencedParticipants()
    {
        // With the «Caller» wrap, non-first segments also emit "activate A" for the root
        // participant — the root must therefore be declared in every deep sub-part too.
        var chain = SingleChildChainSequence();
        var sequence = new CallSequence
        {
            Title = chain.Title,
            Participants = chain.Participants,
            RootCalls = chain.RootCalls,
            RootParticipantId = "A",
            RootMethodLabel = "Entry",
        };

        var result = Serializer.BuildMarkdown(sequence,
            stackedActivationBars: true, autoNumber: false, maxParticipantsPerDiagram: 0, maxMessagesPerDiagram: 12);

        var blocks = ExtractMermaidBlocks(result);
        Assert.IsGreaterThan(1, blocks.Count);
        foreach (var block in blocks)
        {
            AssertAllReferencedParticipantsDeclared(block);
            AssertActivationsBalanced(block);
        }
    }

    // Greedy segmentation shape: a full first segment (cap-forced boundary), then a mid segment,
    // then a 1-call straggler cut off by the low-overlap seam. The straggler fits the caps when
    // merged into the mid segment, so the tiny-tail merge must reattach it.
    private static CallSequence TinyTailSequence()
    {
        var phase1 = Enumerable.Range(1, 20)
            .Select(i => Leaf("A", "B", $"Call{i}"))
            .ToArray();

        return new CallSequence
        {
            Title = "Root",
            Participants =
            [
                P("A", "A"), P("B", "B"), P("C", "C"), P("D", "D"),
                P("E", "E"), P("F", "F"), P("X", "X"), P("Y", "Y"),
            ],
            RootCalls =
            [
                .. phase1,                                     // 40 arrows — fills the 40 cap exactly
                Nested("C", "D", "P2",
                    Leaf("D", "E", "P2a"), Leaf("D", "F", "P2b")), // 6 arrows, 4 meaningful participants
                Leaf("X", "Y", "Straggler"),                   // 2 arrows, zero overlap → soft seam
            ],
        };
    }

    [TestMethod]
    public void BuildMarkdown_TinyTailWithinCaps_IsMergedIntoNeighbor()
    {
        var result = Serializer.BuildMarkdown(TinyTailSequence(),
            stackedActivationBars: true, autoNumber: false, maxParticipantsPerDiagram: 0, maxMessagesPerDiagram: 40);

        var headingCount = result.Split('\n').Count(l => l.TrimStart().StartsWith("## Part"));
        Assert.AreEqual(2, headingCount,
            "The 2-arrow straggler must be merged back into the neighboring part, leaving 2 parts, not 3.");

        var blocks = ExtractMermaidBlocks(result);
        var stragglerBlock = blocks.Single(b => b.Contains("Straggler"));
        Assert.Contains("P2", stragglerBlock, "Straggler should share a part with the P2 phase it was merged into.");
    }

    [TestMethod]
    public void BuildMarkdown_TinyTail_NotMergedWhenParticipantCapForbids()
    {
        // Main touches A,B,C,D (4 = participant cap); the disjoint 2-arrow Tail would push a
        // merged part to 6 participants, so it must stay separate despite being tiny.
        var sequence = new CallSequence
        {
            Title = "Root",
            Participants =
            [
                P("A", "A"), P("B", "B"), P("C", "C"),
                P("D", "D"), P("X", "X"), P("Y", "Y"),
            ],
            RootCalls =
            [
                Nested("A", "B", "Main",
                    Leaf("B", "C", "s1"), Leaf("B", "D", "s2")),
                Leaf("X", "Y", "Tail"),
            ],
        };

        var result = Serializer.BuildMarkdown(sequence,
            stackedActivationBars: true, autoNumber: false, maxParticipantsPerDiagram: 4, maxMessagesPerDiagram: 40);

        var headingCount = result.Split('\n').Count(l => l.TrimStart().StartsWith("## Part"));
        Assert.AreEqual(2, headingCount, "Merging would exceed the participant cap, so the tiny tail must stay its own part.");
    }
}
