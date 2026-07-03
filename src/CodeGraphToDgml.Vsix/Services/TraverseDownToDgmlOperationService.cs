using CodeGraphToDgml.Core;
using CodeGraphToDgml.Vsix.Providers;
using EnvDTE;
using EnvDTE80;
using Microsoft.VisualStudio.Shell.Interop;

namespace CodeGraphToDgml.Vsix;

internal sealed class TraverseDownToDgmlOperationService
{
    private readonly ToolkitPackage _package;
    private readonly HierarchyProviderRegistry _providerRegistry;
    private readonly DgmlDocumentService _dgmlDocumentService;
    private readonly OutputWindowLogger _outputWindowLogger;

    private TraverseDownToDgmlOperationService(ToolkitPackage package)
    {
        _package = package;
        _providerRegistry = new HierarchyProviderRegistry(package);
        _dgmlDocumentService = new DgmlDocumentService(package);
        _outputWindowLogger = new OutputWindowLogger(package);
    }

    public static TraverseDownToDgmlOperationService? Current { get; private set; }

    public static void Initialize(ToolkitPackage package)
    {
        Current = new TraverseDownToDgmlOperationService(package);
    }

    public bool SupportsFilePath(string? filePath)
    {
        return _providerRegistry.SupportsFilePath(filePath);
    }

    public async Task ExecuteAsync()
    {
        await _outputWindowLogger.WriteLineAsync($"[{DateTime.Now:HH:mm:ss.fff}] TraverseDown ExecuteAsync: started").ConfigureAwait(false);

        var options = General.Instance;
        using var progress = new OperationProgressController(_package);

        progress.Start("Code Graph to DGML: resolving symbol...");

        try
        {
            var resolution = await _providerRegistry.TryResolveAsync(progress.Token).ConfigureAwait(false);

            if (resolution is null)
            {
                await _outputWindowLogger.WriteLineAsync("No supported symbol was found at the caret.").ConfigureAwait(true);
                await progress.FailAsync("Code Graph to DGML: no supported symbol at the caret.").ConfigureAwait(true);
                await VsMessageBoxHelper.ShowAsync(
                    _package,
                    "Place the caret on a C# or Visual Basic method, property, or event and try again.",
                    OLEMSGICON.OLEMSGICON_INFO).ConfigureAwait(true);
                return;
            }

            if (options.ShowDetailedOutput)
            {
                await _outputWindowLogger.WriteLineAsync($"Resolved subject: {resolution.Value.Subject.DisplayName}").ConfigureAwait(true);
            }

            progress.ReportStatus("Code Graph to DGML: choosing DGML target...");
            var targetDocument = await _dgmlDocumentService.SelectTargetDocumentAsync(options, progress.Token).ConfigureAwait(true);

            progress.ReportStatus("Code Graph to DGML: traversing callees...");
            var graph = await resolution.Value.Provider.TraverseDownAsync(
                resolution.Value.Subject,
                options.ToTraversalOptions(),
                progress.Progress,
                progress.Token).ConfigureAwait(false);

            if (options.ShowDetailedOutput)
            {
                await _outputWindowLogger.WriteLineAsync($"Traversal complete. Nodes: {graph.NodeCount}, Links: {graph.LinkCount}.").ConfigureAwait(true);
                await _outputWindowLogger.WriteLineAsync($"Target document: {targetDocument.FullPath}").ConfigureAwait(true);
            }

            graph.FlattenSingleChildNamespaces();

            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(progress.Token);

            progress.ReportStatus("Code Graph to DGML: updating DGML...");
            var existingDgml = targetDocument.ReadAllText();
            var replaceContents = options.UpdateMode == GraphUpdateMode.Replace;

            var mergedDgml = await Task.Run(() =>
            {
                var serializer = new DgmlSerializer();
                return serializer.Merge(existingDgml, graph, replaceContents, options.CollapseGroups, options.GraphDirection);
            }).ConfigureAwait(false);

            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(progress.Token);

            targetDocument.ReplaceAllText(mergedDgml);

            if (options.ActivateDgmlWindow)
            {
                targetDocument.Activate();
            }

            await progress.CompleteAsync("Code Graph to DGML: completed.").ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            ActivityLog.TryLogInformation(nameof(TraverseDownToDgmlOperationService), "Traversal cancelled by user.");
            await _outputWindowLogger.WriteLineAsync("Traversal cancelled by user.").ConfigureAwait(true);
            await progress.FailAsync("Code Graph to DGML: cancelled.").ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            ActivityLog.TryLogError(nameof(TraverseDownToDgmlOperationService), $"Unhandled error: {ex}");
            await _outputWindowLogger.WriteLineAsync($"Unhandled error: {ex}").ConfigureAwait(true);
            await progress.FailAsync("Code Graph to DGML: failed.").ConfigureAwait(true);
            await VsMessageBoxHelper.ShowAsync(_package, ex.Message, OLEMSGICON.OLEMSGICON_CRITICAL).ConfigureAwait(true);
        }
    }
}
