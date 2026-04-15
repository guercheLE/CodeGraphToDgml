namespace CodeGraphToDgml.Core;

public sealed record TraversalProgress(
    string Stage,
    int Depth,
    int NodeCount,
    string? CurrentSymbol);
