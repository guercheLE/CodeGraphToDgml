using System.Collections.Generic;
using Microsoft.CodeAnalysis;

namespace CodeGraphToDgml.Roslyn.CallSites;

/// <summary>
/// Visual Basic control-flow context. Not implemented yet: every site is reported as
/// straight-line code, so the control-flow command renders a VB member as a plain diagram.
/// </summary>
internal static class VisualBasicFlowContext
{
    public static IReadOnlyList<RawFlowScope> GetFlowContext(SyntaxNode site, SyntaxNode body)
    {
        return [];
    }
}
