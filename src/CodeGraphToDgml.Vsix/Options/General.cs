using System.ComponentModel;
using System.Runtime.InteropServices;
using CodeGraphToDgml.Core;

namespace CodeGraphToDgml.Vsix;

public enum SequenceDiagramOutputFormat
{
    Markdown,
    Html,
    Both,
}

public enum DgmlDocumentOpenBehavior
{
    AlwaysAsk,
    AlwaysCreateNewTemporary,
    ReuseActiveIfOpen,
}

public enum GraphUpdateMode
{
    Append,
    Replace,
}

internal partial class OptionsProvider
{
    [ComVisible(true)]
    public class GeneralOptions : BaseOptionPage<General>
    {
    }
}

internal class General : BaseOptionModel<General>
{
    [Category("Traversal")]
    [DisplayName("Maximum depth")]
    [Description("Maximum caller traversal depth. Values less than 1 are clamped to 1.")]
    [DefaultValue(16)]
    public int MaxDepth { get; set; } = 16;

    [Category("Traversal")]
    [DisplayName("Maximum node count")]
    [Description("Hard stop for the number of graph nodes. Values less than 1 are clamped to 1.")]
    [DefaultValue(1024)]
    public int MaxNodeCount { get; set; } = 1024;

    [Category("Traversal")]
    [DisplayName("Include properties")]
    [Description("When enabled, property symbols are included during upward traversal.")]
    [DefaultValue(true)]
    public bool IncludeProperties { get; set; } = true;

    [Category("Traversal")]
    [DisplayName("Include events")]
    [Description("When enabled, event symbols are included during upward traversal.")]
    [DefaultValue(true)]
    public bool IncludeEvents { get; set; } = true;

    [Category("Traversal")]
    [DisplayName("Include external symbols")]
    [Description("When enabled, symbols from referenced assemblies can appear in the graph.")]
    [DefaultValue(false)]
    public bool IncludeExternalSymbols { get; set; }

    [Category("Traversal")]
    [DisplayName("Include generated code")]
    [Description("When enabled, symbols from generated files such as .g.cs and Designer files can appear in the graph.")]
    [DefaultValue(false)]
    public bool IncludeGeneratedCode { get; set; }

    [Category("Traversal")]
    [DisplayName("Include component hosts")]
    [Description("When enabled, forms, pages, and windows that host a UI component are discovered and added to the graph with UsedBy links.")]
    [DefaultValue(true)]
    public bool IncludeComponentHosts { get; set; } = true;

    [Category("Traversal")]
    [DisplayName("Maximum host depth")]
    [Description("Maximum levels of nested component hosting to discover. For example, a UserControl inside another UserControl inside a Form requires depth 2. Values less than 1 are clamped to 1.")]
    [DefaultValue(3)]
    public int MaxHostDepth { get; set; } = 3;

    [Category("DGML")]
    [DisplayName("Graph direction")]
    [Description("Controls the layout direction of the generated DGML graph.")]
    [DefaultValue(GraphDirection.TopToBottom)]
    [TypeConverter(typeof(EnumConverter))]
    public GraphDirection GraphDirection { get; set; } = GraphDirection.TopToBottom;

    [Category("DGML")]
    [DisplayName("Collapse groups")]
    [Description("When enabled, namespace, class, and other container nodes start collapsed in the DGML graph.")]
    [DefaultValue(false)]
    public bool CollapseGroups { get; set; }

    [Category("DGML")]
    [DisplayName("Target document behavior")]
    [Description("Controls how the command chooses the DGML document to update.")]
    [DefaultValue(DgmlDocumentOpenBehavior.AlwaysAsk)]
    [TypeConverter(typeof(EnumConverter))]
    public DgmlDocumentOpenBehavior OpenBehavior { get; set; } = DgmlDocumentOpenBehavior.AlwaysAsk;

    [Category("DGML")]
    [DisplayName("Graph update mode")]
    [Description("Append merges nodes and links into the existing DGML document. Replace clears the existing graph before writing the new result.")]
    [DefaultValue(GraphUpdateMode.Append)]
    [TypeConverter(typeof(EnumConverter))]
    public GraphUpdateMode UpdateMode { get; set; } = GraphUpdateMode.Append;

    [Category("Sequence Diagram")]
    [DisplayName("Output format")]
    [Description("Controls which file format(s) are generated when using Traverse Down to Sequence. Markdown can be previewed directly in Visual Studio; Html opens in the default browser with zoom and scroll support.")]
    [DefaultValue(SequenceDiagramOutputFormat.Markdown)]
    [TypeConverter(typeof(EnumConverter))]
    public SequenceDiagramOutputFormat SequenceDiagramOutputFormat { get; set; } = SequenceDiagramOutputFormat.Markdown;

    [Category("Sequence Diagram")]
    [DisplayName("Stacked activation bars")]
    [Description("When enabled, nested calls show stacked activation bars on lifelines (using the +/- Mermaid notation). Recommended off for Markdown, since older bundled Mermaid versions in editor extensions may not render them correctly. Enable when targeting Html or Both, or when your Markdown preview bundles Mermaid v11 or later.")]
    [DefaultValue(false)]
    public bool SequenceDiagramStackedActivationBars { get; set; }

    [Category("Sequence Diagram")]
    [DisplayName("Auto-number calls")]
    [Description("When enabled, adds the autonumber directive to the generated Mermaid sequence diagram, annotating each message arrow with a sequential call number.")]
    [DefaultValue(false)]
    public bool SequenceDiagramAutoNumber { get; set; }

    [Category("Sequence Diagram")]
    [DisplayName("Max participants per diagram")]
    [Description("When greater than zero, large diagrams are split into multiple sections by depth level, each containing at most this many participants. Set to 0 to disable splitting.")]
    [DefaultValue(50)]
    public int SequenceDiagramMaxParticipantsPerDiagram { get; set; } = 50;

    [Category("UI")]
    [DisplayName("Activate result document")]
    [Description("When enabled, the DGML document is brought to the front after it is updated.")]
    [DefaultValue(true)]
    public bool ActivateDgmlWindow { get; set; } = true;

    [Category("UI")]
    [DisplayName("Show detailed output")]
    [Description("When enabled, the command writes detailed progress and target information to the dedicated Output window pane.")]
    [DefaultValue(true)]
    public bool ShowDetailedOutput { get; set; } = true;

    public TraversalOptions ToTraversalOptions()
    {
        return new TraversalOptions
        {
            MaxDepth = MaxDepth,
            MaxNodeCount = MaxNodeCount,
            IncludeProperties = IncludeProperties,
            IncludeEvents = IncludeEvents,
            IncludeExternalSymbols = IncludeExternalSymbols,
            IncludeGeneratedCode = IncludeGeneratedCode,
            IncludeComponentHosts = IncludeComponentHosts,
            MaxHostDepth = MaxHostDepth,
            CollapseGroups = CollapseGroups,
            GraphDirection = GraphDirection,
        }.Normalize();
    }
}
