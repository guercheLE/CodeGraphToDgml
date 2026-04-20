using System.Linq;
using System.Xml.Linq;
using CodeGraphToDgml.Core;

namespace CodeGraphToDgml.Tests;

[TestClass]
public sealed class DgmlSerializerTests
{
    private static readonly XNamespace Namespace = "http://schemas.microsoft.com/vs/2009/dgml";

    [TestMethod]
    public void CreateEmptyText_CreatesStandaloneTopToBottomDocument()
    {
        var text = DgmlSerializer.CreateEmptyText();
        var document = XDocument.Parse(text, LoadOptions.PreserveWhitespace);

        Assert.AreEqual("utf-8", document.Declaration?.Encoding);
        Assert.AreEqual("yes", document.Declaration?.Standalone);
        Assert.AreEqual("TopToBottom", (string?)document.Root?.Attribute("GraphDirection"));
        var expectedCategories = new[]
        {
            "CodeSchema_Assembly", "CodeSchema_Namespace", "CodeSchema_Class", "CodeSchema_Interface",
            "CodeSchema_Struct", "CodeSchema_Enum", "CodeSchema_Delegate", "CodeSchema_Type",
            "CodeSchema_Method", "CodeSchema_Property", "CodeSchema_Event", "CodeSchema_Field",
            "CodeSchema_Calls", "CodeSchema_FunctionPointer",
            "Implements", "Contains", "References", "InheritsFrom", "UsedBy", "Externals"
        };
        CollectionAssert.AreEquivalent(
            expectedCategories,
            document.Root!
                .Element(Namespace + "Categories")!
                .Elements(Namespace + "Category")
                .Select(category => (string)category.Attribute("Id")!)
                .ToArray());

        // Verify Properties section exists with expected entries
        var properties = document.Root!
            .Element(Namespace + "Properties")!
            .Elements(Namespace + "Property")
            .Select(p => (string)p.Attribute("Id")!)
            .ToArray();
        Assert.IsNotEmpty(properties, "Properties section should not be empty");
        CollectionAssert.Contains(properties, "FilePath");
        CollectionAssert.Contains(properties, "Label");
        CollectionAssert.Contains(properties, "Group");

        // Verify Styles section has both node and link styles
        var styles = document.Root!
            .Element(Namespace + "Styles")!
            .Elements(Namespace + "Style")
            .ToArray();
        Assert.IsTrue(styles.Any(s => (string?)s.Attribute("TargetType") == "Node"), "Should have node styles");
        Assert.IsTrue(styles.Any(s => (string?)s.Attribute("TargetType") == "Link"), "Should have link styles");
    }

    [TestMethod]
    public void Merge_AppendsNodesAndLinksWithoutDuplicates()
    {
        var serializer = new DgmlSerializer();
        var graph = new TraversalGraph();
        graph.UpsertNode(new GraphNode("A", "Alpha", "CodeSchema_Method", null, null, "ProjectA"));
        graph.UpsertNode(new GraphNode("B", "Beta", "CodeSchema_Property", "C:\\Code\\B.cs", 42, "ProjectB"));
        graph.AddLink(new GraphLink("A", "B", "CodeSchema_Calls"));

        var first = serializer.Merge(null, graph, replaceContents: false);
        var second = serializer.Merge(first, graph, replaceContents: false);
        var document = XDocument.Parse(second, LoadOptions.PreserveWhitespace);

        Assert.AreEqual(2, document.Root!.Element(Namespace + "Nodes")!.Elements(Namespace + "Node").Count());
        Assert.AreEqual(1, document.Root!.Element(Namespace + "Links")!.Elements(Namespace + "Link").Count());

        var propertyNode = document.Root!
            .Element(Namespace + "Nodes")!
            .Elements(Namespace + "Node")
            .Single(node => (string?)node.Attribute("Id") == "B");

        Assert.AreEqual("C:\\Code\\B.cs", (string?)propertyNode.Attribute("FilePath"));
        Assert.AreEqual("42", (string?)propertyNode.Attribute("Line"));
        // ProjectB mapping was removed as we group by real nodes instead of "Group" attr
    }

    [TestMethod]
    public void Merge_ReplaceContentsClearsPreviousNodesAndLinks()
    {
        var serializer = new DgmlSerializer();
        var original = new TraversalGraph();
        original.UpsertNode(new GraphNode("A", "Alpha", "CodeSchema_Method", null, null, null));
        original.UpsertNode(new GraphNode("B", "Beta", "CodeSchema_Method", null, null, null));
        original.AddLink(new GraphLink("A", "B", "CodeSchema_Calls"));

        var replacement = new TraversalGraph();
        replacement.UpsertNode(new GraphNode("C", "Gamma", "CodeSchema_Event", null, null, null));

        var merged = serializer.Merge(serializer.Merge(null, original, replaceContents: false), replacement, replaceContents: true);
        var document = XDocument.Parse(merged, LoadOptions.PreserveWhitespace);

        CollectionAssert.AreEqual(
            new[] { "C" },
            document.Root!
                .Element(Namespace + "Nodes")!
                .Elements(Namespace + "Node")
                .Select(node => (string)node.Attribute("Id")!)
                .ToArray());
        Assert.AreEqual(0, document.Root!.Element(Namespace + "Links")!.Elements(Namespace + "Link").Count());
    }

    [TestMethod]
    public void Merge_ContainerNodesAreExpandedByDefault()
    {
        var serializer = new DgmlSerializer();
        var graph = new TraversalGraph();
        graph.UpsertNode(new GraphNode("NS", "MyNamespace", "CodeSchema_Namespace", null, null, null));
        graph.UpsertNode(new GraphNode("CLS", "MyClass", "CodeSchema_Class", null, null, null));
        graph.AddLink(new GraphLink("NS", "CLS", "Contains"));

        var result = serializer.Merge(null, graph, replaceContents: false);
        var document = XDocument.Parse(result, LoadOptions.PreserveWhitespace);

        var nsNode = document.Root!
            .Element(Namespace + "Nodes")!
            .Elements(Namespace + "Node")
            .Single(n => (string?)n.Attribute("Id") == "NS");

        var clsNode = document.Root!
            .Element(Namespace + "Nodes")!
            .Elements(Namespace + "Node")
            .Single(n => (string?)n.Attribute("Id") == "CLS");

        Assert.AreEqual("Expanded", (string?)nsNode.Attribute("Group"));
        Assert.AreEqual("Expanded", (string?)clsNode.Attribute("Group"));
    }

    [TestMethod]
    public void Merge_ContainerNodesAreCollapsedWhenOptionSet()
    {
        var serializer = new DgmlSerializer();
        var graph = new TraversalGraph();
        graph.UpsertNode(new GraphNode("NS", "MyNamespace", "CodeSchema_Namespace", null, null, null));
        graph.UpsertNode(new GraphNode("CLS", "MyClass", "CodeSchema_Class", null, null, null));
        graph.AddLink(new GraphLink("NS", "CLS", "Contains"));

        var result = serializer.Merge(null, graph, replaceContents: false, collapseGroups: true);
        var document = XDocument.Parse(result, LoadOptions.PreserveWhitespace);

        var nsNode = document.Root!
            .Element(Namespace + "Nodes")!
            .Elements(Namespace + "Node")
            .Single(n => (string?)n.Attribute("Id") == "NS");

        var clsNode = document.Root!
            .Element(Namespace + "Nodes")!
            .Elements(Namespace + "Node")
            .Single(n => (string?)n.Attribute("Id") == "CLS");

        Assert.AreEqual("Collapsed", (string?)nsNode.Attribute("Group"));
        Assert.AreEqual("Collapsed", (string?)clsNode.Attribute("Group"));
    }

    [TestMethod]
    public void CreateEmptyText_ContainsUsedByLinkStyle()
    {
        var text = DgmlSerializer.CreateEmptyText();
        var document = XDocument.Parse(text, LoadOptions.PreserveWhitespace);

        var usedByStyle = document.Root!
            .Element(Namespace + "Styles")!
            .Elements(Namespace + "Style")
            .SingleOrDefault(s =>
                (string?)s.Attribute("TargetType") == "Link"
                && (string?)s.Attribute("GroupLabel") == "Used By");

        Assert.IsNotNull(usedByStyle, "UsedBy link style should be present");

        var strokeSetter = usedByStyle.Elements(Namespace + "Setter")
            .SingleOrDefault(s => (string?)s.Attribute("Property") == "Stroke");
        Assert.AreEqual("#FFFF8C00", (string?)strokeSetter?.Attribute("Value"), "UsedBy stroke should be orange");

        var drawArrowSetter = usedByStyle.Elements(Namespace + "Setter")
            .SingleOrDefault(s => (string?)s.Attribute("Property") == "DrawArrow");
        Assert.AreEqual("true", (string?)drawArrowSetter?.Attribute("Value"), "UsedBy should draw arrows");
    }
}
