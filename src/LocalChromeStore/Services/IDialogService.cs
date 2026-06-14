using System.Windows;
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
}
