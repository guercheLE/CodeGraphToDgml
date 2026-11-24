using System.Collections.Generic;

namespace CodeGraphToDgml.Core;

internal static class FragmentPaths
{
    // Number of leading fragment scopes two sibling paths share (by instance), i.e. how many
    // fragments would stay open across the boundary between them.
    internal static int CommonInstancePrefixLength(IReadOnlyList<CallSequenceFragmentScope>? a, IReadOnlyList<CallSequenceFragmentScope>? b)
    {
        if (a is null || b is null)
            return 0;

        int n = 0;
        while (n < a.Count && n < b.Count && a[n].InstanceId == b[n].InstanceId)
            n++;
        return n;
    }
}
