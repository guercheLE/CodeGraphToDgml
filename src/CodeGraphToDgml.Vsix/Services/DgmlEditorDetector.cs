using System.Linq;
using Microsoft.VisualStudio.Setup.Configuration;
using Microsoft.VisualStudio.Shell.Interop;

namespace CodeGraphToDgml.Vsix;

/// <summary>
/// Detects whether the "DGML editor" individual component
/// (<c>Microsoft.VisualStudio.Component.GraphDocument</c>) is installed in the running Visual Studio
/// instance. It is optional on Community and Professional; without it a .dgml file opens as plain
/// XML. The VSIX manifest declares it as a prerequisite, and this runtime check is the fallback
/// that explains the situation instead of failing silently.
/// </summary>
internal static class DgmlEditorDetector
{
    private const string GraphDocumentComponentId = "Microsoft.VisualStudio.Component.GraphDocument";

    private static bool? _isInstalled;
    private static bool _warned;

    public static bool IsDgmlEditorInstalled()
    {
        if (_isInstalled.HasValue)
        {
            return _isInstalled.Value;
        }

        try
        {
            var configuration = (ISetupConfiguration2)new SetupConfiguration();
            var instance = (ISetupInstance2)configuration.GetInstanceForCurrentProcess();
            _isInstalled = instance
                .GetPackages()
                .Any(package => string.Equals(package.GetId(), GraphDocumentComponentId, StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception ex)
        {
            // Setup configuration unavailable (unusual host, side-by-side layout, COM failure):
            // assume the editor is present rather than nagging on every run.
            ActivityLog.TryLogWarning(nameof(DgmlEditorDetector), $"Could not query the Visual Studio setup configuration: {ex.Message}");
            _isInstalled = true;
        }

        return _isInstalled.Value;
    }

    /// <summary>
    /// Shows a one-time (per session) message with install steps when the DGML editor is missing.
    /// Never blocks the operation: the .dgml file is still written and opened.
    /// </summary>
    public static async Task WarnIfMissingAsync(ToolkitPackage package)
    {
        if (_warned || IsDgmlEditorInstalled())
        {
            return;
        }

        _warned = true;
        ActivityLog.TryLogWarning(nameof(DgmlEditorDetector), "The DGML editor component is not installed; generated .dgml files will open as XML.");

        await VsMessageBoxHelper.ShowAsync(
            package,
            "The DGML editor is not installed in this Visual Studio instance, so the generated .dgml file will open as plain XML instead of a graph.\n\n" +
            "To install it: open Visual Studio Installer, click Modify, switch to Individual components, search for \"DGML editor\", check it, and click Modify.\n\n" +
            "The file is still generated and opened. This message is shown once per session.",
            OLEMSGICON.OLEMSGICON_WARNING).ConfigureAwait(false);
    }
}
