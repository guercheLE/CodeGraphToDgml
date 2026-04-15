using System.Threading;
using System.Windows.Forms;

namespace CodeGraphToDgml.Vsix;

internal sealed class TraversalProgressForm : Form
{
    private readonly CancellationTokenSource _cancellationTokenSource;
    private readonly Label _messageLabel;
    private readonly Button _cancelButton;

    public TraversalProgressForm(CancellationTokenSource cancellationTokenSource)
    {
        _cancellationTokenSource = cancellationTokenSource;

        Text = Vsix.Name;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterScreen;
        MinimizeBox = false;
        MaximizeBox = false;
        ControlBox = false;
        ShowInTaskbar = false;
        Width = 460;
        Height = 130;

        _messageLabel = new Label
        {
            Left = 12,
            Top = 12,
            Width = 420,
            Height = 16,
            Text = "Preparing traversal...",
        };

        var progressBar = new ProgressBar
        {
            Left = 12,
            Top = 32,
            Width = 420,
            Style = ProgressBarStyle.Marquee,
            MarqueeAnimationSpeed = 35,
        };

        _cancelButton = new Button
        {
            Left = 332,
            Top = 64,
            Width = 100,
            Text = "Cancel",
        };
        _cancelButton.Click += CancelButton_Click;

        Controls.Add(_messageLabel);
        Controls.Add(progressBar);
        Controls.Add(_cancelButton);
    }

    public void UpdateMessage(string message)
    {
        if (IsDisposed)
        {
            return;
        }

        if (InvokeRequired)
        {
            BeginInvoke(new Action<string>(UpdateMessage), message);
            return;
        }

        _messageLabel.Text = message;
    }

    private void CancelButton_Click(object? sender, EventArgs e)
    {
        if (_cancellationTokenSource.IsCancellationRequested)
        {
            return;
        }

        _cancellationTokenSource.Cancel();
        _cancelButton.Enabled = false;
        _messageLabel.Text = "Cancelling...";
    }
}
