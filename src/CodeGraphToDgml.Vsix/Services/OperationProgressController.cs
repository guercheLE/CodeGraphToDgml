using System.Threading;
using CodeGraphToDgml.Core;
using Microsoft.VisualStudio.Shell.Interop;

namespace CodeGraphToDgml.Vsix;

internal sealed class OperationProgressController : IDisposable, IVsThreadedWaitDialogCallback
{
    private readonly ToolkitPackage _package;
    private IVsThreadedWaitDialog4? _waitDialog;
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private bool _completed;
    private bool _dialogStarted;

    public OperationProgressController(ToolkitPackage package)
    {
        _package = package;
        Progress = new Progress<TraversalProgress>(value =>
        {
            var message = $"{value.Stage}: depth {value.Depth}, nodes {value.NodeCount}";
            if (!string.IsNullOrWhiteSpace(value.CurrentSymbol))
            {
                message = $"{message} - {value.CurrentSymbol}";
            }

            _ = UpdateUiAsync(message, ensureVisible: false);
        });
    }

    public CancellationToken Token => _cancellationTokenSource.Token;

    public IProgress<TraversalProgress> Progress { get; }

    public void Start(string message)
    {
        _ = UpdateUiAsync(message, ensureVisible: true);
    }

    public void ReportStatus(string message)
    {
        _ = UpdateUiAsync(message, ensureVisible: false);
    }

    public async Task CompleteAsync(string message)
    {
        _completed = true;
        await UpdateUiAsync(message, ensureVisible: false).ConfigureAwait(true);
        await CloseFormAsync().ConfigureAwait(true);
    }

    public async Task FailAsync(string message)
    {
        await UpdateUiAsync(message, ensureVisible: false).ConfigureAwait(true);
        await CloseFormAsync().ConfigureAwait(true);
    }

    void IVsThreadedWaitDialogCallback.OnCanceled()
    {
        try
        {
            if (!_cancellationTokenSource.IsCancellationRequested)
                _cancellationTokenSource.Cancel();
        }
        catch (ObjectDisposedException) { }
    }

    public void Dispose()
    {
        if (!_completed && !_cancellationTokenSource.IsCancellationRequested)
        {
            _cancellationTokenSource.Cancel();
        }

        _cancellationTokenSource.Dispose();

        _ = CloseFormAsync();
    }

    private async Task UpdateUiAsync(string message, bool ensureVisible)
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

        if (ensureVisible && !_dialogStarted)
        {
            var dialogFactory = await _package.GetServiceAsync(typeof(SVsThreadedWaitDialogFactory)).ConfigureAwait(true) as IVsThreadedWaitDialogFactory;
            if (dialogFactory != null)
            {
                dialogFactory.CreateInstance(out var waitDialog);
                _waitDialog = waitDialog as IVsThreadedWaitDialog4;
                if (waitDialog is IVsThreadedWaitDialog3 dialog3)
                {
                    dialog3.StartWaitDialogWithCallback(
                        "Code Graph to DGML", message, null, null, "Cancelling...",
                        fIsCancelable: true, iDelayToShowDialog: 0, fShowProgress: true,
                        iTotalSteps: 0, iCurrentStep: 0, pCallback: this);
                }
                else
                {
                    waitDialog?.StartWaitDialog("Code Graph to DGML", message, null, null, "Cancelling...", 0, true, true);
                }
            }
            _dialogStarted = true;
        }

        if (_waitDialog != null)
        {
            _waitDialog.UpdateProgress(message, null, null, 0, 0, false, out _);
        }

        var statusBar = await _package.GetServiceAsync(typeof(SVsStatusbar)).ConfigureAwait(true) as IVsStatusbar;
        statusBar?.SetText(message);
    }

    private async Task CloseFormAsync()
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

        if (_waitDialog != null)
        {
            _waitDialog.EndWaitDialog(out _);
            _waitDialog = null;
        }
    }
}
