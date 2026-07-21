using Avalonia.Controls;
using Avalonia;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using System.Collections.ObjectModel;
using System.ComponentModel;
using Redmond.Avalonia.Controls;
using Redmond.Avalonia.Windowing;
using Redmond.Notepad.Core;

namespace Redmond.Notepad.Avalonia;

public partial class MainWindow : Window
{
    private const double TabWidth = 210;
    private const double TabScrollStep = 8;
    private const double TabScrollButtonWidth = 28;

    private readonly NotepadWorkspace _workspace = new();
    private readonly ObservableCollection<NotepadTabItem> _tabs = [];
    private readonly WindowAppearanceController _appearanceController;
    private WindowAppearanceOptions _appearance;
    private AppThemePreference _themePreference;
    private bool _isLoadingTab;
    private bool _isUpdatingTabScrollButtons;

    public MainWindow()
    {
        InitializeComponent();
        _appearance = NotepadSettingsStore.LoadAppearance();
        _themePreference = NotepadSettingsStore.LoadThemePreference();
        _appearanceController = WindowAppearanceController.Attach(this, WindowSurface, _appearance);
        _appearanceController.PresentationChanged += (_, _) => UpdateWindowPresentation();
        SettingsPage.SetAppearance(_appearance);
        SettingsPage.SetThemePreference(_themePreference);
        SettingsPage.AppearanceChanged += OnAppearanceChanged;
        SettingsPage.ThemeChanged += OnThemeChanged;
        foreach (var tab in _workspace.Tabs)
        {
            _tabs.Add(new NotepadTabItem(tab));
        }
        TabsList.ItemsSource = _tabs;
        TabsList.SelectedItem = _tabs.Single(item => ReferenceEquals(item.Tab, _workspace.SelectedTab));
        RefreshTabSeparators();
        UpdateWindowPresentation();
        Editor.TextChanged += OnEditorTextChanged;
        Editor.PropertyChanged += OnEditorPropertyChanged;
        Opened += OnOpened;
        PropertyChanged += OnWindowPropertyChanged;
        LoadSelectedTab();
        RefreshDocumentStatus();
    }

    private void OnOpened(object? sender, EventArgs e)
    {
        Editor.Focus();
        ScheduleTabViewportUpdate(ensureSelectedTabIsVisible: true);
    }

    private void OnSettingsClick(object? sender, RoutedEventArgs e)
    {
        SettingsPage.SetAppearance(_appearance);
        SettingsPage.SetThemePreference(_themePreference);
        DocumentTitleBar.IsVisible = false;
        DocumentPage.IsVisible = false;
        SettingsTitleBar.IsVisible = true;
        SettingsPage.IsVisible = true;
        UpdateWindowPresentation();
    }

    private void OnBackFromSettingsClick(object? sender, RoutedEventArgs e)
    {
        SettingsTitleBar.IsVisible = false;
        SettingsPage.IsVisible = false;
        DocumentTitleBar.IsVisible = true;
        DocumentPage.IsVisible = true;
        UpdateWindowPresentation();
        Editor.Focus();
    }

    private void OnAppearanceChanged(object? sender, WindowAppearanceOptions options)
    {
        _appearance = options;
        _appearanceController.Apply(options);
        NotepadSettingsStore.SaveAppearance(options);
        UpdateWindowPresentation();
    }

    private void OnThemeChanged(object? sender, AppThemePreference preference)
    {
        _themePreference = preference;
        App.ApplyThemePreference(preference);
        NotepadSettingsStore.SaveThemePreference(preference);
        UpdateWindowPresentation();
    }

    private void OnWindowPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == ActualThemeVariantProperty)
        {
            UpdateWindowPresentation();
        }
    }

    private void OnNewTabClick(object? sender, RoutedEventArgs e)
    {
        var tab = _workspace.CreateTab();
        var item = new NotepadTabItem(tab);
        _tabs.Add(item);
        TabsList.SelectedItem = item;
        RefreshTabSeparators();
        LoadSelectedTab();
        ScheduleTabViewportUpdate(ensureSelectedTabIsVisible: true);
    }

    private void OnCloseTabClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { CommandParameter: NotepadTabItem item })
        {
            return;
        }

        var tab = item.Tab;
        var replacement = _workspace.CloseTab(tab);
        _tabs.Remove(item);
        var replacementItem = _tabs.FirstOrDefault(candidate => ReferenceEquals(candidate.Tab, replacement));
        if (replacementItem is null)
        {
            replacementItem = new NotepadTabItem(replacement);
            _tabs.Add(replacementItem);
        }

        TabsList.SelectedItem = replacementItem;
        RefreshTabSeparators();
        LoadSelectedTab();
        ScheduleTabViewportUpdate(ensureSelectedTabIsVisible: true);
        e.Handled = true;
    }

    private void OnTabSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (TabsList.SelectedItem is not NotepadTabItem item || ReferenceEquals(item.Tab, _workspace.SelectedTab))
        {
            return;
        }

        _workspace.SelectTab(item.Tab);
        RefreshTabSeparators();
        LoadSelectedTab();
        ScheduleTabViewportUpdate(ensureSelectedTabIsVisible: true);
    }

    private void OnTabScrollLeftClick(object? sender, RoutedEventArgs e)
    {
        ScrollTabsBy(-TabScrollStep);
    }

    private void OnTabScrollRightClick(object? sender, RoutedEventArgs e)
    {
        ScrollTabsBy(TabScrollStep);
    }

    private void OnTabScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        UpdateTabScrollButtons();
    }

    private void ScrollTabsBy(double delta)
    {
        var maximumOffset = Math.Max(0, TabScrollViewer.Extent.Width - TabScrollViewer.Viewport.Width);
        var offset = Math.Clamp(TabScrollViewer.Offset.X + delta, 0, maximumOffset);
        TabScrollViewer.Offset = new Vector(offset, 0);
        UpdateTabScrollButtons();
    }

    private void ScheduleTabViewportUpdate(bool ensureSelectedTabIsVisible = false)
    {
        Dispatcher.UIThread.Post(() =>
        {
            UpdateTabScrollButtons();
            if (ensureSelectedTabIsVisible)
            {
                EnsureSelectedTabVisible();
            }
        }, DispatcherPriority.Loaded);
    }

    private void UpdateTabScrollButtons()
    {
        if (_isUpdatingTabScrollButtons)
        {
            return;
        }

        var widthReservedByVisibleButtons = TabScrollLeftButton.IsVisible
            ? TabScrollButtonWidth * 2
            : 0;
        var availableWidthWithoutButtons = TabScrollViewer.Viewport.Width + widthReservedByVisibleButtons;
        var hasOverflow = TabScrollViewer.Extent.Width > availableWidthWithoutButtons + 0.5;

        if (TabScrollLeftButton.IsVisible != hasOverflow)
        {
            _isUpdatingTabScrollButtons = true;
            TabScrollLeftButton.IsVisible = hasOverflow;
            TabScrollRightButton.IsVisible = hasOverflow;
            if (!hasOverflow)
            {
                TabScrollViewer.Offset = default;
            }
            _isUpdatingTabScrollButtons = false;
            ScheduleTabViewportUpdate();
            return;
        }

        var maximumOffset = Math.Max(0, TabScrollViewer.Extent.Width - TabScrollViewer.Viewport.Width);
        TabScrollLeftButton.IsEnabled = hasOverflow && TabScrollViewer.Offset.X > 0.5;
        TabScrollRightButton.IsEnabled = hasOverflow && TabScrollViewer.Offset.X < maximumOffset - 0.5;
    }

    private void EnsureSelectedTabVisible()
    {
        if (TabsList.SelectedItem is not NotepadTabItem selectedItem)
        {
            return;
        }

        var selectedIndex = _tabs.IndexOf(selectedItem);
        if (selectedIndex < 0 || TabScrollViewer.Viewport.Width <= 0)
        {
            return;
        }

        var tabStart = selectedIndex * TabWidth;
        var tabEnd = tabStart + TabWidth;
        var offset = TabScrollViewer.Offset.X;

        if (tabStart < offset)
        {
            offset = tabStart;
        }
        else if (tabEnd > offset + TabScrollViewer.Viewport.Width)
        {
            offset = tabEnd - TabScrollViewer.Viewport.Width;
        }

        var maximumOffset = Math.Max(0, TabScrollViewer.Extent.Width - TabScrollViewer.Viewport.Width);
        TabScrollViewer.Offset = new Vector(Math.Clamp(offset, 0, maximumOffset), 0);
        UpdateTabScrollButtons();
    }

    private void RefreshTabSeparators()
    {
        var selectedIndex = TabsList.SelectedItem is NotepadTabItem selected
            ? _tabs.IndexOf(selected)
            : -1;

        for (var index = 0; index < _tabs.Count; index++)
        {
            _tabs[index].ShowLeadingSeparator = index > 0
                && index != selectedIndex;
        }
    }

    private void LoadSelectedTab()
    {
        _isLoadingTab = true;
        Editor.Text = _workspace.SelectedTab.Document.Text;
        Editor.CaretIndex = Editor.Text?.Length ?? 0;
        _isLoadingTab = false;
        Title = $"{_workspace.SelectedTab.Title} - Notepad";
        RefreshDocumentStatus();
        RefreshCursorStatus();
        Editor.Focus();
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
        DocumentTitleBar.Background = _appearanceController.IsBackdropActive
            ? Brushes.Transparent
            : new SolidColorBrush(Color.Parse(dark ? "#202020" : "#F3F3F3"));
        WindowSurface.Background = new SolidColorBrush(Color.Parse(
            _appearanceController.IsBackdropActive
                ? dark ? "#18383D40" : "#18F3F3F3"
                : dark ? "#202027" : "#F3F3F3"));
    }

    private void OnEditorTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (_isLoadingTab)
        {
            return;
        }

        _workspace.SelectedTab.Document.ReplaceText(Editor.Text);
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
        var document = _workspace.SelectedTab.Document;
        var lineLabel = document.LineCount == 1 ? "line" : "lines";
        var characterLabel = document.CharacterCount == 1 ? "character" : "characters";
        DocumentSummary.Text = $"{document.LineCount} {lineLabel} · {document.CharacterCount} {characterLabel}";
        LineEndingStatus.Text = document.LineEndingDisplayText;
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

internal sealed class NotepadTabItem(NotepadTab tab) : INotifyPropertyChanged
{
    private bool _showLeadingSeparator;

    public NotepadTab Tab { get; } = tab;

    public string Title => Tab.Title;

    public bool ShowLeadingSeparator
    {
        get => _showLeadingSeparator;
        set
        {
            if (_showLeadingSeparator == value)
            {
                return;
            }

            _showLeadingSeparator = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ShowLeadingSeparator)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
