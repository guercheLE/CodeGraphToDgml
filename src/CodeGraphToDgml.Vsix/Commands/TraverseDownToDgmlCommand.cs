using System.ComponentModel.Design;
using EnvDTE;
using EnvDTE80;

namespace CodeGraphToDgml.Vsix.Commands;

[Command(PackageGuids.EditorCommandsString, PackageIds.TraverseDownToDgmlCommand)]
internal sealed class TraverseDownToDgmlCommand : BaseCommand<TraverseDownToDgmlCommand>
{
    protected override void BeforeQueryStatus(EventArgs e)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        try
        {
            var dte = Microsoft.VisualStudio.Shell.Package.GetGlobalService(typeof(DTE)) as DTE2;
            var filePath = dte?.ActiveDocument?.FullName;
            var currentService = TraverseDownToDgmlOperationService.Current;
            var isSupported = currentService?.SupportsFilePath(filePath) == true;

            Command.Visible = isSupported;
            Command.Enabled = isSupported;
        }
        catch (Exception ex)
        {
            ActivityLog.TryLogError(nameof(TraverseDownToDgmlCommand), $"BeforeQueryStatus exception: {ex}");
        }
    }

    protected override async Task ExecuteAsync(OleMenuCmdEventArgs e)
    {
        if (TraverseDownToDgmlOperationService.Current is null)
        {
            ActivityLog.TryLogWarning(nameof(TraverseDownToDgmlCommand), "ExecuteAsync: TraverseDownToDgmlOperationService is not initialized.");
            return;
        }

        await TraverseDownToDgmlOperationService.Current.ExecuteAsync();
    }
}
