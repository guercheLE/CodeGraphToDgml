using EnvDTE;
using EnvDTE80;

namespace CodeGraphToDgml.Vsix.Commands;

/// <summary>
/// "Traverse Down to Sequence Diagram (with Control Flow)": same traversal as
/// <see cref="TraverseDownToSequenceCommand"/>, but calls are wrapped in Mermaid
/// alt/opt/loop/break fragments derived from the enclosing if/switch/loop/catch statements.
/// </summary>
[Command(PackageGuids.EditorCommandsString, PackageIds.TraverseDownToSequenceFlowCommand)]
internal sealed class TraverseDownToSequenceFlowCommand : BaseCommand<TraverseDownToSequenceFlowCommand>
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
            ActivityLog.TryLogError(nameof(TraverseDownToSequenceFlowCommand), $"BeforeQueryStatus exception: {ex}");
        }
    }

    protected override async Task ExecuteAsync(OleMenuCmdEventArgs e)
    {
        if (TraverseDownToSequenceOperationService.Current is null)
        {
            ActivityLog.TryLogWarning(nameof(TraverseDownToSequenceFlowCommand), "ExecuteAsync: TraverseDownToSequenceOperationService is not initialized.");
            return;
        }

        await TraverseDownToSequenceOperationService.Current.ExecuteAsync(includeControlFlow: true);
    }
}
