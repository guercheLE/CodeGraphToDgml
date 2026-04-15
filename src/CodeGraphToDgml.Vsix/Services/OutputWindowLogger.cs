using Microsoft.VisualStudio.Shell.Interop;

namespace CodeGraphToDgml.Vsix;

internal sealed class OutputWindowLogger
{
    private static readonly Guid PaneGuid = new("9f8e95f5-b69f-41f0-b8dd-06e31b67c946");
    private readonly ToolkitPackage _package;

    public OutputWindowLogger(ToolkitPackage package)
    {
        _package = package;
    }

    public async Task WriteLineAsync(string message)
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

        var outputWindow = await _package.GetServiceAsync(typeof(SVsOutputWindow)).ConfigureAwait(true) as IVsOutputWindow;
        if (outputWindow is null)
        {
            return;
        }

        var paneGuid = PaneGuid;
        outputWindow.CreatePane(ref paneGuid, Vsix.Name, fInitVisible: 1, fClearWithSolution: 1);
        outputWindow.GetPane(ref paneGuid, out var pane);
        pane?.OutputStringThreadSafe(message + Environment.NewLine);
    }
}
