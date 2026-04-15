using System.Collections.Generic;
using System.Linq;
using CodeGraphToDgml.Vsix.Providers;

namespace CodeGraphToDgml.Vsix;

internal sealed class HierarchyProviderRegistry
{
    private readonly IReadOnlyList<IHierarchyProvider> _providers;

    public HierarchyProviderRegistry(ToolkitPackage package)
    {
        _providers = new IHierarchyProvider[]
        {
            new RoslynCallHierarchyProvider(package),
        };
    }

    public bool SupportsFilePath(string? filePath)
    {
        return _providers.Any(provider => provider.CouldSupport(filePath));
    }

    public async Task<(IHierarchyProvider Provider, HierarchySubject Subject)?> TryResolveAsync(CancellationToken cancellationToken)
    {
        foreach (var provider in _providers)
        {
            var subject = await provider.TryResolveSubjectAsync(cancellationToken).ConfigureAwait(false);
            if (subject is not null)
            {
                return (provider, subject);
            }
        }

        return null;
    }
}
