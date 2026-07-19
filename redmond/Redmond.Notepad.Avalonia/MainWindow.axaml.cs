using Avalonia.Controls;
using Avalonia;
using Avalonia.Interactivity;
using Redmond.Notepad.Core;

namespace Redmond.Notepad.Avalonia;

public partial class MainWindow : Window
{
    private readonly NotepadDocument _document = new();
    private SettingsWindow? _settingsWindow;

    public MainWindow()
    {
        InitializeComponent();
        Editor.TextChanged += OnEditorTextChanged;
        Editor.PropertyChanged += OnEditorPropertyChanged;
        Opened += OnOpened;
        RefreshDocumentStatus();
    }

    private void OnOpened(object? sender, EventArgs e) => Editor.Focus();

    private void OnSettingsClick(object? sender, RoutedEventArgs e)
    {
        if (_settingsWindow is not null)
        {
            _settingsWindow.Activate();
            return;
        }

        _settingsWindow = new SettingsWindow();
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Show(this);
    }

    private void OnEditorTextChanged(object? sender, TextChangedEventArgs e)
    {
        _document.ReplaceText(Editor.Text);
        RefreshDocumentStatus();
        RefreshCursorStatus();
    }

    private void OnEditorPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == TextBox.CaretIndexProperty)
        {
            RefreshCursorStatus();
        }
    }

    private void RefreshDocumentStatus()
    {
        var lineLabel = _document.LineCount == 1 ? "line" : "lines";
        var characterLabel = _document.CharacterCount == 1 ? "character" : "characters";
        DocumentSummary.Text = $"{_document.LineCount} {lineLabel} · {_document.CharacterCount} {characterLabel}";
        LineEndingStatus.Text = _document.LineEndingDisplayText;
    }

    private void RefreshCursorStatus()
    {
        var text = Editor.Text ?? string.Empty;
        var caret = Math.Clamp(Editor.CaretIndex, 0, text.Length);
        var line = 1;
        var column = 1;

        for (var index = 0; index < caret; index++)
        {
            if (text[index] == '\n')
            {
                line++;
                column = 1;
            }
            else
            {
                column++;
            }
        }

        CursorStatus.Text = $"Ln {line}, Col {column}";
    }
}
