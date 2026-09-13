namespace CodeGraphToDgml.Core;

public sealed record TraversalOptions
{
    public int MaxDepth { get; init; } = 16;

    public int MaxNodeCount { get; init; } = 1024;

    public bool IncludeProperties { get; init; } = true;

    public bool IncludeEvents { get; init; } = true;

    public bool IncludeConstructors { get; init; } = true;

    public bool IncludeExternalSymbols { get; init; }

    public bool IncludeGeneratedCode { get; init; }

    public bool IncludeComponentHosts { get; init; } = true;

    public int MaxHostDepth { get; init; } = 3;

    public bool CollapseGroups { get; init; }

    public GraphDirection GraphDirection { get; init; } = GraphDirection.TopToBottom;

    /// <summary>
    /// Sequence diagrams only: wrap calls in Mermaid <c>alt</c>/<c>opt</c>/<c>loop</c>/<c>break</c>
    /// fragments derived from the enclosing if/switch/loop/catch statements, and render interface
    /// or virtual dispatch as one <c>alt</c> branch per implementation.
    /// </summary>
    public bool IncludeControlFlow { get; init; }

    /// <summary>Maximum length of a fragment label (condition text) before it is truncated with an ellipsis.</summary>
    public int MaxConditionLabelLength { get; init; } = 40;

    public TraversalOptions Normalize()
    {
        return this with
        {
            MaxDepth = MaxDepth < 1 ? 1 : MaxDepth,
            MaxNodeCount = MaxNodeCount < 1 ? 1 : MaxNodeCount,
            MaxHostDepth = MaxHostDepth < 1 ? 1 : MaxHostDepth,
            MaxConditionLabelLength = MaxConditionLabelLength < 10 ? 10 : MaxConditionLabelLength,
        };
    }
}
