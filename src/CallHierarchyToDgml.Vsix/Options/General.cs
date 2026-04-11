using System.ComponentModel;
using System.Runtime.InteropServices;
using CallHierarchyToDgml.Core;

namespace CallHierarchyToDgml.Vsix;

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
    [DefaultValue(3)]
    public int MaxDepth { get; set; } = 3;

    [Category("Traversal")]
    [DisplayName("Maximum node count")]
    [Description("Hard stop for the number of graph nodes. Values less than 1 are clamped to 1.")]
    [DefaultValue(250)]
    public int MaxNodeCount { get; set; } = 250;

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
        }.Normalize();
    }
}
