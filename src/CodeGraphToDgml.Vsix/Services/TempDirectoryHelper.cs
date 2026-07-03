using System.IO;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell.Interop;

namespace CodeGraphToDgml.Vsix;

internal static class TempDirectoryHelper
{
    // Keeps output under the solution's ".vs" folder instead of %TEMP%, since VS throws NullReferenceException opening files outside the loaded solution tree.
    internal static async Task<string> GetOutputDirectoryAsync(ToolkitPackage package)
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

        var solution = await package.GetServiceAsync(typeof(SVsSolution)).ConfigureAwait(true) as IVsSolution;
        if (solution != null &&
            ErrorHandler.Succeeded(solution.GetSolutionInfo(out var solutionDirectory, out _, out _)) &&
            !string.IsNullOrEmpty(solutionDirectory))
        {
            var directory = Path.Combine(solutionDirectory, ".vs", "CodeGraphToDgml");
            Directory.CreateDirectory(directory);
            return directory;
        }

        return Path.GetTempPath();
    }
}
