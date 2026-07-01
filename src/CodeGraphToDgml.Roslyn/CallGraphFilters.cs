using System;
using System.IO;
using System.Linq;
using CodeGraphToDgml.Core;
using Microsoft.CodeAnalysis;

namespace CodeGraphToDgml.Roslyn;

/// <summary>
/// Symbol-inclusion filtering shared by Traverse Up/Down and sequence-diagram generation.
/// </summary>
public static class CallGraphFilters
{
    public static bool IsSupportedRootSymbol(ISymbol symbol)
    {
        return symbol.Kind is SymbolKind.Method or SymbolKind.Property or SymbolKind.Event;
    }

    public static bool IsAllowed(ISymbol symbol, TraversalOptions options)
    {
        if (!IsIncludedByKind(symbol, options))
        {
            return false;
        }

        var sourceLocation = symbol.Locations.FirstOrDefault(location => location.IsInSource);
        if (!options.IncludeExternalSymbols && sourceLocation is null)
        {
            // Item 4: well-known framework "outcome" methods (e.g. ControllerBase.Ok/BadRequest)
            // are allowed through even though they're external, independent of the global
            // IncludeExternalSymbols toggle — they represent real business behavior the user
            // wrote, not BCL noise.
            if (symbol is not IMethodSymbol method || !WellKnownFrameworkMethods.IsWellKnownOutcomeMethod(method))
            {
                return false;
            }
        }

        if (!options.IncludeGeneratedCode && IsGenerated(sourceLocation?.SourceTree?.FilePath))
        {
            return false;
        }

        return true;
    }

    private static bool IsIncludedByKind(ISymbol symbol, TraversalOptions options)
    {
        return symbol.Kind switch
        {
            SymbolKind.Method => true,
            SymbolKind.Property => options.IncludeProperties,
            SymbolKind.Event => options.IncludeEvents,
            _ => false,
        };
    }

    private static bool IsGenerated(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return false;
        }

        var normalizedFilePath = filePath!;
        var fileName = Path.GetFileName(normalizedFilePath) ?? string.Empty;
        return fileName.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase)
            || fileName.EndsWith(".g.vb", StringComparison.OrdinalIgnoreCase)
            || fileName.EndsWith(".designer.cs", StringComparison.OrdinalIgnoreCase)
            || fileName.EndsWith(".designer.vb", StringComparison.OrdinalIgnoreCase)
            || fileName.EndsWith(".generated.cs", StringComparison.OrdinalIgnoreCase)
            || fileName.EndsWith(".generated.vb", StringComparison.OrdinalIgnoreCase)
            || normalizedFilePath.IndexOf("\\obj\\", StringComparison.OrdinalIgnoreCase) >= 0;
    }
}
