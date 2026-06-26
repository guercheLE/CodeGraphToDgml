using System.Diagnostics;
using System.IO;
using System.Text;
using CodeGraphToDgml.Core;
using Microsoft.VisualStudio.Shell.Interop;

namespace CodeGraphToDgml.Vsix;

internal sealed class TraverseDownToSequenceOperationService
{
    private readonly ToolkitPackage _package;
    private readonly HierarchyProviderRegistry _providerRegistry;
    private readonly OutputWindowLogger _outputWindowLogger;

    private TraverseDownToSequenceOperationService(ToolkitPackage package)
    {
        _package = package;
        _providerRegistry = new HierarchyProviderRegistry(package);
        _outputWindowLogger = new OutputWindowLogger(package);
    }

    public static TraverseDownToSequenceOperationService? Current { get; private set; }

    public static void Initialize(ToolkitPackage package)
    {
        Current = new TraverseDownToSequenceOperationService(package);
    }

    public bool SupportsFilePath(string? filePath)
    {
        return _providerRegistry.SupportsFilePath(filePath);
    }

    public async Task ExecuteAsync()
    {
        await _outputWindowLogger.WriteLineAsync($"[{DateTime.Now:HH:mm:ss.fff}] TraverseDownToSequence ExecuteAsync: started").ConfigureAwait(false);

        var options = General.Instance;
        using var progress = new OperationProgressController(_package);

        progress.Start("Code Graph to Sequence: resolving symbol...");

        try
        {
            var resolution = await _providerRegistry.TryResolveAsync(progress.Token).ConfigureAwait(false);

            if (resolution is null)
            {
                await _outputWindowLogger.WriteLineAsync("No supported symbol was found at the caret.").ConfigureAwait(true);
                await progress.FailAsync("Code Graph to Sequence: no supported symbol at the caret.").ConfigureAwait(true);
                await ShowMessageAsync(
                    "Place the caret on a C# or Visual Basic method, property, or event and try again.",
                    OLEMSGICON.OLEMSGICON_INFO).ConfigureAwait(true);
                return;
            }

            if (options.ShowDetailedOutput)
            {
                await _outputWindowLogger.WriteLineAsync($"Resolved subject: {resolution.Value.Subject.DisplayName}").ConfigureAwait(true);
            }

            progress.ReportStatus("Code Graph to Sequence: traversing callees...");
            var sequence = await resolution.Value.Provider.TraverseDownToSequenceAsync(
                resolution.Value.Subject,
                options.ToTraversalOptions(),
                progress.Progress,
                progress.Token).ConfigureAwait(false);

            if (options.ShowDetailedOutput)
            {
                await _outputWindowLogger.WriteLineAsync($"Traversal complete. Participants: {sequence.Participants.Count}, Root calls: {sequence.RootCalls.Count}.").ConfigureAwait(true);
            }

            progress.ReportStatus("Code Graph to Sequence: writing output...");

            var timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmssfff");
            var serializer = new MermaidSequenceSerializer();
            var format = options.SequenceDiagramOutputFormat;

            string? mdPath = null;
            string? htmlPath = null;

            await Task.Run(() =>
            {
                var tempDir = Path.GetTempPath();

                if (format == SequenceDiagramOutputFormat.Markdown || format == SequenceDiagramOutputFormat.Both)
                {
                    mdPath = Path.Combine(tempDir, $"CodeSequence-{timestamp}.md");
                    File.WriteAllText(mdPath, serializer.BuildMarkdown(sequence), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                }

                if (format == SequenceDiagramOutputFormat.Html || format == SequenceDiagramOutputFormat.Both)
                {
                    htmlPath = Path.Combine(tempDir, $"CodeSequence-{timestamp}.html");
                    File.WriteAllText(htmlPath, serializer.BuildHtml(sequence), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                }
            }).ConfigureAwait(false);

            if (options.ShowDetailedOutput)
            {
                if (mdPath is not null) await _outputWindowLogger.WriteLineAsync($"Markdown: {mdPath}").ConfigureAwait(true);
                if (htmlPath is not null) await _outputWindowLogger.WriteLineAsync($"HTML: {htmlPath}").ConfigureAwait(true);
            }

            if (options.ActivateDgmlWindow)
            {
                // Markdown: open inside Visual Studio for its built-in preview.
                // HTML: open in default browser (VS cannot execute JS in its HTML editor).
                if (mdPath is not null)
                {
                    await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(progress.Token);
                    DgmlDocumentService.OpenDocumentSafe(_package, mdPath).Show();
                }

                if (htmlPath is not null)
                {
                    OpenInDefaultBrowser(htmlPath);
                }
            }

            await progress.CompleteAsync("Code Graph to Sequence: completed.").ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            ActivityLog.TryLogInformation(nameof(TraverseDownToSequenceOperationService), "Traversal cancelled by user.");
            await _outputWindowLogger.WriteLineAsync("Traversal cancelled by user.").ConfigureAwait(true);
            await progress.FailAsync("Code Graph to Sequence: cancelled.").ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            ActivityLog.TryLogError(nameof(TraverseDownToSequenceOperationService), $"Unhandled error: {ex}");
            await _outputWindowLogger.WriteLineAsync($"Unhandled error: {ex}").ConfigureAwait(true);
            await progress.FailAsync("Code Graph to Sequence: failed.").ConfigureAwait(true);
            await ShowMessageAsync(ex.Message, OLEMSGICON.OLEMSGICON_CRITICAL).ConfigureAwait(true);
        }
    }

    private static void OpenInDefaultBrowser(string filePath)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = filePath,
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            ActivityLog.TryLogError(nameof(TraverseDownToSequenceOperationService), $"Failed to open browser: {ex}");
        }
    }

    private async Task ShowMessageAsync(string message, OLEMSGICON icon)
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

        VsShellUtilities.ShowMessageBox(
            _package,
            message,
            Vsix.Name,
            icon,
            OLEMSGBUTTON.OLEMSGBUTTON_OK,
            OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
    }
}
