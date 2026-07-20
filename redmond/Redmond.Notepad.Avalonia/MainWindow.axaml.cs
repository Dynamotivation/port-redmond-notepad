using Avalonia.Controls;
using Avalonia;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Redmond.Avalonia.Windowing;
using Redmond.Notepad.Core;

namespace Redmond.Notepad.Avalonia;

public partial class MainWindow : Window
{
    private readonly NotepadDocument _document = new();
    private readonly WindowAppearanceController _appearanceController;
    private WindowAppearanceOptions _appearance;
    private SettingsWindow? _settingsWindow;

    public MainWindow()
    {
        InitializeComponent();
        _appearance = NotepadSettingsStore.LoadAppearance();
        _appearanceController = WindowAppearanceController.Attach(this, WindowSurface, _appearance);
        _appearanceController.PresentationChanged += (_, _) => UpdateWindowPresentation();
        UpdateWindowPresentation();
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

        _settingsWindow = new SettingsWindow(_appearance);
        _settingsWindow.AppearanceChanged += OnAppearanceChanged;
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Show(this);
    }

    private void OnAppearanceChanged(object? sender, WindowAppearanceOptions options)
    {
        _appearance = options;
        _appearanceController.Apply(options);
        NotepadSettingsStore.SaveAppearance(options);
        UpdateWindowPresentation();
    }

    private void UpdateWindowPresentation()
    {
        var useNativeControls = _appearance.ControlStyle == WindowControlStyle.MacOS;
        var useHostedMacOSControls = OperatingSystem.IsMacOS() && useNativeControls;
        CaptionButtons.IsVisible = !useNativeControls;
        CaptionButtons.ControlStyle = _appearance.ControlStyle;
        ResizeHandles.IsVisible = _appearanceController.UsesCustomResizeHandles;
        TitleBarContent.Margin = useHostedMacOSControls
            ? new Thickness(64, 5, 0, 0)
            : new Thickness(8, 5, 0, 0);

        var dark = ActualThemeVariant == ThemeVariant.Dark;
        WindowSurface.Background = new SolidColorBrush(Color.Parse(
            _appearance.UseSystemBackdrop
                ? dark ? "#D9202027" : "#D9F3F3F3"
                : dark ? "#202027" : "#F3F3F3"));
    }

    private void OnTitleBarPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        for (var visual = e.Source as Visual; visual is not null; visual = visual.GetVisualParent())
        {
            if (visual is Button)
            {
                return;
            }
        }

        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
        }
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
