using System.Collections.Generic;
using CodeGraphToDgml.Core;
using CodeGraphToDgml.Roslyn;
using Microsoft.CodeAnalysis;
using RoslynSolution = Microsoft.CodeAnalysis.Solution;
using static CodeGraphToDgml.Vsix.Providers.RoslynGraphHelpers;

namespace CodeGraphToDgml.Vsix.Providers;

/// <summary>
/// Builds the <see cref="CallSequence"/> for Traverse Down to Sequence Diagram. One instance
/// serves one traversal: it owns the participant table, the visited set, and the node budget
/// shared by every level of the recursion. Two kinds of call site are followed, each in its own
/// part: <c>.Callees.cs</c> (one node per distinct callee, fluent chains nested under their
/// receiver) and <c>.CallSites.cs</c> (one node per call site, with control-flow fragments).
/// </summary>
internal sealed partial class CallSequenceBuilder
{
    private readonly RoslynSolution _solution;
    private readonly TraversalOptions _options;
    private readonly IProgress<TraversalProgress>? _progress;
    private readonly CancellationToken _cancellationToken;
    private readonly ParticipantTable _participants = new();
    private readonly HashSet<string> _visited = new(StringComparer.Ordinal);
    private int _nodeCount;

    /// <param name="options">Already normalized options.</param>
    public CallSequenceBuilder(
        RoslynSolution solution,
        TraversalOptions options,
        IProgress<TraversalProgress>? progress,
        CancellationToken cancellationToken)
    {
        _solution = solution;
        _options = options;
        _progress = progress;
        _cancellationToken = cancellationToken;
    }

    public async Task<CallSequence> BuildAsync(ISymbol subjectSymbol, string title)
    {
        var rootSymbol = CallGraphSyntaxWalker.NormalizeSymbol(subjectSymbol)!;
        var rootParticipantId = GetParticipantId(rootSymbol, _participants, callerParticipantId: null);

        var rootCalls = _options.IncludeControlFlow
            ? await BuildFlowCallsAsync(rootSymbol, rootParticipantId, 0).ConfigureAwait(false)
            : await BuildCallsAsync(rootSymbol, rootParticipantId, 0).ConfigureAwait(false);

        return new CallSequence
        {
            Title = title,
            Participants = _participants.Participants,
            RootCalls = rootCalls,
            RootParticipantId = rootParticipantId,
            RootMethodLabel = GetNodeLabel(rootSymbol),
            RootReturnTypeLabel = GetReturnTypeLabel(rootSymbol),
        };
    }

    private static string GetParticipantId(ISymbol symbol, ParticipantTable table, string? callerParticipantId)
    {
        // Item 4: well-known framework "outcome" methods (e.g. ControllerBase.Ok/BadRequest) are
        // rendered as a self-call on the caller's own lifeline rather than a separate participant
        // for their (external) declaring type — one noisy "ControllerBase" swimlane per whitelisted
        // call would be both cluttered and semantically wrong, since the caller itself produces
        // the response, not a distinct collaborator.
        if (callerParticipantId is not null && symbol is IMethodSymbol method && WellKnownFrameworkMethods.IsWellKnownOutcomeMethod(method))
        {
            return callerParticipantId;
        }

        var containingType = symbol.ContainingType;
        if (containingType is not null)
            return table.GetOrAdd(containingType);

        // Top-level statements or module-level code: use assembly name as fallback.
        var assemblyName = symbol.ContainingAssembly?.Name ?? "?";
        return table.GetOrAddRaw(assemblyName);
    }
}
