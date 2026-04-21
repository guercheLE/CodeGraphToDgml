using System.Collections.Generic;
using System.Linq;

namespace CodeGraphToDgml.Core;

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

public sealed record CategoryDefinition(
    string Id,
    string Label,
    string? BasedOn = null,
    string? Icon = null,
    string? DefaultAction = null,
    string? NavigationActionLabel = null,
    string? IncomingActionLabel = null,
    string? OutgoingActionLabel = null,
    bool? CanBeDataDriven = null,
    bool? CanLinkedNodesBeDataDriven = null,
    bool? IsContainment = null,
    string? Description = null);

public sealed record PropertyDefinition(
    string Id,
    string? Label = null,
    string? Description = null,
    string DataType = "System.String");

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

    public bool RemoveNode(string id)
    {
        return _nodes.Remove(id);
    }

    public bool RemoveLink(GraphLink link)
    {
        return _links.Remove(link);
    }

    public void FlattenSingleChildNamespaces()
    {
        while (TryFlattenSingleNamespace())
        {
        }
    }

    private bool TryFlattenSingleNamespace()
    {
        foreach (var node in _nodes.Values)
        {
            if (node.Kind != "CodeSchema_Namespace")
            {
                continue;
            }

            var childLinks = _links
                .Where(l => l.SourceId == node.Id && l.Category == "Contains")
                .ToList();

            if (childLinks.Count != 1)
            {
                continue;
            }

            var onlyChildId = childLinks[0].TargetId;
            if (!_nodes.TryGetValue(onlyChildId, out var childNode)
                || childNode.Kind != "CodeSchema_Namespace")
            {
                continue;
            }

            var incomingLinks = _links
                .Where(l => l.TargetId == node.Id)
                .ToList();

            foreach (var incoming in incomingLinks)
            {
                _links.Remove(incoming);
                _links.Add(new GraphLink(incoming.SourceId, onlyChildId, incoming.Category));
            }

            _links.Remove(childLinks[0]);
            _nodes.Remove(node.Id);
            return true;
        }

        return false;
    }
}
