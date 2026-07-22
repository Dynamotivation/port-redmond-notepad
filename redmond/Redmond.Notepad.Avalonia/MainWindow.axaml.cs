using Avalonia.Controls;
using Avalonia;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text;
using Redmond.Avalonia.Controls;
using Redmond.Avalonia.Windowing;
using Redmond.Notepad.Core;
using Redmond.Notepad.Editor.AvaloniaEdit;

namespace Redmond.Notepad.Avalonia;

public partial class MainWindow : Window
{
    private const double TabWidth = 210;
    private const double TabScrollStep = 8;
    private const double TabScrollButtonWidth = 28;

    private readonly NotepadWorkspace _workspace;
    private readonly ObservableCollection<NotepadTabItem> _tabs = [];
    private readonly ITextFileStore _fileStore = new PhysicalTextFileStore();
    private readonly IReadOnlyList<DocumentFileReference> _initialFiles;
    private readonly WindowAppearanceController _appearanceController;
    private WindowAppearanceOptions _appearance;
    private AppThemePreference _themePreference;
    private bool _isLoadingTab;
    private bool _isUpdatingTabScrollButtons;
    private bool _allowWindowClose;
    private bool _isHandlingWindowClose;
    private INotepadFilePicker? _filePicker;
    private TaskCompletionSource<UnsavedChangesDecision>? _unsavedChangesDecision;

    public MainWindow() : this([])
    {
    }

    internal MainWindow(IEnumerable<string> initialPaths)
    {
        _initialFiles = initialPaths
            .Where(File.Exists)
            .Select(path => new DocumentFileReference(path))
            .ToArray();
        _workspace = new NotepadWorkspace(new AvaloniaEditTextBufferFactory());
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
        Editor.TextArea.Caret.PositionChanged += OnCaretPositionChanged;
        Opened += OnOpened;
        Closing += OnWindowClosing;
        PropertyChanged += OnWindowPropertyChanged;
        LoadSelectedTab();
        RefreshDocumentStatus();
    }

    private async void OnOpened(object? sender, EventArgs e)
    {
        _filePicker = new AvaloniaNotepadFilePicker(StorageProvider);
        if (_initialFiles.Count > 0)
        {
            await OpenFilesAsync(_initialFiles);
        }

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

    private async void OnOpenFilesClick(object? sender, RoutedEventArgs e)
    {
        if (_filePicker is null)
        {
            return;
        }

        var files = await _filePicker.PickOpenFilesAsync(allowMultiple: true);
        await OpenFilesAsync(files);
    }

    private async Task OpenFilesAsync(IReadOnlyList<DocumentFileReference> files)
    {
        var selectedDocumentChanged = false;
        string? errorMessage = null;
        foreach (var file in files)
        {
            var existing = _tabs.FirstOrDefault(item =>
                string.Equals(item.Tab.Document.File?.Path, file.Path, StringComparison.OrdinalIgnoreCase));
            if (existing is not null)
            {
                TabsList.SelectedItem = existing;
                selectedDocumentChanged = true;
                continue;
            }

            var useCurrentTab = _tabs.Count == 1
                && _workspace.SelectedTab.Document.File is null
                && !_workspace.SelectedTab.Document.IsModified
                && _workspace.SelectedTab.Document.CharacterCount == 0;
            var tab = useCurrentTab ? _workspace.SelectedTab : _workspace.CreateTab();
            var item = useCurrentTab
                ? _tabs.Single(candidate => ReferenceEquals(candidate.Tab, tab))
                : new NotepadTabItem(tab);
            if (!useCurrentTab)
            {
                _tabs.Add(item);
            }

            try
            {
                await tab.Document.LoadAsync(_fileStore, file);
                item.RefreshTitle();
                TabsList.SelectedItem = item;
                selectedDocumentChanged = true;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or DecoderFallbackException)
            {
                if (!useCurrentTab)
                {
                    _workspace.CloseTab(tab);
                    _tabs.Remove(item);
                }

                errorMessage = $"Could not open {file.DisplayName}: {exception.Message}";
            }
        }

        RefreshTabSeparators();
        if (selectedDocumentChanged)
        {
            LoadSelectedTab();
        }
        else if (errorMessage is not null)
        {
            DocumentSummary.Text = errorMessage;
        }

        ScheduleTabViewportUpdate(ensureSelectedTabIsVisible: true);
    }

    private async void OnSaveClick(object? sender, RoutedEventArgs e) =>
        await SaveSelectedDocumentAsync(forcePicker: false);

    private async void OnSaveAsClick(object? sender, RoutedEventArgs e) =>
        await SaveSelectedDocumentAsync(forcePicker: true);

    private async Task<bool> SaveSelectedDocumentAsync(bool forcePicker)
    {
        var document = _workspace.SelectedTab.Document;
        var destination = forcePicker ? null : document.File;
        if (destination is null)
        {
            if (_filePicker is null)
            {
                return false;
            }

            destination = await _filePicker.PickSaveFileAsync(
                document.File?.DisplayName ?? $"{NotepadDocument.UntitledName}.txt");
            if (destination is null)
            {
                return false;
            }
        }

        try
        {
            await document.SaveAsync(_fileStore, destination);
            if (TabsList.SelectedItem is NotepadTabItem item)
            {
                item.RefreshTitle();
            }

            UpdateDocumentTitle();
            RefreshDocumentStatus();
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or EncoderFallbackException)
        {
            DocumentSummary.Text = $"Could not save {destination.DisplayName}: {exception.Message}";
            return false;
        }
    }

    private async void OnCloseTabClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { CommandParameter: NotepadTabItem item })
        {
            return;
        }

        if (item.Tab.Document.IsModified)
        {
            TabsList.SelectedItem = item;
            var decision = await AskToSaveChangesAsync(item.Tab.Document);
            if (decision == UnsavedChangesDecision.Cancel
                || decision == UnsavedChangesDecision.Save
                && !await SaveSelectedDocumentAsync(forcePicker: false))
            {
                return;
            }
        }

        CloseTab(item);
        e.Handled = true;
    }

    private void CloseTab(NotepadTabItem item)
    {
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
    }

    private async void OnWindowClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_allowWindowClose)
        {
            return;
        }

        var modifiedTabs = _tabs.Where(item => item.Tab.Document.IsModified).ToArray();
        if (modifiedTabs.Length == 0)
        {
            return;
        }

        e.Cancel = true;
        if (_isHandlingWindowClose)
        {
            return;
        }

        _isHandlingWindowClose = true;
        foreach (var item in modifiedTabs)
        {
            TabsList.SelectedItem = item;
            var decision = await AskToSaveChangesAsync(item.Tab.Document);
            if (decision == UnsavedChangesDecision.Cancel
                || decision == UnsavedChangesDecision.Save
                && !await SaveSelectedDocumentAsync(forcePicker: false))
            {
                _isHandlingWindowClose = false;
                return;
            }
        }

        _allowWindowClose = true;
        Close();
    }

    private Task<UnsavedChangesDecision> AskToSaveChangesAsync(NotepadDocument document)
    {
        if (_unsavedChangesDecision is not null)
        {
            return _unsavedChangesDecision.Task;
        }

        UnsavedChangesMessage.Text = $"Do you want to save changes to {document.DisplayName}?";
        UnsavedChangesOverlay.IsVisible = true;
        SaveUnsavedChangesButton.Focus();
        _unsavedChangesDecision = new TaskCompletionSource<UnsavedChangesDecision>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        return _unsavedChangesDecision.Task;
    }

    private void OnSaveUnsavedChangesClick(object? sender, RoutedEventArgs e) =>
        ResolveUnsavedChanges(UnsavedChangesDecision.Save);

    private void OnDiscardUnsavedChangesClick(object? sender, RoutedEventArgs e) =>
        ResolveUnsavedChanges(UnsavedChangesDecision.Discard);

    private void OnCancelUnsavedChangesClick(object? sender, RoutedEventArgs e) =>
        ResolveUnsavedChanges(UnsavedChangesDecision.Cancel);

    private void ResolveUnsavedChanges(UnsavedChangesDecision decision)
    {
        var completion = _unsavedChangesDecision;
        _unsavedChangesDecision = null;
        UnsavedChangesOverlay.IsVisible = false;
        completion?.TrySetResult(decision);
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
                && index != selectedIndex
                && index - 1 != selectedIndex;
            _tabs[index].ShowTrailingSeparator = index == _tabs.Count - 1
                && index != selectedIndex;
        }
    }

    private void LoadSelectedTab()
    {
        _isLoadingTab = true;
        Editor.Document = GetEditorBuffer(_workspace.SelectedTab).Document;
        Editor.CaretOffset = Editor.Document.TextLength;
        _isLoadingTab = false;
        UpdateDocumentTitle();
        RefreshDocumentStatus();
        RefreshCursorStatus();
        if (TabsList.SelectedItem is NotepadTabItem item)
        {
            item.RefreshTitle();
        }
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

    private void OnEditorTextChanged(object? sender, EventArgs e)
    {
        if (_isLoadingTab)
        {
            return;
        }

        RefreshDocumentStatus();
        RefreshCursorStatus();
        UpdateDocumentTitle();
        if (TabsList.SelectedItem is NotepadTabItem item)
        {
            item.RefreshTitle();
        }
    }

    private void OnCaretPositionChanged(object? sender, EventArgs e) => RefreshCursorStatus();

    private void RefreshDocumentStatus()
    {
        var document = _workspace.SelectedTab.Document;
        var lineLabel = document.LineCount == 1 ? "line" : "lines";
        var characterLabel = document.CharacterCount == 1 ? "character" : "characters";
        DocumentSummary.Text = $"{document.LineCount} {lineLabel} · {document.CharacterCount} {characterLabel}";
        LineEndingStatus.Text = document.LineEndingDisplayText;
        EncodingStatus.Text = document.EncodingDisplayText;
    }

    private void RefreshCursorStatus()
    {
        var position = _workspace.SelectedTab.Document.Buffer.GetPosition(Editor.CaretOffset);
        CursorStatus.Text = $"Ln {position.Line}, Col {position.Column}";
    }

    private static AvaloniaEditTextBuffer GetEditorBuffer(NotepadTab tab) =>
        tab.Document.Buffer as AvaloniaEditTextBuffer
        ?? throw new InvalidOperationException("The Avalonia frontend requires an AvaloniaEdit text buffer.");

    private void UpdateDocumentTitle()
    {
        var document = _workspace.SelectedTab.Document;
        var modified = document.IsModified ? "*" : string.Empty;
        Title = $"{modified}{document.DisplayName} - Notepad";
    }
}

internal sealed class NotepadTabItem(NotepadTab tab) : INotifyPropertyChanged
{
    private bool _showLeadingSeparator;
    private bool _showTrailingSeparator;

    public NotepadTab Tab { get; } = tab;

    public string Title => Tab.Document.IsModified ? $"*{Tab.Title}" : Tab.Title;

    public void RefreshTitle() =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Title)));

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

    public bool ShowTrailingSeparator
    {
        get => _showTrailingSeparator;
        set
        {
            if (_showTrailingSeparator == value)
            {
                return;
            }

            _showTrailingSeparator = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ShowTrailingSeparator)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

internal enum UnsavedChangesDecision
{
    Save,
    Discard,
    Cancel,
}
