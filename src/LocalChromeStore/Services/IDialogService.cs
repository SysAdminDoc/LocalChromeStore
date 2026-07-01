using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;

namespace LocalChromeStore.Services;

public enum DialogIcon { None, Question, Warning, Error }

/// <summary>
/// Abstraction over the user-facing dialog surfaces (confirmations, alerts, file pickers, clipboard).
/// Lets the view models be exercised headlessly: tests inject a fake that scripts the user's answers
/// instead of popping real WPF windows. The production implementation is <see cref="DialogService"/>.
/// </summary>
public interface IDialogService
{
    /// <summary>Yes/No confirmation. Returns true on Yes.</summary>
    bool Confirm(string message, string title, DialogIcon icon = DialogIcon.Question);

    /// <summary>Informational/error message with a single OK.</summary>
    void Alert(string message, string title, DialogIcon icon = DialogIcon.None);

    /// <summary>Save-file picker. Returns the chosen path, or null if cancelled.</summary>
    string? SaveFile(string title, string filter, string defaultFileName, string? initialDirectory = null, string? defaultExt = null);

    /// <summary>Open-file picker. Returns the chosen path, or null if cancelled.</summary>
    string? OpenFile(string title, string filter, string? initialDirectory = null, string? defaultExt = null);

    /// <summary>Single-line text prompt. Returns the entered text, or null if cancelled.</summary>
    string? PromptText(string title, string message, string defaultValue = "");

    /// <summary>Copies text to the system clipboard.</summary>
    void SetClipboardText(string text);
}

/// <summary>WPF implementation backed by <see cref="MessageBox"/>, the Win32 file dialogs, and the clipboard.</summary>
public sealed class DialogService : IDialogService
{
    public bool Confirm(string message, string title, DialogIcon icon = DialogIcon.Question) =>
        MessageBox.Show(message, title, MessageBoxButton.YesNo, Map(icon)) == MessageBoxResult.Yes;

    public void Alert(string message, string title, DialogIcon icon = DialogIcon.None) =>
        MessageBox.Show(message, title, MessageBoxButton.OK, Map(icon));

    public string? SaveFile(string title, string filter, string defaultFileName, string? initialDirectory = null, string? defaultExt = null)
    {
        var dlg = new SaveFileDialog
        {
            Title = title,
            FileName = defaultFileName,
            Filter = filter,
            DefaultExt = defaultExt ?? string.Empty,
            InitialDirectory = initialDirectory ?? string.Empty
        };
        return ShowOwned(dlg) ? dlg.FileName : null;
    }

    public string? OpenFile(string title, string filter, string? initialDirectory = null, string? defaultExt = null)
    {
        var dlg = new OpenFileDialog
        {
            Title = title,
            Filter = filter,
            DefaultExt = defaultExt ?? string.Empty,
            InitialDirectory = initialDirectory ?? string.Empty,
            CheckFileExists = true
        };
        return ShowOwned(dlg) ? dlg.FileName : null;
    }

    public string? PromptText(string title, string message, string defaultValue = "") =>
        TextPromptDialog.Show(title, message, defaultValue);

    public void SetClipboardText(string text) => Clipboard.SetText(text);

    private static bool ShowOwned(FileDialog dlg)
    {
        var owner = Application.Current?.MainWindow;
        return (owner != null ? dlg.ShowDialog(owner) : dlg.ShowDialog()) == true;
    }

    private static MessageBoxImage Map(DialogIcon icon) => icon switch
    {
        DialogIcon.Question => MessageBoxImage.Question,
        DialogIcon.Warning => MessageBoxImage.Warning,
        DialogIcon.Error => MessageBoxImage.Error,
        _ => MessageBoxImage.None
    };

    private sealed class TextPromptDialog : Window
    {
        private readonly TextBox _textBox;

        private TextPromptDialog(string title, string message, string defaultValue)
        {
            Title = title;
            Width = 560;
            MinWidth = 420;
            SizeToContent = SizeToContent.Height;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.NoResize;
            if (Application.Current?.TryFindResource("BaseBrush") is SolidColorBrush bg)
                Background = bg;
            if (Application.Current?.TryFindResource("TextBrush") is SolidColorBrush fg)
                Foreground = fg;

            var panel = new Grid { Margin = new Thickness(18) };
            panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var messageBlock = new TextBlock
            {
                Text = message,
                TextWrapping = TextWrapping.Wrap,
                Foreground = Foreground,
                Margin = new Thickness(0, 0, 0, 12)
            };
            Grid.SetRow(messageBlock, 0);
            panel.Children.Add(messageBlock);

            _textBox = new TextBox
            {
                Text = defaultValue,
                MinWidth = 500,
                Margin = new Thickness(0, 0, 0, 16)
            };
            Grid.SetRow(_textBox, 1);
            panel.Children.Add(_textBox);

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            var cancel = new Button { Content = "Cancel", MinWidth = 82, Margin = new Thickness(0, 0, 8, 0), IsCancel = true };
            var ok = new Button { Content = "OK", MinWidth = 82, IsDefault = true };
            ok.Click += (_, _) => { DialogResult = true; Close(); };
            buttons.Children.Add(cancel);
            buttons.Children.Add(ok);
            Grid.SetRow(buttons, 2);
            panel.Children.Add(buttons);

            Content = panel;
            Loaded += (_, _) =>
            {
                _textBox.Focus();
                _textBox.SelectAll();
            };
        }

        public static string? Show(string title, string message, string defaultValue)
        {
            var owner = Application.Current?.MainWindow;
            var dialog = new TextPromptDialog(title, message, defaultValue);
            if (owner != null) dialog.Owner = owner;
            return dialog.ShowDialog() == true ? dialog._textBox.Text : null;
        }
    }
}
