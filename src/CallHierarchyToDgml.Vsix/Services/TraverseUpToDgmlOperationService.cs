using System.Linq;
using CallHierarchyToDgml.Core;
using CallHierarchyToDgml.Vsix.Providers;
using EnvDTE;
using EnvDTE80;
using Microsoft.VisualStudio.Shell.Interop;

namespace CallHierarchyToDgml.Vsix;

internal sealed class TraverseUpToDgmlOperationService
{
    private readonly ToolkitPackage _package;
    private readonly HierarchyProviderRegistry _providerRegistry;
    private readonly DgmlDocumentService _dgmlDocumentService;
    private readonly OutputWindowLogger _outputWindowLogger;

    private TraverseUpToDgmlOperationService(ToolkitPackage package)
    {
        _package = package;
        _providerRegistry = new HierarchyProviderRegistry(package);
        _dgmlDocumentService = new DgmlDocumentService(package);
        _outputWindowLogger = new OutputWindowLogger(package);
    }

    public static TraverseUpToDgmlOperationService? Current { get; private set; }

    public static void Initialize(ToolkitPackage package)
    {
        Current = new TraverseUpToDgmlOperationService(package);
    }

    public bool SupportsFilePath(string? filePath)
    {
        return _providerRegistry.SupportsFilePath(filePath);
    }

    public async Task ExecuteAsync()
    {
        await _outputWindowLogger.WriteLineAsync($"[{DateTime.Now:HH:mm:ss.fff}] ExecuteAsync: started").ConfigureAwait(false);

        var options = General.Instance;
        using var progress = new OperationProgressController(_package);

        progress.Start("Call Hierarchy to DGML: resolving symbol...");

        try
        {
            var resolution = await _providerRegistry.TryResolveAsync(progress.Token).ConfigureAwait(false);

            if (resolution is null)
            {
                await _outputWindowLogger.WriteLineAsync("No supported symbol was found at the caret.").ConfigureAwait(true);
                await progress.FailAsync("Call Hierarchy to DGML: no supported symbol at the caret.").ConfigureAwait(true);
                await ShowMessageAsync(
                    "Place the caret on a C# or Visual Basic method, property, or event and try again.",
                    OLEMSGICON.OLEMSGICON_INFO).ConfigureAwait(true);
                return;
            }

            if (options.ShowDetailedOutput)
            {
                await _outputWindowLogger.WriteLineAsync($"Resolved subject: {resolution.Value.Subject.DisplayName}").ConfigureAwait(true);
            }

            progress.ReportStatus("Call Hierarchy to DGML: choosing DGML target...");
            var targetDocument = await SelectTargetDocumentAsync(options, progress.Token).ConfigureAwait(true);

            progress.ReportStatus("Call Hierarchy to DGML: traversing callers...");
            var graph = await resolution.Value.Provider.TraverseUpAsync(
                resolution.Value.Subject,
                options.ToTraversalOptions(),
                progress.Progress,
                progress.Token).ConfigureAwait(false);

            if (options.ShowDetailedOutput)
            {
                await _outputWindowLogger.WriteLineAsync($"Traversal complete. Nodes: {graph.NodeCount}, Links: {graph.LinkCount}.").ConfigureAwait(true);
                await _outputWindowLogger.WriteLineAsync($"Target document: {targetDocument.FullPath}").ConfigureAwait(true);
            }

            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(progress.Token);

            progress.ReportStatus("Call Hierarchy to DGML: updating DGML...");
            var serializer = new DgmlSerializer();
            var mergedDgml = serializer.Merge(
                targetDocument.ReadAllText(),
                graph,
                options.UpdateMode == GraphUpdateMode.Replace);

            targetDocument.ReplaceAllText(mergedDgml);

            if (options.ActivateDgmlWindow)
            {
                targetDocument.Activate();
            }

            await progress.CompleteAsync("Call Hierarchy to DGML: completed.").ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            ActivityLog.TryLogInformation(nameof(TraverseUpToDgmlOperationService), "Traversal cancelled by user.");
            await _outputWindowLogger.WriteLineAsync("Traversal cancelled by user.").ConfigureAwait(true);
            await progress.FailAsync("Call Hierarchy to DGML: cancelled.").ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            ActivityLog.TryLogError(nameof(TraverseUpToDgmlOperationService), $"Unhandled error: {ex}");
            await _outputWindowLogger.WriteLineAsync($"Unhandled error: {ex}").ConfigureAwait(true);
            await progress.FailAsync("Call Hierarchy to DGML: failed.").ConfigureAwait(true);
            await ShowMessageAsync(ex.Message, OLEMSGICON.OLEMSGICON_CRITICAL).ConfigureAwait(true);
        }
    }

    private async Task<DgmlDocumentSession> SelectTargetDocumentAsync(General options, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var openDocuments = await _dgmlDocumentService.GetOpenDgmlDocumentsAsync().ConfigureAwait(true);

        if (openDocuments.Count == 0 || options.OpenBehavior == DgmlDocumentOpenBehavior.AlwaysCreateNewTemporary)
        {
            return await _dgmlDocumentService.OpenOrCreateTemporaryDocumentAsync().ConfigureAwait(true);
        }

        if (options.OpenBehavior == DgmlDocumentOpenBehavior.ReuseActiveIfOpen)
        {
            var activePath = await _dgmlDocumentService.GetActiveDgmlDocumentPathAsync().ConfigureAwait(true);
            var selectedPath = activePath ?? openDocuments.Select(document => document.FullPath).First();
            return await _dgmlDocumentService.OpenDocumentSessionAsync(selectedPath).ConfigureAwait(true);
        }

        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

        using var picker = new DgmlDocumentPickerForm(openDocuments.Select(document => document.FullPath).ToArray());
        if (picker.ShowDialog() != System.Windows.Forms.DialogResult.OK)
        {
            throw new OperationCanceledException();
        }

        if (picker.CreateNew)
        {
            return await _dgmlDocumentService.OpenOrCreateTemporaryDocumentAsync().ConfigureAwait(true);
        }

        if (string.IsNullOrWhiteSpace(picker.SelectedExistingDocument))
        {
            throw new OperationCanceledException();
        }

        return await _dgmlDocumentService.OpenDocumentSessionAsync(picker.SelectedExistingDocument!).ConfigureAwait(true);
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
