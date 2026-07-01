using System.Collections.Generic;
using Microsoft.CodeAnalysis;

namespace CodeGraphToDgml.Roslyn;

/// <summary>
/// A small, curated, extensible whitelist of framework base-class methods that represent
/// meaningful business behavior (e.g. an ASP.NET Core controller producing an HTTP result) and
/// should be included in call graphs / sequence diagrams even though their declaring type lives
/// outside the analyzed solution. Deliberately narrow — this is not a general external-symbol
/// inclusion mechanism (that would flood diagrams with BCL noise like Console.WriteLine).
/// </summary>
public static class WellKnownFrameworkMethods
{
    private static readonly SymbolDisplayFormat TypeNameFormat = new(
        globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Omitted,
        typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces);

    private static readonly HashSet<(string Type, string Method)> Whitelist = new()
    {
        ("Microsoft.AspNetCore.Mvc.ControllerBase", "Ok"),
        ("Microsoft.AspNetCore.Mvc.ControllerBase", "BadRequest"),
        ("Microsoft.AspNetCore.Mvc.ControllerBase", "NotFound"),
        ("Microsoft.AspNetCore.Mvc.ControllerBase", "Created"),
        ("Microsoft.AspNetCore.Mvc.ControllerBase", "CreatedAtAction"),
        ("Microsoft.AspNetCore.Mvc.ControllerBase", "CreatedAtRoute"),
        ("Microsoft.AspNetCore.Mvc.ControllerBase", "Unauthorized"),
        ("Microsoft.AspNetCore.Mvc.ControllerBase", "Forbid"),
        ("Microsoft.AspNetCore.Mvc.ControllerBase", "NoContent"),
        ("Microsoft.AspNetCore.Mvc.ControllerBase", "Problem"),
        ("Microsoft.AspNetCore.Mvc.ControllerBase", "StatusCode"),
        ("Microsoft.AspNetCore.Mvc.ControllerBase", "Conflict"),
        ("Microsoft.AspNetCore.Mvc.ControllerBase", "ValidationProblem"),
        ("Microsoft.AspNetCore.Mvc.ControllerBase", "Redirect"),
        ("Microsoft.AspNetCore.Mvc.ControllerBase", "RedirectToAction"),
        ("Microsoft.AspNetCore.Mvc.ControllerBase", "File"),
    };

    /// <summary>
    /// True when <paramref name="method"/> (or a method it overrides, walking the base-type
    /// chain) matches an entry in the whitelist.
    /// </summary>
    public static bool IsWellKnownOutcomeMethod(IMethodSymbol method)
    {
        for (var type = method.ContainingType; type is not null; type = type.BaseType)
        {
            var fqName = type.ToDisplayString(TypeNameFormat);
            if (Whitelist.Contains((fqName, method.Name)))
            {
                return true;
            }
        }

        return false;
    }
}
