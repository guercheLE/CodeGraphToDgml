using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Microsoft.VisualStudio.PlatformUI;

namespace CodeGraphToDgml.Vsix;

internal sealed class DgmlDocumentPickerWindow : DialogWindow
{
    private readonly RadioButton _createNewRadioButton;
    private readonly RadioButton _useExistingRadioButton;
    private readonly ListBox _documentsListBox;

    public DgmlDocumentPickerWindow(IReadOnlyList<string> openDocuments)
    {
        Title = "Choose DGML target";
        Width = 540;
        Height = 340;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;

        HasDialogFrame = true;

        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var instructions = new TextBlock
        {
            Text = "Choose whether to create a new DGML document or reuse one of the currently open DGML documents.",
            Margin = new Thickness(12),
            TextWrapping = TextWrapping.Wrap
        };
        Grid.SetRow(instructions, 0);

        _createNewRadioButton = new RadioButton
        {
            Content = "Create new temporary DGML",
            IsChecked = true,
            Margin = new Thickness(12, 4, 12, 4)
        };
        Grid.SetRow(_createNewRadioButton, 1);

        _useExistingRadioButton = new RadioButton
        {
            Content = "Use existing open DGML",
            Margin = new Thickness(12, 8, 12, 4)
        };
        Grid.SetRow(_useExistingRadioButton, 2);

        _documentsListBox = new ListBox
        {
            Margin = new Thickness(32, 0, 12, 12),
            IsEnabled = false
        };
        _documentsListBox.SetBinding(UIElement.IsEnabledProperty, new System.Windows.Data.Binding(nameof(RadioButton.IsChecked)) { Source = _useExistingRadioButton });

        foreach (var document in openDocuments.OrderBy(Path.GetFileName, System.StringComparer.OrdinalIgnoreCase))
        {
            _documentsListBox.Items.Add(document);
        }

        if (_documentsListBox.Items.Count > 0)
        {
            _documentsListBox.SelectedIndex = 0;
        }

        Grid.SetRow(_documentsListBox, 3);

        var buttonPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(12)
        };
        Grid.SetRow(buttonPanel, 4);

        var okButton = new Button
        {
            Content = "OK",
            Width = 80,
            Margin = new Thickness(0, 0, 8, 0),
            IsDefault = true,
        };
        okButton.Click += (s, e) =>
        {
            if (_useExistingRadioButton.IsChecked == true && _documentsListBox.SelectedItem == null)
            {
                System.Windows.MessageBox.Show("Select an open DGML document or choose to create a new one.", Title, MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            DialogResult = true;
            Close();
        };

        var cancelButton = new Button
        {
            Content = "Cancel",
            Width = 80,
            IsCancel = true,
        };

        buttonPanel.Children.Add(okButton);
        buttonPanel.Children.Add(cancelButton);

        grid.Children.Add(instructions);
        grid.Children.Add(_createNewRadioButton);
        grid.Children.Add(_useExistingRadioButton);
        grid.Children.Add(_documentsListBox);
        grid.Children.Add(buttonPanel);

        Content = grid;
    }

    public bool CreateNew => _createNewRadioButton.IsChecked == true;

    public string? SelectedExistingDocument => _documentsListBox.SelectedItem as string;
}
