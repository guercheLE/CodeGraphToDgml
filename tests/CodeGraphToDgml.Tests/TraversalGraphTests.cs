using CodeGraphToDgml.Core;

namespace CodeGraphToDgml.Tests;

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
        graph.UpsertNode(new GraphNode("Project=P1|N:A.B", "A.B", "CodeSchema_Namespace", null, null, null));
        graph.UpsertNode(new GraphNode("Project=P1|N:A.B.C", "A.B.C", "CodeSchema_Namespace", null, null, null));
        graph.UpsertNode(new GraphNode("Project=P1|T:A.B.C.MyClass", "MyClass", "CodeSchema_Class", null, null, null));

        graph.AddLink(new GraphLink("Project=P1", "Project=P1|N:A", "Contains"));
        graph.AddLink(new GraphLink("Project=P1|N:A", "Project=P1|N:A.B", "Contains"));
        graph.AddLink(new GraphLink("Project=P1|N:A.B", "Project=P1|N:A.B.C", "Contains"));
        graph.AddLink(new GraphLink("Project=P1|N:A.B.C", "Project=P1|T:A.B.C.MyClass", "Contains"));

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

    [TestMethod]
    public void FlattenSingleChildNamespaces_PreservesSameNamedTypesInDifferentNamespaces()
    {
        var graph = new TraversalGraph();
        graph.UpsertNode(new GraphNode("Project=P1", "P1", "CodeSchema_Assembly", null, null, "P1"));

        // N1.S1 has both a child namespace (N1.S1.S2) AND a type (N1.S1.A)
        graph.UpsertNode(new GraphNode("Project=P1|N:N1", "N1", "CodeSchema_Namespace", null, null, null));
        graph.UpsertNode(new GraphNode("Project=P1|N:N1.S1", "N1.S1", "CodeSchema_Namespace", null, null, null));
        graph.UpsertNode(new GraphNode("Project=P1|N:N1.S1.S2", "N1.S1.S2", "CodeSchema_Namespace", null, null, null));
        graph.UpsertNode(new GraphNode("Project=P1|N:N1.S1.S2.S3", "N1.S1.S2.S3", "CodeSchema_Namespace", null, null, null));

        // Same-named types in different namespaces (project-scoped IDs)
        graph.UpsertNode(new GraphNode("Project=P1|T:N1.S1.A", "A", "CodeSchema_Class", null, null, null));
        graph.UpsertNode(new GraphNode("Project=P1|T:N1.S1.S2.S3.A", "A", "CodeSchema_Class", null, null, null));
        graph.UpsertNode(new GraphNode("Project=P1|T:N1.S1.B", "B", "CodeSchema_Interface", null, null, null));
        graph.UpsertNode(new GraphNode("Project=P1|T:N1.S1.S2.S3.B", "B", "CodeSchema_Interface", null, null, null));

        // Methods in each type
        graph.UpsertNode(new GraphNode("Project=P1|M:N1.S1.A.Do", "Do", "CodeSchema_Method", null, null, null));
        graph.UpsertNode(new GraphNode("Project=P1|M:N1.S1.S2.S3.A.Do", "Do", "CodeSchema_Method", null, null, null));

        // Containment: Project -> N1 -> N1.S1 -> { N1.S1.S2, A, B }
        graph.AddLink(new GraphLink("Project=P1", "Project=P1|N:N1", "Contains"));
        graph.AddLink(new GraphLink("Project=P1|N:N1", "Project=P1|N:N1.S1", "Contains"));
        graph.AddLink(new GraphLink("Project=P1|N:N1.S1", "Project=P1|N:N1.S1.S2", "Contains"));
        graph.AddLink(new GraphLink("Project=P1|N:N1.S1", "Project=P1|T:N1.S1.A", "Contains"));
        graph.AddLink(new GraphLink("Project=P1|N:N1.S1", "Project=P1|T:N1.S1.B", "Contains"));
        // N1.S1.S2 -> N1.S1.S2.S3 -> { A, B }
        graph.AddLink(new GraphLink("Project=P1|N:N1.S1.S2", "Project=P1|N:N1.S1.S2.S3", "Contains"));
        graph.AddLink(new GraphLink("Project=P1|N:N1.S1.S2.S3", "Project=P1|T:N1.S1.S2.S3.A", "Contains"));
        graph.AddLink(new GraphLink("Project=P1|N:N1.S1.S2.S3", "Project=P1|T:N1.S1.S2.S3.B", "Contains"));
        // Types contain methods
        graph.AddLink(new GraphLink("Project=P1|T:N1.S1.A", "Project=P1|M:N1.S1.A.Do", "Contains"));
        graph.AddLink(new GraphLink("Project=P1|T:N1.S1.S2.S3.A", "Project=P1|M:N1.S1.S2.S3.A.Do", "Contains"));

        graph.FlattenSingleChildNamespaces();

        // N1 had only one child (N1.S1), both namespaces only -> N1 gets flattened into N1.S1
        Assert.IsFalse(graph.ContainsNode("Project=P1|N:N1"), "N1 should be merged into N1.S1");
        // N1.S1 has 3 children (S2 + A + B) -> cannot flatten
        Assert.IsTrue(graph.ContainsNode("Project=P1|N:N1.S1"), "N1.S1 must survive (has namespace + type children)");
        // N1.S1.S2 has 1 namespace child (S3) -> can flatten
        Assert.IsFalse(graph.ContainsNode("Project=P1|N:N1.S1.S2"), "N1.S1.S2 should be merged into N1.S1.S2.S3");
        Assert.IsTrue(graph.ContainsNode("Project=P1|N:N1.S1.S2.S3"), "N1.S1.S2.S3 must survive");

        // Both A types and both B types survive
        Assert.IsTrue(graph.ContainsNode("Project=P1|T:N1.S1.A"), "N1.S1.A must survive");
        Assert.IsTrue(graph.ContainsNode("Project=P1|T:N1.S1.S2.S3.A"), "N1.S1.S2.S3.A must survive");
        Assert.IsTrue(graph.ContainsNode("Project=P1|T:N1.S1.B"), "N1.S1.B must survive");
        Assert.IsTrue(graph.ContainsNode("Project=P1|T:N1.S1.S2.S3.B"), "N1.S1.S2.S3.B must survive");

        // After flattening, N1.S1's label should be its full name
        Assert.AreEqual("N1.S1", graph.Nodes.Single(n => n.Id == "Project=P1|N:N1.S1").Label);
        Assert.AreEqual("N1.S1.S2.S3", graph.Nodes.Single(n => n.Id == "Project=P1|N:N1.S1.S2.S3").Label);
    }

    [TestMethod]
    public void UsedByLinks_SurviveGraphConstructionAndSerialization()
    {
        var graph = new TraversalGraph();
        graph.UpsertNode(new GraphNode("Project=P1|T:App.MainForm", "MainForm", "CodeSchema_Class", null, null, "P1"));
        graph.UpsertNode(new GraphNode("Project=P1|T:App.MyUserControl", "MyUserControl", "CodeSchema_Class", null, null, "P1"));
        graph.AddLink(new GraphLink("Project=P1|T:App.MainForm", "Project=P1|T:App.MyUserControl", "UsedBy"));

        Assert.AreEqual(2, graph.NodeCount);
        Assert.AreEqual(1, graph.LinkCount);

        var serializer = new DgmlSerializer();
        var dgml = serializer.Merge(null, graph, replaceContents: false);
        var document = System.Xml.Linq.XDocument.Parse(dgml);
        var ns = System.Xml.Linq.XNamespace.Get("http://schemas.microsoft.com/vs/2009/dgml");

        var usedByLink = document.Root!
            .Element(ns + "Links")!
            .Elements(ns + "Link")
            .SingleOrDefault(l =>
                (string?)l.Attribute("Source") == "Project=P1|T:App.MainForm"
                && (string?)l.Attribute("Target") == "Project=P1|T:App.MyUserControl"
                && (string?)l.Attribute("Category") == "UsedBy");

        Assert.IsNotNull(usedByLink, "UsedBy link should be present in serialized DGML");
    }

    [TestMethod]
    public void Normalize_ClampsMaxHostDepthToOne()
    {
        var options = new TraversalOptions
        {
            MaxHostDepth = 0,
        };

        var normalized = options.Normalize();

        Assert.AreEqual(1, normalized.MaxHostDepth);
    }
}