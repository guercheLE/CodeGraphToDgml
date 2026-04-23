using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace CodeGraphToDgml.Vsix;

internal sealed class DgmlDocumentPickerForm : Form
{
    private readonly RadioButton _createNewRadioButton;
    private readonly RadioButton _useExistingRadioButton;
    private readonly ListBox _documentsListBox;

    public DgmlDocumentPickerForm(IReadOnlyList<string> openDocuments)
    {
        Text = "Choose DGML target";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterScreen;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        Width = 540;
        Height = 340;

        var instructions = new Label
        {
            Left = 12,
            Top = 12,
            Width = 500,
            Height = 32,
            Text = "Choose whether to create a new DGML document or reuse one of the currently open DGML documents.",
        };

        _createNewRadioButton = new RadioButton
        {
            Left = 12,
            Top = 52,
            Width = 240,
            Text = "Create new temporary DGML",
            Checked = true,
        };
        _createNewRadioButton.CheckedChanged += SelectionModeChanged;

        _useExistingRadioButton = new RadioButton
        {
            Left = 12,
            Top = 78,
            Width = 220,
            Text = "Use existing open DGML",
        };
        _useExistingRadioButton.CheckedChanged += SelectionModeChanged;

        _documentsListBox = new ListBox
        {
            Left = 12,
            Top = 112,
            Width = 500,
            Height = 150,
            Enabled = false,
            DrawMode = DrawMode.OwnerDrawFixed,
            IntegralHeight = false,
        };
        _documentsListBox.DrawItem += DocumentsListBox_DrawItem;

        foreach (var document in openDocuments.OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase))
        {
            _documentsListBox.Items.Add(document);
        }

        if (_documentsListBox.Items.Count > 0)
        {
            _documentsListBox.SelectedIndex = 0;
        }

        var okButton = new Button
        {
            Left = 332,
            Top = 272,
            Width = 80,
            Text = "OK",
            DialogResult = DialogResult.OK,
        };

        var cancelButton = new Button
        {
            Left = 432,
            Top = 272,
            Width = 80,
            Text = "Cancel",
            DialogResult = DialogResult.Cancel,
        };

        AcceptButton = okButton;
        CancelButton = cancelButton;

        Controls.Add(instructions);
        Controls.Add(_createNewRadioButton);
        Controls.Add(_useExistingRadioButton);
        Controls.Add(_documentsListBox);
        Controls.Add(okButton);
        Controls.Add(cancelButton);
    }

    public bool CreateNew => _createNewRadioButton.Checked;

    public string? SelectedExistingDocument => _documentsListBox.SelectedItem as string;

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (DialogResult == DialogResult.OK && _useExistingRadioButton.Checked && SelectedExistingDocument is null)
        {
            System.Windows.Forms.MessageBox.Show(this, "Select an open DGML document or choose to create a new one.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            e.Cancel = true;
            return;
        }

        base.OnFormClosing(e);
    }

    private void SelectionModeChanged(object? sender, EventArgs e)
    {
        _documentsListBox.Enabled = _useExistingRadioButton.Checked;
        _documentsListBox.Invalidate();
    }

    private void DocumentsListBox_DrawItem(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0)
        {
            return;
        }

        var listBox = (ListBox)sender!;
        var enabled = listBox.Enabled;
        var isSelected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;

        Color backColor;
        Color foreColor;

        if (!enabled)
        {
            backColor = SystemColors.Control;
            foreColor = SystemColors.GrayText;
        }
        else if (isSelected)
        {
            backColor = SystemColors.Highlight;
            foreColor = SystemColors.HighlightText;
        }
        else
        {
            backColor = listBox.BackColor;
            foreColor = listBox.ForeColor;
        }

        using (var backBrush = new SolidBrush(backColor))
        {
            e.Graphics.FillRectangle(backBrush, e.Bounds);
        }

        var text = listBox.Items[e.Index]?.ToString() ?? string.Empty;
        TextRenderer.DrawText(
            e.Graphics,
            text,
            e.Font ?? listBox.Font,
            e.Bounds,
            foreColor,
            backColor,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);

        if (enabled && isSelected)
        {
            e.DrawFocusRectangle();
        }
    }
}
