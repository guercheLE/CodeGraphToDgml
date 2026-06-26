namespace CodeGraphToDgml.Core;

public sealed record TraversalOptions
{
    public int MaxDepth { get; init; } = 16;

    public int MaxNodeCount { get; init; } = 1024;

    public bool IncludeProperties { get; init; } = true;

    public bool IncludeEvents { get; init; } = true;

    public bool IncludeExternalSymbols { get; init; }

    public bool IncludeGeneratedCode { get; init; }

    public bool IncludeComponentHosts { get; init; } = true;

    public int MaxHostDepth { get; init; } = 3;

    public bool CollapseGroups { get; init; }

    public GraphDirection GraphDirection { get; init; } = GraphDirection.TopToBottom;

    public TraversalOptions Normalize()
    {
        return this with
        {
            MaxDepth = MaxDepth < 1 ? 1 : MaxDepth,
            MaxNodeCount = MaxNodeCount < 1 ? 1 : MaxNodeCount,
            MaxHostDepth = MaxHostDepth < 1 ? 1 : MaxHostDepth
        };
    }
}
