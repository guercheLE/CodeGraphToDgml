using CallHierarchyToDgml.Core;

namespace CallHierarchyToDgml.Tests;

[TestClass]
public sealed class TraversalGraphTests
{
    [TestMethod]
    public void UpsertNode_ReplacesExistingNodeUsingOrdinalIdComparison()
    {
        var graph = new TraversalGraph();
        graph.UpsertNode(new GraphNode("Node", "First", "CodeSchema_Method", null, null, null));
        graph.UpsertNode(new GraphNode("Node", "Second", "CodeSchema_Property", null, 12, "Project"));
        graph.UpsertNode(new GraphNode("node", "Third", "CodeSchema_Method", null, null, null));

        Assert.AreEqual(2, graph.NodeCount);
        CollectionAssert.AreEquivalent(
            new[] { "Second", "Third" },
            graph.Nodes.Select(node => node.Label).ToArray());
    }

    [TestMethod]
    public void AddLink_UsesSetSemantics()
    {
        var graph = new TraversalGraph();
        graph.AddLink(new GraphLink("A", "B", "CodeSchema_Calls"));
        graph.AddLink(new GraphLink("A", "B", "CodeSchema_Calls"));
        graph.AddLink(new GraphLink("a", "B", "CodeSchema_Calls"));

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

    [TestMethod]
    public void FlattenSingleChildNamespaces_MergesChainedNamespaces()
    {
        var graph = new TraversalGraph();
        graph.UpsertNode(new GraphNode("Project=P1", "P1", "CodeSchema_Assembly", null, null, "P1"));
        graph.UpsertNode(new GraphNode("Project=P1|N:A", "A", "CodeSchema_Namespace", null, null, null));
        graph.UpsertNode(new GraphNode("Project=P1|N:A.B", "B", "CodeSchema_Namespace", null, null, null));
        graph.UpsertNode(new GraphNode("Project=P1|N:A.B.C", "C", "CodeSchema_Namespace", null, null, null));
        graph.UpsertNode(new GraphNode("T:A.B.C.MyClass", "MyClass", "CodeSchema_Class", null, null, null));

        graph.AddLink(new GraphLink("Project=P1", "Project=P1|N:A", "Contains"));
        graph.AddLink(new GraphLink("Project=P1|N:A", "Project=P1|N:A.B", "Contains"));
        graph.AddLink(new GraphLink("Project=P1|N:A.B", "Project=P1|N:A.B.C", "Contains"));
        graph.AddLink(new GraphLink("Project=P1|N:A.B.C", "T:A.B.C.MyClass", "Contains"));

        graph.FlattenSingleChildNamespaces();

        Assert.IsFalse(graph.ContainsNode("Project=P1|N:A"), "A should be removed");
        Assert.IsFalse(graph.ContainsNode("Project=P1|N:A.B"), "A.B should be removed");
        Assert.IsTrue(graph.ContainsNode("Project=P1|N:A.B.C"), "A.B.C should remain");

        var remaining = graph.Nodes.Single(n => n.Id == "Project=P1|N:A.B.C");
        Assert.AreEqual("A.B.C", remaining.Label);

        Assert.IsTrue(graph.Links.Any(l => l.SourceId == "Project=P1" && l.TargetId == "Project=P1|N:A.B.C" && l.Category == "Contains"));
    }

    [TestMethod]
    public void FlattenSingleChildNamespaces_PreservesNamespaceWithMultipleChildren()
    {
        var graph = new TraversalGraph();
        graph.UpsertNode(new GraphNode("N:Root", "Root", "CodeSchema_Namespace", null, null, null));
        graph.UpsertNode(new GraphNode("N:Root.Child1", "Child1", "CodeSchema_Namespace", null, null, null));
        graph.UpsertNode(new GraphNode("N:Root.Child2", "Child2", "CodeSchema_Namespace", null, null, null));

        graph.AddLink(new GraphLink("N:Root", "N:Root.Child1", "Contains"));
        graph.AddLink(new GraphLink("N:Root", "N:Root.Child2", "Contains"));

        graph.FlattenSingleChildNamespaces();

        Assert.IsTrue(graph.ContainsNode("N:Root"));
        Assert.IsTrue(graph.ContainsNode("N:Root.Child1"));
        Assert.IsTrue(graph.ContainsNode("N:Root.Child2"));
        Assert.AreEqual("Root", graph.Nodes.Single(n => n.Id == "N:Root").Label);
    }

    [TestMethod]
    public void FlattenSingleChildNamespaces_StopsAtNonNamespaceChild()
    {
        var graph = new TraversalGraph();
        graph.UpsertNode(new GraphNode("N:NS", "NS", "CodeSchema_Namespace", null, null, null));
        graph.UpsertNode(new GraphNode("T:NS.MyClass", "MyClass", "CodeSchema_Class", null, null, null));

        graph.AddLink(new GraphLink("N:NS", "T:NS.MyClass", "Contains"));

        graph.FlattenSingleChildNamespaces();

        Assert.IsTrue(graph.ContainsNode("N:NS"), "Namespace with a type child should not be flattened");
        Assert.AreEqual("NS", graph.Nodes.Single(n => n.Id == "N:NS").Label);
    }
}