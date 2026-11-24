using System.Collections.Generic;
using CodeGraphToDgml.Core;
using CodeGraphToDgml.Roslyn;
using Microsoft.CodeAnalysis;
using RoslynSolution = Microsoft.CodeAnalysis.Solution;
using static CodeGraphToDgml.Vsix.Providers.RoslynGraphHelpers;

namespace CodeGraphToDgml.Vsix.Providers;

// Per-call-site variant ("with Control Flow" command): same shape as BuildCallsAsync, but driven
// by FindCallSitesAsync, so there is one entry per call site (a callee called from two branches
// appears twice, each with its own fragment path), keyed by site index instead of by symbol.
internal sealed partial class CallSequenceBuilder
{
    private const int DispatchFragmentInstanceId = -1;

    private sealed record FlowEntry(
        string ParticipantId,
        string Label,
        string ReturnTypeLabel,
        IReadOnlyList<CallSequenceCallNode> Nested,
        IReadOnlyList<CallSequenceFragmentScope> FragmentPath,
        int ReceiverIndex);

    private async Task<IReadOnlyList<CallSequenceCallNode>> BuildFlowCallsAsync(
        ISymbol caller,
        string callerParticipantId,
        int depth)
    {
        if (depth >= _options.MaxDepth)
            return [];

        _progress?.Report(new TraversalProgress("Traversing callees", depth, _nodeCount, caller.ToDisplayString()));

        // Already normalized and filtered by CallGraphFilters.IsAllowed.
        var sites = await CallGraphSyntaxWalker.FindCallSitesAsync(caller, _solution, _options, _cancellationToken).ConfigureAwait(false);

        var entries = new List<FlowEntry>(sites.Count);
        var lastEntryOf = new Dictionary<ISymbol, int>(SymbolEqualityComparer.Default);

        foreach (var site in sites)
        {
            _cancellationToken.ThrowIfCancellationRequested();

            var normalized = site.Symbol;

            _nodeCount++;
            if (_nodeCount >= _options.MaxNodeCount)
                break;

            var calleeParticipantId = GetParticipantId(normalized, _participants, callerParticipantId);
            var calleeId = GetStableId(normalized);

            IReadOnlyList<CallSequenceCallNode> nested;
            if (CallGraphDispatch.IsInterfaceMember(normalized))
            {
                _visited.Add(calleeId);
                var implementations = await CallGraphDispatch.FindImplementationsAsync(normalized, _solution, _cancellationToken).ConfigureAwait(false);
                nested = await BuildDispatchFlowCallsAsync(implementations, calleeParticipantId, depth).ConfigureAwait(false);
            }
            else if (_visited.Add(calleeId))
            {
                var ownBody = await BuildFlowCallsAsync(normalized, calleeParticipantId, depth + 1).ConfigureAwait(false);

                if (CallGraphDispatch.CanBeOverridden(normalized))
                {
                    var overrides = await CallGraphDispatch.FindOverridesAsync(normalized, _solution, _cancellationToken).ConfigureAwait(false);
                    var dispatch = await BuildDispatchFlowCallsAsync(overrides, calleeParticipantId, depth).ConfigureAwait(false);
                    nested = dispatch.Count == 0 ? ownBody : [.. ownBody, .. dispatch];
                }
                else
                {
                    nested = ownBody;
                }
            }
            else
            {
                nested = [];
            }

            int receiverIndex = -1;
            if (site.FluentReceiver is { } receiver
                && CallGraphSyntaxWalker.NormalizeSymbol(receiver) is { } normalizedReceiver
                && lastEntryOf.TryGetValue(normalizedReceiver, out var receiverEntry))
            {
                receiverIndex = receiverEntry;
            }

            entries.Add(new FlowEntry(
                calleeParticipantId,
                GetNodeLabel(normalized),
                GetReturnTypeLabel(normalized),
                nested,
                site.FragmentPath,
                receiverIndex));

            lastEntryOf[normalized] = entries.Count - 1;
        }

        // Pass 2: graft fluent dependents under their receiver (same as BuildCallsAsync).
        // Grafted dependents carry no fragment path: they render inside the receiver's
        // activation, whose own path already opened the enclosing fragment.
        var finalized = new CallSequenceCallNode?[entries.Count];

        CallSequenceCallNode Finalize(int index, string effectiveCallerParticipantId, bool asDependent)
        {
            if (!asDependent && finalized[index] is { } existing)
                return existing;

            var entry = entries[index];
            var ownNested = new List<CallSequenceCallNode>(entry.Nested);
            for (int j = 0; j < entries.Count; j++)
            {
                if (entries[j].ReceiverIndex == index)
                    ownNested.Add(Finalize(j, entry.ParticipantId, asDependent: true));
            }

            var node = new CallSequenceCallNode(effectiveCallerParticipantId, entry.ParticipantId, entry.Label, ownNested, entry.ReturnTypeLabel)
            {
                FragmentPath = asDependent ? [] : entry.FragmentPath,
            };

            if (!asDependent)
                finalized[index] = node;
            return node;
        }

        var nodes = new List<CallSequenceCallNode>();
        for (int i = 0; i < entries.Count; i++)
        {
            if (entries[i].ReceiverIndex >= 0)
                continue;

            nodes.Add(Finalize(i, callerParticipantId, asDependent: false));
        }

        return nodes;
    }

    // Interface implementations / virtual overrides as one `alt` with a branch per target
    // (labelled with the implementing type); a single target needs no fragment.
    private async Task<IReadOnlyList<CallSequenceCallNode>> BuildDispatchFlowCallsAsync(
        IReadOnlyList<ISymbol> targets,
        string dispatcherParticipantId,
        int depth)
    {
        var dispatchNodes = new List<CallSequenceCallNode>();
        foreach (var target in targets)
        {
            _cancellationToken.ThrowIfCancellationRequested();

            if (!CallGraphFilters.IsAllowed(target, _options))
                continue;

            _nodeCount++;
            if (_nodeCount >= _options.MaxNodeCount)
                break;

            var targetParticipantId = GetParticipantId(target, _participants, dispatcherParticipantId);
            var targetId = GetStableId(target);

            IReadOnlyList<CallSequenceCallNode> targetBody;
            if (_visited.Add(targetId))
                targetBody = await BuildFlowCallsAsync(target, targetParticipantId, depth + 1).ConfigureAwait(false);
            else
                targetBody = [];

            dispatchNodes.Add(new CallSequenceCallNode(dispatcherParticipantId, targetParticipantId, GetNodeLabel(target), targetBody, GetReturnTypeLabel(target))
            {
                // Section label: the implementing type. Instance id -1 never collides with
                // the positive ids FindCallSitesAsync assigns to a body's own fragments.
                FragmentPath = [new CallSequenceFragmentScope(DispatchFragmentInstanceId, SequenceFragmentKind.Alt, dispatchNodes.Count, target.ContainingType?.Name ?? target.Name)],
            });
        }

        if (dispatchNodes.Count == 1)
            return [dispatchNodes[0] with { FragmentPath = [] }];

        return dispatchNodes;
    }
}
