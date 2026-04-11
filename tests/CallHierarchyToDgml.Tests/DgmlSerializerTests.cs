using System.Linq;
using System.Xml.Linq;
using CallHierarchyToDgml.Core;

namespace CallHierarchyToDgml.Tests;

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
        CollectionAssert.AreEquivalent(
            new[] { "Method", "Property", "Event", "Class", "Interface", "Struct", "Enum", "Delegate", "Calls", "Implements/Overrides", "Contains" },
            document.Root!
                .Element(Namespace + "Categories")!
                .Elements(Namespace + "Category")
                .Select(category => (string)category.Attribute("Id")!)
                .ToArray());
    }

    [TestMethod]
    public void Merge_AppendsNodesAndLinksWithoutDuplicates()
    {
        var serializer = new DgmlSerializer();
        var graph = new TraversalGraph();
        graph.UpsertNode(new GraphNode("A", "Alpha", "Method", null, null, "ProjectA"));
        graph.UpsertNode(new GraphNode("B", "Beta", "Property", "C:\\Code\\B.cs", 42, "ProjectB"));
        graph.AddLink(new GraphLink("A", "B", "Calls"));

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
        original.UpsertNode(new GraphNode("A", "Alpha", "Method", null, null, null));
        original.UpsertNode(new GraphNode("B", "Beta", "Method", null, null, null));
        original.AddLink(new GraphLink("A", "B", "Calls"));

        var replacement = new TraversalGraph();
        replacement.UpsertNode(new GraphNode("C", "Gamma", "Event", null, null, null));

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
}
