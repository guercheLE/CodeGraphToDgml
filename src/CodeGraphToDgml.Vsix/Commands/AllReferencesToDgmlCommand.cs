using System.ComponentModel.Design;
using EnvDTE;
using EnvDTE80;

namespace CodeGraphToDgml.Vsix.Commands;

[Command(PackageGuids.EditorCommandsString, PackageIds.AllReferencesToDgmlCommand)]
internal sealed class AllReferencesToDgmlCommand : BaseCommand<AllReferencesToDgmlCommand>
{
    protected override void BeforeQueryStatus(EventArgs e)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        try
        {
            var dte = Microsoft.VisualStudio.Shell.Package.GetGlobalService(typeof(DTE)) as DTE2;
            var filePath = dte?.ActiveDocument?.FullName;
            var currentService = AllReferencesToDgmlOperationService.Current;
            var isSupported = currentService?.SupportsFilePath(filePath) == true;

            Command.Visible = isSupported;
            Command.Enabled = isSupported;
        }
        catch (Exception ex)
        {
            ActivityLog.TryLogError(nameof(AllReferencesToDgmlCommand), $"BeforeQueryStatus exception: {ex}");
        }
    }

    protected override async Task ExecuteAsync(OleMenuCmdEventArgs e)
    {
        if (AllReferencesToDgmlOperationService.Current is null)
        {
            ActivityLog.TryLogWarning(nameof(AllReferencesToDgmlCommand), "ExecuteAsync: AllReferencesToDgmlOperationService is not initialized.");
            return;
        }

        await AllReferencesToDgmlOperationService.Current.ExecuteAsync();
    }
}
