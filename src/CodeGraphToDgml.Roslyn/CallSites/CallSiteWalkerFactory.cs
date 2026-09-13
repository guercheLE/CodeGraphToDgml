using Microsoft.CodeAnalysis;

namespace CodeGraphToDgml.Roslyn.CallSites;

internal static class CallSiteWalkerFactory
{
    /// <summary>Returns the walker for a Roslyn language name, or null for unsupported languages.</summary>
    public static ICallSiteWalker? For(string language)
    {
        return language switch
        {
            LanguageNames.CSharp => CSharpCallSiteWalker.Instance,
            LanguageNames.VisualBasic => VisualBasicCallSiteWalker.Instance,
            _ => null,
        };
    }
}
