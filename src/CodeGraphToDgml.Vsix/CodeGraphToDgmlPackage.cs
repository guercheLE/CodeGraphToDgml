using System.Runtime.InteropServices;
using System.Threading;

namespace CodeGraphToDgml.Vsix;

[PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
[InstalledProductRegistration(Vsix.Name, Vsix.Description, Vsix.Version)]
[ProvideMenuResource("Menus.ctmenu", 1)]
[ProvideOptionPage(typeof(OptionsProvider.GeneralOptions), Vsix.Name, "General", 0, 0, true, ProvidesLocalizedCategoryName = false, SupportsProfiles = true)]
[Guid(PackageGuids.CodeGraphToDgmlString)]
public sealed class CodeGraphToDgmlPackage : ToolkitPackage
{
    protected override async Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
    {
        await this.RegisterCommandsAsync();

        try
        {
            TraverseUpToDgmlOperationService.Initialize(this);
            TraverseDownToDgmlOperationService.Initialize(this);
            AllReferencesToDgmlOperationService.Initialize(this);
            TraverseDownToSequenceOperationService.Initialize(this);
            ActivityLog.TryLogInformation(nameof(CodeGraphToDgmlPackage), "InitializeAsync completed successfully");
        }
        catch (Exception ex)
        {
            ActivityLog.TryLogError(nameof(CodeGraphToDgmlPackage), $"InitializeAsync failed: {ex}");
            throw;
        }
    }
}
