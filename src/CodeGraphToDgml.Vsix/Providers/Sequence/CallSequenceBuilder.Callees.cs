using System.Collections.Generic;
using CodeGraphToDgml.Core;
using CodeGraphToDgml.Roslyn;
using Microsoft.CodeAnalysis;
using RoslynSolution = Microsoft.CodeAnalysis.Solution;
using static CodeGraphToDgml.Vsix.Providers.RoslynGraphHelpers;

namespace CodeGraphToDgml.Vsix.Providers;

// Symbol-level callees: one node per distinct callee of a member, fluent chains grafted under
// their receiver. Used by the plain "Traverse Down to Sequence Diagram" command.
internal sealed partial class CallSequenceBuilder
{
    private async Task<IReadOnlyList<CallSequenceCallNode>> BuildCallsAsync(
        ISymbol caller,
        string callerParticipantId,
        int depth)
    {
        if (depth >= _options.MaxDepth)
            return [];

        _progress?.Report(new TraversalProgress("Traversing callees", depth, _nodeCount, caller.ToDisplayString()));

        var callees = await CallGraphSyntaxWalker.FindCalleesAsync(caller, _solution, _cancellationToken).ConfigureAwait(false);

        // Item 1g: fluent invocation chains (e.g. obj.GetClient().GetProduct()) are nested
        // under their receiver's activation in the sequence diagram, instead of being shown
        // as siblings both called directly by `caller`. This is a rendering-only distinction —
        // the DGML graph (TraverseDownAsync above) never sees CalleeInfo.FluentReceiver.
        //
        // Pass 1: build each callee's own node content (participant, label, its own nested
        // calls) without yet deciding whether it will be a top-level sibling or grafted under
        // a fluent receiver.
        var order = new List<ISymbol>();
        var ownNestedOf = new Dictionary<ISymbol, IReadOnlyList<CallSequenceCallNode>>(SymbolEqualityComparer.Default);
        var participantOf = new Dictionary<ISymbol, string>(SymbolEqualityComparer.Default);
        var labelOf = new Dictionary<ISymbol, string>(SymbolEqualityComparer.Default);
        var returnTypeLabelOf = new Dictionary<ISymbol, string>(SymbolEqualityComparer.Default);
        var fluentReceiverOf = new Dictionary<ISymbol, ISymbol>(SymbolEqualityComparer.Default);

        foreach (var calleeInfo in callees)
        {
            _cancellationToken.ThrowIfCancellationRequested();

            var normalized = CallGraphSyntaxWalker.NormalizeSymbol(calleeInfo.Symbol);
            if (normalized is null || !CallGraphFilters.IsAllowed(normalized, _options))
                continue;

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
                nested = await BuildDispatchCallsAsync(implementations, calleeParticipantId, depth).ConfigureAwait(false);
            }
            else if (_visited.Add(calleeId))
            {
                var ownBody = await BuildCallsAsync(normalized, calleeParticipantId, depth + 1).ConfigureAwait(false);

                if (CallGraphDispatch.CanBeOverridden(normalized))
                {
                    // A virtual/abstract callee may dispatch to an override at run time; show
                    // each override as a dispatch call from the base member's lifeline, after
                    // the base member's own body, mirroring interface dispatch above.
                    var overrides = await CallGraphDispatch.FindOverridesAsync(normalized, _solution, _cancellationToken).ConfigureAwait(false);
                    var dispatch = await BuildDispatchCallsAsync(overrides, calleeParticipantId, depth).ConfigureAwait(false);
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

            order.Add(normalized);
            ownNestedOf[normalized] = nested;
            participantOf[normalized] = calleeParticipantId;
            labelOf[normalized] = GetNodeLabel(normalized);
            returnTypeLabelOf[normalized] = GetReturnTypeLabel(normalized);

            if (calleeInfo.FluentReceiver is { } receiver)
            {
                var normReceiver = CallGraphSyntaxWalker.NormalizeSymbol(receiver);
                if (normReceiver != null)
                    fluentReceiverOf[normalized] = normReceiver;
            }
        }

        // Pass 2: assemble nodes, grafting fluent-dependents into their receiver's
        // NestedCalls (with the receiver's own participant as the dependent's caller)
        // instead of returning them as top-level siblings of `caller`.
        var finalized = new Dictionary<ISymbol, CallSequenceCallNode>(SymbolEqualityComparer.Default);

        CallSequenceCallNode Finalize(ISymbol symbol, string effectiveCallerParticipantId)
        {
            if (finalized.TryGetValue(symbol, out var existing))
                return existing;

            var participantId = participantOf[symbol];
            var ownNested = new List<CallSequenceCallNode>(ownNestedOf[symbol]);
            foreach (var dependent in order)
            {
                if (fluentReceiverOf.TryGetValue(dependent, out var receiver)
                    && SymbolEqualityComparer.Default.Equals(receiver, symbol))
                {
                    ownNested.Add(Finalize(dependent, participantId));
                }
            }

            var node = new CallSequenceCallNode(effectiveCallerParticipantId, participantId, labelOf[symbol], ownNested, returnTypeLabelOf[symbol]);
            finalized[symbol] = node;
            return node;
        }

        var nodes = new List<CallSequenceCallNode>();
        foreach (var symbol in order)
        {
            // Skip symbols that will be grafted under their fluent receiver's NestedCalls
            // (only when that receiver was itself kept in this batch).
            if (fluentReceiverOf.TryGetValue(symbol, out var receiver) && ownNestedOf.ContainsKey(receiver))
                continue;

            nodes.Add(Finalize(symbol, callerParticipantId));
        }

        return nodes;
    }

    // Renders each run-time dispatch target (interface implementation or virtual override) as
    // a call from the dispatching member's lifeline, with the target's own body nested.
    private async Task<IReadOnlyList<CallSequenceCallNode>> BuildDispatchCallsAsync(
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
                targetBody = await BuildCallsAsync(target, targetParticipantId, depth + 1).ConfigureAwait(false);
            else
                targetBody = [];

            dispatchNodes.Add(new CallSequenceCallNode(dispatcherParticipantId, targetParticipantId, GetNodeLabel(target), targetBody, GetReturnTypeLabel(target)));
        }

        return dispatchNodes;
    }
}
