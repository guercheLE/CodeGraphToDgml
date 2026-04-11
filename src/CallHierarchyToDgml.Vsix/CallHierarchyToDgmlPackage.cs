using System.Runtime.InteropServices;
using System.Threading;

namespace CallHierarchyToDgml.Vsix;

[PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
[InstalledProductRegistration(Vsix.Name, Vsix.Description, Vsix.Version)]
[ProvideMenuResource("Menus.ctmenu", 1)]
[ProvideOptionPage(typeof(OptionsProvider.GeneralOptions), Vsix.Name, "General", 0, 0, true, ProvidesLocalizedCategoryName = false, SupportsProfiles = true)]
[Guid(PackageGuids.CallHierarchyToDgmlString)]
public sealed class CallHierarchyToDgmlPackage : ToolkitPackage
{
    protected override async Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
    {
        await this.RegisterCommandsAsync();

        try
        {
            TraverseUpToDgmlOperationService.Initialize(this);
            ActivityLog.TryLogInformation(nameof(CallHierarchyToDgmlPackage), "InitializeAsync completed successfully");
        }
        catch (Exception ex)
        {
            ActivityLog.TryLogError(nameof(CallHierarchyToDgmlPackage), $"InitializeAsync failed: {ex}");
            throw;
        }
    }
}
