using System.ComponentModel.Design;
using EnvDTE;
using EnvDTE80;

namespace CodeGraphToDgml.Vsix.Commands;

[Command(PackageGuids.EditorCommandsString, PackageIds.TraverseUpToDgmlCommand)]
internal sealed class TraverseUpToDgmlCommand : BaseCommand<TraverseUpToDgmlCommand>
{
    protected override void BeforeQueryStatus(EventArgs e)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        try
        {
            var dte = Microsoft.VisualStudio.Shell.Package.GetGlobalService(typeof(DTE)) as DTE2;
            var filePath = dte?.ActiveDocument?.FullName;
            var currentService = TraverseUpToDgmlOperationService.Current;
            var isSupported = currentService?.SupportsFilePath(filePath) == true;


            Command.Visible = isSupported;
            Command.Enabled = isSupported;
        }
        catch (Exception ex)
        {
            ActivityLog.TryLogError(nameof(TraverseUpToDgmlCommand), $"BeforeQueryStatus exception: {ex}");
        }
    }

    protected override async Task ExecuteAsync(OleMenuCmdEventArgs e)
    {
        if (TraverseUpToDgmlOperationService.Current is null)
        {
            ActivityLog.TryLogWarning(nameof(TraverseUpToDgmlCommand), "ExecuteAsync: TraverseUpToDgmlOperationService is not initialized.");
            return;
        }

        await TraverseUpToDgmlOperationService.Current.ExecuteAsync();
    }
}
