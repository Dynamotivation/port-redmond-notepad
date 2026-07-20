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

    public MainWindow()
    {
        InitializeComponent();
        _appearance = NotepadSettingsStore.LoadAppearance();
        _appearanceController = WindowAppearanceController.Attach(this, WindowSurface, _appearance);
        _appearanceController.PresentationChanged += (_, _) => UpdateWindowPresentation();
        SettingsPage.SetAppearance(_appearance);
        SettingsPage.AppearanceChanged += OnAppearanceChanged;
        UpdateWindowPresentation();
        Editor.TextChanged += OnEditorTextChanged;
        Editor.PropertyChanged += OnEditorPropertyChanged;
        Opened += OnOpened;
        RefreshDocumentStatus();
    }

    private void OnOpened(object? sender, EventArgs e) => Editor.Focus();

    private void OnSettingsClick(object? sender, RoutedEventArgs e)
    {
        SettingsPage.SetAppearance(_appearance);
        DocumentTitleBar.IsVisible = false;
        DocumentPage.IsVisible = false;
        SettingsTitleBar.IsVisible = true;
        SettingsPage.IsVisible = true;
    }

    private void OnBackFromSettingsClick(object? sender, RoutedEventArgs e)
    {
        SettingsTitleBar.IsVisible = false;
        SettingsPage.IsVisible = false;
        DocumentTitleBar.IsVisible = true;
        DocumentPage.IsVisible = true;
        Editor.Focus();
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
        var captionWidth = useNativeControls
            ? 0
            : _appearance.ControlStyle == WindowControlStyle.Windows10 ? 138 : 180;
        var leftInset = useHostedMacOSControls ? 64 : 8;
        TitleBarContent.Margin = new Thickness(leftInset, 5, captionWidth, 0);
        SettingsTitleBarContent.Margin = new Thickness(leftInset, 0, captionWidth, 0);

        var dark = ActualThemeVariant == ThemeVariant.Dark;
        WindowSurface.Background = new SolidColorBrush(Color.Parse(
            _appearance.UseSystemBackdrop
                ? dark ? "#18383D40" : "#18F3F3F3"
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
