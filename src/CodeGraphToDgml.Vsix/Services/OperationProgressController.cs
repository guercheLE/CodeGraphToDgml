using System.Threading;
using CodeGraphToDgml.Core;
using Microsoft.VisualStudio.Shell.Interop;

namespace CodeGraphToDgml.Vsix;

internal sealed class OperationProgressController : IDisposable
{
    private readonly ToolkitPackage _package;
    private readonly TraversalProgressForm _form;
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private bool _completed;
    private bool _dialogStarted;

    public OperationProgressController(ToolkitPackage package)
    {
        _package = package;
        _form = new TraversalProgressForm(_cancellationTokenSource);
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

    public void Dispose()
    {
        if (!_completed && !_cancellationTokenSource.IsCancellationRequested)
        {
            _cancellationTokenSource.Cancel();
        }

        _cancellationTokenSource.Dispose();

        if (!_form.IsDisposed)
        {
            try
            {
                if (_form.InvokeRequired)
                {
                    _form.BeginInvoke(new Action(() => _form.Close()));
                }
                else
                {
                    _form.Close();
                }
            }
            catch
            {
            }
        }
    }

    private async Task UpdateUiAsync(string message, bool ensureVisible)
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

        if (ensureVisible && !_dialogStarted)
        {
            _dialogStarted = true;
            if (!_form.IsDisposed)
            {
                _form.Show();
            }
        }

        if (!_form.IsDisposed)
        {
            _form.UpdateMessage(message);
        }

        var statusBar = await _package.GetServiceAsync(typeof(SVsStatusbar)).ConfigureAwait(true) as IVsStatusbar;
        statusBar?.SetText(message);
    }

    private async Task CloseFormAsync()
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

        if (!_form.IsDisposed)
        {
            _form.Close();
        }
    }
}
