namespace CallHierarchyToDgml.Core;

public sealed class TraversalOptions
{
    public int MaxDepth { get; init; } = 3;

    public int MaxNodeCount { get; init; } = 250;

    public bool IncludeProperties { get; init; } = true;

    public bool IncludeEvents { get; init; } = true;

    public bool IncludeExternalSymbols { get; init; }

    public bool IncludeGeneratedCode { get; init; }

    public TraversalOptions Normalize()
    {
        return new TraversalOptions
        {
            MaxDepth = MaxDepth < 1 ? 1 : MaxDepth,
            MaxNodeCount = MaxNodeCount < 1 ? 1 : MaxNodeCount,
            IncludeProperties = IncludeProperties,
            IncludeEvents = IncludeEvents,
            IncludeExternalSymbols = IncludeExternalSymbols,
            IncludeGeneratedCode = IncludeGeneratedCode,
        };
    }
}
