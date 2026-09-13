using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FindSymbols;

namespace CodeGraphToDgml.Roslyn;

/// <summary>
/// Resolves the concrete members a call site may dispatch to at run time: the implementations of
/// an interface member, and the overrides of a virtual, abstract, or overriding class member.
/// Shared by Traverse Down (DGML) and the sequence-diagram builder so both follow the same
/// dispatch rules.
/// </summary>
public static class CallGraphDispatch
{
    public static bool IsInterfaceMember(ISymbol symbol)
    {
        return symbol.ContainingType?.TypeKind == TypeKind.Interface;
    }

    /// <summary>
    /// True for class members that a derived type may override: <c>virtual</c>, <c>abstract</c>,
    /// or a non-sealed <c>override</c>.
    /// </summary>
    public static bool CanBeOverridden(ISymbol symbol)
    {
        if (symbol.ContainingType?.TypeKind != TypeKind.Class || symbol.IsSealed)
        {
            return false;
        }

        return symbol.IsVirtual || symbol.IsAbstract || symbol.IsOverride;
    }

    /// <summary>
    /// All members implementing <paramref name="interfaceMember"/> across the solution,
    /// normalized and ordered by display string for deterministic output.
    /// </summary>
    public static async Task<IReadOnlyList<ISymbol>> FindImplementationsAsync(
        ISymbol interfaceMember,
        Solution solution,
        CancellationToken cancellationToken)
    {
        var implementations = await SymbolFinder.FindImplementationsAsync(
            interfaceMember, solution, cancellationToken: cancellationToken).ConfigureAwait(false);

        return Normalize(implementations, interfaceMember);
    }

    /// <summary>
    /// All members overriding <paramref name="member"/> across the solution, including indirect
    /// overrides further down the hierarchy, normalized and ordered by display string.
    /// </summary>
    public static async Task<IReadOnlyList<ISymbol>> FindOverridesAsync(
        ISymbol member,
        Solution solution,
        CancellationToken cancellationToken)
    {
        var overrides = await SymbolFinder.FindOverridesAsync(
            member, solution, cancellationToken: cancellationToken).ConfigureAwait(false);

        return Normalize(overrides, member);
    }

    /// <summary>
    /// Implementations for an interface member, overrides for an overridable class member,
    /// otherwise empty.
    /// </summary>
    public static Task<IReadOnlyList<ISymbol>> FindDispatchTargetsAsync(
        ISymbol member,
        Solution solution,
        CancellationToken cancellationToken)
    {
        if (IsInterfaceMember(member))
        {
            return FindImplementationsAsync(member, solution, cancellationToken);
        }

        if (CanBeOverridden(member))
        {
            return FindOverridesAsync(member, solution, cancellationToken);
        }

        return Task.FromResult<IReadOnlyList<ISymbol>>([]);
    }

    private static IReadOnlyList<ISymbol> Normalize(IEnumerable<ISymbol> symbols, ISymbol self)
    {
        var seen = new HashSet<ISymbol>(SymbolEqualityComparer.Default) { self };
        var result = new List<ISymbol>();

        foreach (var symbol in symbols)
        {
            var normalized = CallGraphSyntaxWalker.NormalizeSymbol(symbol);
            if (normalized is not null && seen.Add(normalized))
            {
                result.Add(normalized);
            }
        }

        return result
            .OrderBy(symbol => symbol.ToDisplayString(), System.StringComparer.Ordinal)
            .ToList();
    }
}
