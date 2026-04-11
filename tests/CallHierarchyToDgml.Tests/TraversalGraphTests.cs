using CallHierarchyToDgml.Core;

namespace CallHierarchyToDgml.Tests;

[TestClass]
public sealed class TraversalGraphTests
{
    [TestMethod]
    public void UpsertNode_ReplacesExistingNodeUsingOrdinalIdComparison()
    {
        var graph = new TraversalGraph();
        graph.UpsertNode(new GraphNode("Node", "First", "Method", null, null, null));
        graph.UpsertNode(new GraphNode("Node", "Second", "Property", null, 12, "Project"));
        graph.UpsertNode(new GraphNode("node", "Third", "Method", null, null, null));

        Assert.AreEqual(2, graph.NodeCount);
        CollectionAssert.AreEquivalent(
            new[] { "Second", "Third" },
            graph.Nodes.Select(node => node.Label).ToArray());
    }

    [TestMethod]
    public void AddLink_UsesSetSemantics()
    {
        var graph = new TraversalGraph();
        graph.AddLink(new GraphLink("A", "B", "Calls"));
        graph.AddLink(new GraphLink("A", "B", "Calls"));
        graph.AddLink(new GraphLink("a", "B", "Calls"));

        Assert.AreEqual(2, graph.LinkCount);
    }

    [TestMethod]
    public void Normalize_ClampsTraversalLimitsToOne()
    {
        var options = new TraversalOptions
        {
            MaxDepth = 0,
            MaxNodeCount = -5,
        };

        var normalized = options.Normalize();

        Assert.AreEqual(1, normalized.MaxDepth);
        Assert.AreEqual(1, normalized.MaxNodeCount);
    }
}