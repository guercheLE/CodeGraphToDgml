using System.Collections.Generic;

namespace CallHierarchyToDgml.Core;

public sealed record GraphNode(
    string Id,
    string Label,
    string Kind,
    string? FilePath,
    int? Line,
    string? ProjectName);

public sealed record GraphLink(
    string SourceId,
    string TargetId,
    string Category);

public sealed class TraversalGraph
{
    private readonly Dictionary<string, GraphNode> _nodes = new(StringComparer.Ordinal);
    private readonly HashSet<GraphLink> _links = new();

    public IReadOnlyCollection<GraphNode> Nodes => _nodes.Values;

    public IReadOnlyCollection<GraphLink> Links => _links;

    public int NodeCount => _nodes.Count;

    public int LinkCount => _links.Count;

    public void UpsertNode(GraphNode node)
    {
        _nodes[node.Id] = node;
    }

    public void AddLink(GraphLink link)
    {
        _links.Add(link);
    }

    public bool ContainsNode(string id)
    {
        return _nodes.ContainsKey(id);
    }
}
