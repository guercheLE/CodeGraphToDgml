using EnvDTE;
using EnvDTE80;

namespace CodeGraphToDgml.Vsix.Commands;

[Command(PackageGuids.EditorCommandsString, PackageIds.TraverseDownToSequenceCommand)]
internal sealed class TraverseDownToSequenceCommand : BaseCommand<TraverseDownToSequenceCommand>
{
    protected override void BeforeQueryStatus(EventArgs e)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        try
        {
            var dte = Microsoft.VisualStudio.Shell.Package.GetGlobalService(typeof(DTE)) as DTE2;
            var filePath = dte?.ActiveDocument?.FullName;
            var currentService = TraverseDownToSequenceOperationService.Current;
            var isSupported = currentService?.SupportsFilePath(filePath) == true;

            Command.Visible = isSupported;
            Command.Enabled = isSupported;
        }
        catch (Exception ex)
        {
            ActivityLog.TryLogError(nameof(TraverseDownToSequenceCommand), $"BeforeQueryStatus exception: {ex}");
        }
    }

    protected override async Task ExecuteAsync(OleMenuCmdEventArgs e)
    {
        if (TraverseDownToSequenceOperationService.Current is null)
        {
            ActivityLog.TryLogWarning(nameof(TraverseDownToSequenceCommand), "ExecuteAsync: TraverseDownToSequenceOperationService is not initialized.");
            return;
        }

        await TraverseDownToSequenceOperationService.Current.ExecuteAsync();
    }
}
