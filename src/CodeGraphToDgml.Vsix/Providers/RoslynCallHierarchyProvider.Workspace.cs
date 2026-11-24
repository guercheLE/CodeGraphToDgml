using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.VisualStudio.ComponentModelHost;

namespace CodeGraphToDgml.Vsix.Providers;

// Locates the Visual Studio Roslyn workspace through MEF, with reflection fallbacks.
internal sealed partial class RoslynCallHierarchyProvider
{
    private async Task<Workspace?> GetWorkspaceAsync(CancellationToken cancellationToken)
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

        var componentModel = await _package.GetServiceAsync(typeof(SComponentModel)).ConfigureAwait(true) as IComponentModel;
        if (componentModel is null)
        {
            return null;
        }

        var genericMethodDefinition = typeof(IComponentModel)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Single(method => method.Name == nameof(IComponentModel.GetService) && method.IsGenericMethodDefinition && method.GetParameters().Length == 0);

        foreach (var workspaceType in GetWorkspaceCandidateTypes())
        {
            if (!typeof(Workspace).IsAssignableFrom(workspaceType))
            {
                continue;
            }

            try
            {
                var genericMethod = genericMethodDefinition.MakeGenericMethod(workspaceType);
                if (genericMethod.Invoke(componentModel, null) is Workspace workspace)
                {
                    return workspace;
                }
            }
            catch
            {
            }
        }

        try
        {
            var exportProviderProperty = componentModel.GetType().GetProperty("DefaultExportProvider", BindingFlags.Public | BindingFlags.Instance);
            var exportProvider = exportProviderProperty?.GetValue(componentModel);
            var getExportedValuesMethod = exportProvider?.GetType()
                .GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .FirstOrDefault(method => method.Name == "GetExportedValues" && method.IsGenericMethodDefinition && method.GetParameters().Length == 0);

            if (exportProvider is not null && getExportedValuesMethod is not null)
            {
                var genericMethod = getExportedValuesMethod.MakeGenericMethod(typeof(Workspace));
                if (genericMethod.Invoke(exportProvider, null) is IEnumerable<Workspace> workspaces)
                {
                    return workspaces.FirstOrDefault();
                }
            }
        }
        catch
        {
        }

        return null;
    }

    private static IEnumerable<Type> GetWorkspaceCandidateTypes()
    {
        yield return typeof(Workspace);

        var primaryType = Type.GetType(
            "Microsoft.VisualStudio.LanguageServices.VisualStudioWorkspace, Microsoft.VisualStudio.LanguageServices",
            throwOnError: false);

        if (primaryType is not null)
        {
            yield return primaryType;
        }

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type[] types;
            try
            {
                types = assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                types = ex.Types.Where(type => type is not null).Cast<Type>().ToArray();
            }
            catch
            {
                continue;
            }

            foreach (var type in types)
            {
                if (!typeof(Workspace).IsAssignableFrom(type))
                {
                    continue;
                }

                if (type.Name.IndexOf("Workspace", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                yield return type;
            }
        }
    }
}
