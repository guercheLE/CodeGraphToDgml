using System.Collections.Generic;
using System.Text;
using CodeGraphToDgml.Core;
using Microsoft.CodeAnalysis;

namespace CodeGraphToDgml.Roslyn.CallSites;

/// <summary>
/// Turns raw per-site flow scopes into the <see cref="CallSequenceFragmentScope"/> paths the
/// serializer renders: numbers fragment instances in first-seen order, downgrades an
/// <c>alt</c> whose calls all sit in one section to an <c>opt</c>, truncates labels, and
/// de-duplicates a callee that appears several times in the same section.
/// </summary>
internal static class FlowContextFinalizer
{
    public static IReadOnlyList<CallSiteInfo> Finalize(
        IReadOnlyList<(RawCallSite Site, IReadOnlyList<RawFlowScope> Scopes)> collected,
        int maxLabelLength)
    {
        var instanceIds = new Dictionary<SyntaxNode, int>();
        var usedSections = new Dictionary<SyntaxNode, HashSet<int>>();

        foreach (var (_, scopes) in collected)
        {
            foreach (var scope in scopes)
            {
                if (!instanceIds.ContainsKey(scope.Construct))
                {
                    instanceIds[scope.Construct] = instanceIds.Count + 1;
                    usedSections[scope.Construct] = new HashSet<int>();
                }

                usedSections[scope.Construct].Add(scope.SectionIndex);
            }
        }

        var result = new List<CallSiteInfo>();
        var seenPathsOf = new Dictionary<ISymbol, HashSet<string>>(SymbolEqualityComparer.Default);

        foreach (var (site, scopes) in collected)
        {
            var path = new List<CallSequenceFragmentScope>(scopes.Count);
            var signature = new StringBuilder();

            foreach (var scope in scopes)
            {
                var instanceId = instanceIds[scope.Construct];
                var kind = scope.Kind;
                var section = scope.SectionIndex;
                var label = scope.Label;

                if (kind == SequenceFragmentKind.Alt && usedSections[scope.Construct].Count == 1)
                {
                    // Only one branch contains calls: an `alt` with a single section reads
                    // better as `opt`. A lone else/default section is phrased as the negation
                    // of the first condition, since "else" alone would be meaningless.
                    kind = SequenceFragmentKind.Opt;
                    section = 0;
                    if (scope.IsElseSection)
                        label = "not (" + scope.FirstSectionLabel + ")";
                }

                path.Add(new CallSequenceFragmentScope(instanceId, kind, section, Truncate(label, maxLabelLength)));
                signature.Append(instanceId).Append(':').Append(section).Append('|');
            }

            if (!seenPathsOf.TryGetValue(site.Symbol, out var seenPaths))
            {
                seenPaths = new HashSet<string>(System.StringComparer.Ordinal);
                seenPathsOf[site.Symbol] = seenPaths;
            }

            if (!seenPaths.Add(signature.ToString()))
                continue;

            result.Add(new CallSiteInfo(site.Symbol, site.FluentReceiver, path));
        }

        return result;
    }

    private static string Truncate(string label, int maxLength)
    {
        if (label.Length <= maxLength)
            return label;

        return label.Substring(0, maxLength - 1).TrimEnd() + "…";
    }
}
