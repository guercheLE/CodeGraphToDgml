using System.Linq;
using CodeGraphToDgml.Core;
using CodeGraphToDgml.Vsix.Providers;
using EnvDTE;
using EnvDTE80;
using Microsoft.VisualStudio.Shell.Interop;

namespace CodeGraphToDgml.Vsix;

internal sealed class AllReferencesToDgmlOperationService
{
    private readonly ToolkitPackage _package;
    private readonly RoslynReferencesProvider _provider;
    private readonly DgmlDocumentService _dgmlDocumentService;
    private readonly OutputWindowLogger _outputWindowLogger;

    private AllReferencesToDgmlOperationService(ToolkitPackage package)
    {
        _package = package;
        _provider = new RoslynReferencesProvider(package);
        _dgmlDocumentService = new DgmlDocumentService(package);
        _outputWindowLogger = new OutputWindowLogger(package);
    }

    public static AllReferencesToDgmlOperationService? Current { get; private set; }

    public static void Initialize(ToolkitPackage package)
    {
        Current = new AllReferencesToDgmlOperationService(package);
    }

    public bool SupportsFilePath(string? filePath)
    {
        return _provider.CouldSupport(filePath);
    }

    public async Task ExecuteAsync()
    {
        await _outputWindowLogger.WriteLineAsync($"[{DateTime.Now:HH:mm:ss.fff}] AllReferences ExecuteAsync: started").ConfigureAwait(false);

        var options = General.Instance;
        using var progress = new OperationProgressController(_package);

        progress.Start("Code Graph to DGML: resolving symbol...");

        try
        {
            var subject = await _provider.TryResolveSubjectAsync(progress.Token).ConfigureAwait(false);

            if (subject is null)
            {
                await _outputWindowLogger.WriteLineAsync("No supported symbol was found at the caret.").ConfigureAwait(true);
                await progress.FailAsync("Code Graph to DGML: no supported symbol at the caret.").ConfigureAwait(true);
                await ShowMessageAsync(
                    "Place the caret on a C# or Visual Basic type or member (class, struct, interface, record, method, property, event, or field) and try again.",
                    OLEMSGICON.OLEMSGICON_INFO).ConfigureAwait(true);
                return;
            }

            if (options.ShowDetailedOutput)
            {
                await _outputWindowLogger.WriteLineAsync($"Resolved subject: {subject.DisplayName}").ConfigureAwait(true);
            }

            progress.ReportStatus("Code Graph to DGML: choosing DGML target...");
            var targetDocument = await SelectTargetDocumentAsync(options, progress.Token).ConfigureAwait(true);

            progress.ReportStatus("Code Graph to DGML: finding references...");
            var graph = await _provider.FindReferencesAsync(
                subject,
                options.ToTraversalOptions(),
                progress.Progress,
                progress.Token).ConfigureAwait(false);

            if (options.ShowDetailedOutput)
            {
                await _outputWindowLogger.WriteLineAsync($"Reference search complete. Nodes: {graph.NodeCount}, Links: {graph.LinkCount}.").ConfigureAwait(true);
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
            ActivityLog.TryLogInformation(nameof(AllReferencesToDgmlOperationService), "Reference search cancelled by user.");
            await _outputWindowLogger.WriteLineAsync("Reference search cancelled by user.").ConfigureAwait(true);
            await progress.FailAsync("Code Graph to DGML: cancelled.").ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            ActivityLog.TryLogError(nameof(AllReferencesToDgmlOperationService), $"Unhandled error: {ex}");
            await _outputWindowLogger.WriteLineAsync($"Unhandled error: {ex}").ConfigureAwait(true);
            await progress.FailAsync("Code Graph to DGML: failed.").ConfigureAwait(true);
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

        var picker = new DgmlDocumentPickerWindow(openDocuments.Select(document => document.FullPath).ToArray());
        if (picker.ShowDialog() != true)
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
