using System.Collections.Generic;
using System.Text;

namespace CodeGraphToDgml.Core;

/// <summary>
/// Renders a <see cref="CallSequence"/> as Mermaid sequence-diagram source, Markdown, or HTML,
/// splitting it into several linked diagrams when it exceeds the participant or message caps.
/// </summary>
/// <remarks>
/// The work is split across internal collaborators:
/// <c>SequenceSegmentPlanBuilder</c> builds the segment model,
/// <c>SequenceSegmenter</c> decides where to cut (layout),
/// <c>MermaidSequenceEmitter</c> writes the Mermaid text, and
/// <c>SequenceHtmlRenderer</c> wraps diagrams in an HTML page.
/// </remarks>
public sealed class MermaidSequenceSerializer
{
    public string Serialize(CallSequence sequence, bool stackedActivationBars = true, bool autoNumber = false)
        => MermaidSequenceEmitter.EmitDiagram(sequence, stackedActivationBars, autoNumber);

    public string BuildMarkdown(CallSequence sequence, bool stackedActivationBars = true, bool autoNumber = false, int maxParticipantsPerDiagram = 0, int maxMessagesPerDiagram = 0)
    {
        var sections = BuildSections(sequence, stackedActivationBars, autoNumber, maxParticipantsPerDiagram, maxMessagesPerDiagram);

        var sb = new StringBuilder();
        sb.Append("# Sequence: ").AppendLine(sequence.Title);
        sb.AppendLine();

        if (sections.Count == 1)
        {
            sb.AppendLine("```mermaid");
            sb.AppendLine(sections[0].Content);
            sb.AppendLine("```");
        }
        else
        {
            for (int i = 0; i < sections.Count; i++)
            {
                if (i > 0) sb.AppendLine();
                sb.Append("## ").AppendLine(sections[i].Heading);
                sb.AppendLine();
                sb.AppendLine("```mermaid");
                sb.AppendLine(sections[i].Content);
                sb.AppendLine("```");
            }
        }

        return sb.ToString();
    }

    public string BuildHtml(CallSequence sequence, bool stackedActivationBars = true, bool autoNumber = false, int maxParticipantsPerDiagram = 0, int maxMessagesPerDiagram = 0)
    {
        var sections = BuildSections(sequence, stackedActivationBars, autoNumber, maxParticipantsPerDiagram, maxMessagesPerDiagram);
        return SequenceHtmlRenderer.Build(sequence.Title, sections);
    }

    private static List<DiagramSection> BuildSections(CallSequence sequence, bool stackedBars, bool autoNumber, int maxPerDiagram, int maxMessagesPerDiagram = 0)
    {
        if (maxPerDiagram <= 0 && maxMessagesPerDiagram <= 0)
            return [new DiagramSection("", MermaidSequenceEmitter.EmitDiagram(sequence, stackedBars, autoNumber))];

        if (sequence.RootCalls.Count == 0)
            return [new DiagramSection("", MermaidSequenceEmitter.EmitDiagram(sequence, stackedBars, autoNumber))];

        var maxParticipants = maxPerDiagram > 0 ? maxPerDiagram : int.MaxValue;
        var maxMessages = maxMessagesPerDiagram > 0 ? maxMessagesPerDiagram : int.MaxValue;

        bool participantsFit = sequence.Participants.Count <= maxParticipants;
        bool messagesFit = SequenceSegmenter.CountArrows(sequence.RootCalls, stackedBars) <= maxMessages;
        if (participantsFit && messagesFit)
            return [new DiagramSection("", MermaidSequenceEmitter.EmitDiagram(sequence, stackedBars, autoNumber))];

        var plans = SequenceSegmentPlanBuilder.Build(sequence, maxParticipants, maxMessages, stackedBars);

        if (plans.Count <= 1)
            return [new DiagramSection("", MermaidSequenceEmitter.EmitDiagram(sequence, stackedBars, autoNumber))];

        var sections = new List<DiagramSection>(plans.Count);
        foreach (var plan in plans)
        {
            var content = MermaidSequenceEmitter.EmitSegment(sequence, plan, stackedBars, autoNumber);
            var heading = "Part " + plan.PartNumber + " — " + plan.Title;
            sections.Add(new DiagramSection(heading, content, plan));
        }

        return sections;
    }
}
