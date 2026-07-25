using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Primitives;
using Avalonia;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using AvaloniaEdit;
using AvaloniaEdit.Rendering;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text;
using Redmond.Avalonia.Controls;
using Redmond.Avalonia.Windowing;
using Redmond.Notepad.Core;
using Redmond.Notepad.Editor.AvaloniaEdit;
using Redmond.Notepad.Avalonia.Printing;
using Redmond.Notepad.Avalonia.SpellChecking;
using Redmond.Shortcuts;

namespace Redmond.Notepad.Avalonia;

public partial class MainWindow : Window
{
    private const double MaximumTabWidth = 228;
    private const double MinimumTabWidth = 80;
    private const double TabWidthReductionFactor = 0.75;
    private const double NewTabButtonSlotWidth = 37;
    private const double TabScrollStep = 8;
    private const double ZoomStep = 10;
    private static readonly double[] TabWidthSteps = CreateTabWidthSteps();

    private readonly NotepadWorkspace _workspace;
    private readonly ObservableCollection<NotepadTabItem> _tabs = [];
    private readonly ITextFileStore _fileStore = new PhysicalTextFileStore();
    private readonly IReadOnlyList<DocumentFileReference> _initialFiles;
    private readonly NotepadSessionWindowState? _initialSession;
    private readonly Guid _sessionId;
    private readonly IShortcutService _shortcutService;
    private readonly IReadOnlyList<IDisposable> _shortcutRegistrations;
    private readonly WindowAppearanceController _appearanceController;
    private readonly IPlatformPrintService _printService;
    private readonly SpellCheckEditorController _spellCheckController;
    private RecentFileList _recentFiles;
    private WindowAppearanceOptions _appearance;
    private NotepadPageSettings _pageSettings = new();
    private NotepadViewOptions _viewOptions;
    private NotepadFontOptions _fontOptions;
    private NotepadOpeningOptions _openingOptions;
    private NotepadSpellingOptions _spellingOptions;
    private AppThemePreference _themePreference;
    private bool _recentFilesEnabled;
    private bool _useNativeMacOSMenuBar;
    private bool _isLoadingTab;
    private bool _isUpdatingTabScrollButtons;
    private bool _isTabWidthUpdateScheduled;
    private bool _allowTabGrowthOnNextUpdate;
    private bool _isPointerOverTabBar;
    private int _tabWidthStep;
    private bool _allowWindowClose;
    private bool _isHandlingWindowClose;
    private bool _suppressExternalChangeCheck;
    private bool _discardSessionOnClose;
    private readonly HashSet<Guid> _discardedTabsOnClose = [];
    private INotepadFilePicker? _filePicker;
    private TaskCompletionSource<UnsavedChangesDecision>? _unsavedChangesDecision;
    private TaskCompletionSource<ExternalChangesDecision>? _externalChangesDecision;
    private CancellationTokenSource? _externalChangeCheckCancellation;
    private AutocorrectionChange? _visibleAutocorrection;

    public MainWindow() : this([], null)
    {
    }

    internal MainWindow(IEnumerable<string> initialPaths) : this(initialPaths, null)
    {
    }

    internal MainWindow(NotepadSessionWindowState session) : this([], session)
    {
    }

    private MainWindow(IEnumerable<string> initialPaths, NotepadSessionWindowState? session)
    {
        _initialFiles = initialPaths
            .Where(File.Exists)
            .Select(path => new DocumentFileReference(path))
            .ToArray();
        _initialSession = session;
        _sessionId = session?.Id ?? Guid.NewGuid();
        _workspace = new NotepadWorkspace(new AvaloniaEditTextBufferFactory());
        InitializeComponent();
        _shortcutService = ShortcutServices.CreateForCurrentPlatform();
        _shortcutRegistrations = NotepadShortcutCatalog.CreateDefinitions()
            .Select(definition => _shortcutService.Register(definition))
            .ToArray();
        _printService = PlatformPrintServiceFactory.Create();
        _spellCheckController = new SpellCheckEditorController(
            Editor,
            PlatformSpellCheckServiceFactory.Create());
        _spellCheckController.AutocorrectionChanged += OnAutocorrectionChanged;
        Editor.TextArea.RightClickMovesCaret = true;
        ConfigureFileMenuShortcuts();
        Resources["CloseTabToolTip"] = FormatCommandToolTip("Close tab", NotepadShortcutIds.CloseTab);
        Resources["NewTabToolTip"] = FormatCommandToolTip("New tab", NotepadShortcutIds.NewTab);
        AddHandler(KeyDownEvent, OnNotepadKeyDown, RoutingStrategies.Tunnel);
        AddHandler(PointerMovedEvent, OnWindowPointerMoved, RoutingStrategies.Tunnel);
        PointerExited += OnWindowPointerExited;
        _appearance = NotepadSettingsStore.LoadAppearance();
        _themePreference = NotepadSettingsStore.LoadThemePreference();
        _viewOptions = NotepadSettingsStore.LoadViewOptions();
        _fontOptions = NotepadSettingsStore.LoadFontOptions();
        _openingOptions = NotepadSettingsStore.LoadOpeningOptions();
        _spellingOptions = NotepadSettingsStore.LoadSpellingOptions();
        _useNativeMacOSMenuBar = NotepadSettingsStore.LoadUseNativeMacOSMenuBar();
        _recentFiles = NotepadSettingsStore.LoadRecentFiles();
        _recentFilesEnabled = NotepadSettingsStore.LoadRecentFilesEnabled();
        _appearanceController = WindowAppearanceController.Attach(this, WindowSurface, _appearance);
        _appearanceController.PresentationChanged += (_, _) => UpdateWindowPresentation();
        SettingsPage.SetAppearance(_appearance);
        SettingsPage.SetThemePreference(_themePreference);
        SettingsPage.SetRecentFilesEnabled(_recentFilesEnabled);
        SettingsPage.SetWordWrap(_viewOptions.WordWrap);
        SettingsPage.SetFormattingEnabled(_viewOptions.FormattingEnabled);
        SettingsPage.SetUseNativeMacOSMenuBar(_useNativeMacOSMenuBar);
        SettingsPage.SetFontOptions(_fontOptions);
        SettingsPage.SetOpeningOptions(_openingOptions);
        SettingsPage.SetSpellingOptions(_spellingOptions);
        SettingsPage.AppearanceChanged += OnAppearanceChanged;
        SettingsPage.ThemeChanged += OnThemeChanged;
        SettingsPage.RecentFilesEnabledChanged += OnRecentFilesEnabledChanged;
        SettingsPage.WordWrapChanged += OnSettingsWordWrapChanged;
        SettingsPage.FormattingEnabledChanged += OnFormattingEnabledChanged;
        SettingsPage.NativeMacOSMenuBarChanged += OnNativeMacOSMenuBarChanged;
        SettingsPage.FontOptionsChanged += OnFontOptionsChanged;
        SettingsPage.OpeningOptionsChanged += OnOpeningOptionsChanged;
        SettingsPage.SpellingOptionsChanged += OnSpellingOptionsChanged;
        NotepadSettingsStore.FontOptionsChanged += OnGlobalFontOptionsChanged;
        NotepadSettingsStore.OpeningOptionsChanged += OnGlobalOpeningOptionsChanged;
        NotepadSettingsStore.NativeMacOSMenuBarChanged += OnGlobalNativeMacOSMenuBarChanged;
        NotepadSettingsStore.SpellingOptionsChanged += OnGlobalSpellingOptionsChanged;
        foreach (var tab in _workspace.Tabs)
        {
            _tabs.Add(CreateTabItem(tab));
        }
        TabsList.ItemsSource = _tabs;
        TabsList.SelectedItem = _tabs.Single(item => ReferenceEquals(item.Tab, _workspace.SelectedTab));
        RefreshTabSeparators();
        RefreshRecentFilesMenu();
        ApplyViewOptions();
        ApplyMacOSMenuPresentation();
        UpdateWindowPresentation();
        Editor.TextChanged += OnEditorTextChanged;
        Editor.TextArea.Caret.PositionChanged += OnCaretPositionChanged;
        Opened += OnOpened;
        Closing += OnWindowClosing;
        PropertyChanged += OnWindowPropertyChanged;
        Closed += OnWindowClosed;
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
        else if (_initialSession is not null)
        {
            await RestoreSessionAsync(_initialSession);
        }

        Editor.Focus();
        ScheduleTabWidthUpdate(allowGrowth: true);
        ScheduleTabViewportUpdate(ensureSelectedTabIsVisible: true);
    }

    private void OnSettingsClick(object? sender, RoutedEventArgs e)
        => ShowSettingsPage();

    internal void ShowSettingsPage()
    {
        SettingsPage.SetAppearance(_appearance);
        SettingsPage.SetThemePreference(_themePreference);
        SettingsPage.SetRecentFilesEnabled(_recentFilesEnabled);
        SettingsPage.SetWordWrap(_viewOptions.WordWrap);
        SettingsPage.SetFormattingEnabled(_viewOptions.FormattingEnabled);
        SettingsPage.SetUseNativeMacOSMenuBar(_useNativeMacOSMenuBar);
        SettingsPage.SetFontOptions(_fontOptions);
        SettingsPage.SetOpeningOptions(_openingOptions);
        SettingsPage.SetSpellingOptions(_spellingOptions);
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

    private void OnRecentFilesEnabledChanged(object? sender, bool enabled)
    {
        _recentFilesEnabled = enabled;
        NotepadSettingsStore.SaveRecentFilesEnabled(enabled);
        if (enabled)
        {
            _recentFiles = NotepadSettingsStore.LoadRecentFiles();
        }

        RefreshRecentFilesMenu();
    }

    private void OnSettingsWordWrapChanged(object? sender, bool enabled)
    {
        _viewOptions = _viewOptions with { WordWrap = enabled };
        ApplyViewOptions();
        NotepadSettingsStore.SaveViewOptions(_viewOptions);
        Editor.Focus();
    }

    private void OnFormattingEnabledChanged(object? sender, bool enabled)
    {
        _viewOptions = _viewOptions with { FormattingEnabled = enabled };
        ApplyViewOptions();
        NotepadSettingsStore.SaveViewOptions(_viewOptions);
    }

    private void OnNativeMacOSMenuBarChanged(object? sender, bool enabled) =>
        NotepadSettingsStore.SaveUseNativeMacOSMenuBar(enabled);

    private void OnGlobalNativeMacOSMenuBarChanged(object? sender, bool enabled)
    {
        _useNativeMacOSMenuBar = enabled;
        SettingsPage.SetUseNativeMacOSMenuBar(enabled);
        ApplyMacOSMenuPresentation();
    }

    private void OnFontOptionsChanged(object? sender, NotepadFontOptions options) =>
        NotepadSettingsStore.SaveFontOptions(options);

    private void OnGlobalFontOptionsChanged(object? sender, NotepadFontOptions options)
    {
        _fontOptions = options;
        SettingsPage.SetFontOptions(options);
        ApplyViewOptions();
    }

    private void OnOpeningOptionsChanged(object? sender, NotepadOpeningOptions options) =>
        NotepadSettingsStore.SaveOpeningOptions(options);

    private void OnGlobalOpeningOptionsChanged(object? sender, NotepadOpeningOptions options)
    {
        _openingOptions = options;
        SettingsPage.SetOpeningOptions(options);
    }

    private void OnSpellingOptionsChanged(object? sender, NotepadSpellingOptions options) =>
        NotepadSettingsStore.SaveSpellingOptions(options);

    private void OnGlobalSpellingOptionsChanged(object? sender, NotepadSpellingOptions options)
    {
        _spellingOptions = options;
        SettingsPage.SetSpellingOptions(options);
        ApplySpellCheckPolicy();
    }

    private void OnWindowPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == ActualThemeVariantProperty)
        {
            UpdateWindowPresentation();
        }
    }

    private void OnNewTabClick(object? sender, RoutedEventArgs e) => CreateNewTab();

    private void CreateNewTab()
    {
        var tab = _workspace.CreateTab();
        var item = CreateTabItem(tab);
        _tabs.Add(item);
        TabsList.SelectedItem = item;
        RefreshTabSeparators();
        LoadSelectedTab();
        ScheduleTabWidthUpdate(allowGrowth: false);
        ScheduleTabViewportUpdate(ensureSelectedTabIsVisible: true);
    }

    private void OnNewWindowClick(object? sender, RoutedEventArgs e) => OpenNewWindow();

    private static void OpenNewWindow() => new MainWindow().Show();

    private async void OnOpenFilesClick(object? sender, RoutedEventArgs e)
        => await PickAndOpenFilesAsync();

    private async Task PickAndOpenFilesAsync()
    {
        if (_filePicker is null)
        {
            return;
        }

        var files = await _filePicker.PickOpenFilesAsync(allowMultiple: true);
        await OpenFilesAccordingToPreferenceAsync(files);
    }

    private async Task OpenFilesAccordingToPreferenceAsync(IReadOnlyList<DocumentFileReference> files)
    {
        if (files.Count == 0)
        {
            return;
        }

        if (_openingOptions.FileOpeningMode == NotepadFileOpeningMode.NewTab)
        {
            await OpenFilesAsync(files);
            return;
        }

        foreach (var file in files)
        {
            new MainWindow([file.Path]).Show();
        }
    }

    private async Task OpenFilesAsync(IReadOnlyList<DocumentFileReference> files)
    {
        var selectedDocumentChanged = false;
        var recentFilesChanged = false;
        string? errorMessage = null;
        foreach (var file in files)
        {
            var existing = _tabs.FirstOrDefault(item =>
                string.Equals(item.Tab.Document.File?.Path, file.Path, StringComparison.OrdinalIgnoreCase));
            if (existing is not null)
            {
                TabsList.SelectedItem = existing;
                selectedDocumentChanged = true;
                recentFilesChanged |= RememberRecentFile(file.Path);
                continue;
            }

            var useCurrentTab = _tabs.Count == 1
                && _workspace.SelectedTab.Document.File is null
                && !_workspace.SelectedTab.Document.IsModified
                && _workspace.SelectedTab.Document.CharacterCount == 0;
            var tab = useCurrentTab ? _workspace.SelectedTab : _workspace.CreateTab();
            var item = useCurrentTab
                ? _tabs.Single(candidate => ReferenceEquals(candidate.Tab, tab))
                : CreateTabItem(tab);
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
                recentFilesChanged |= RememberRecentFile(file.Path);
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
        if (recentFilesChanged)
        {
            RefreshRecentFilesMenu();
        }

        if (selectedDocumentChanged)
        {
            LoadSelectedTab();
        }
        else if (errorMessage is not null)
        {
            DocumentSummary.Text = errorMessage;
        }

        ScheduleTabWidthUpdate(allowGrowth: false);
        ScheduleTabViewportUpdate(ensureSelectedTabIsVisible: true);
    }

    private async Task RestoreSessionAsync(NotepadSessionWindowState session)
    {
        var savedDocuments = session.Documents ?? [];
        if (savedDocuments.Length == 0)
        {
            return;
        }

        string? restorationMessage = null;
        for (var index = 0; index < savedDocuments.Length; index++)
        {
            var savedDocument = savedDocuments[index];
            var tab = index == 0 ? _workspace.SelectedTab : _workspace.CreateTab();
            var item = index == 0
                ? _tabs.Single(candidate => ReferenceEquals(candidate.Tab, tab))
                : CreateTabItem(tab);
            if (index > 0)
            {
                _tabs.Add(item);
            }

            DocumentFileReference? file = null;
            if (!string.IsNullOrWhiteSpace(savedDocument.Path))
            {
                try
                {
                    file = new DocumentFileReference(savedDocument.Path);
                }
                catch (ArgumentException exception)
                {
                    restorationMessage = $"Could not restore a session path: {exception.Message}";
                }
            }

            if (savedDocument.ModifiedText is not null)
            {
                Encoding encoding;
                try
                {
                    encoding = string.IsNullOrWhiteSpace(savedDocument.EncodingName)
                        ? TextFileMetadata.NewDocument.Encoding
                        : RestoreSessionEncoding(
                            savedDocument.EncodingName,
                            savedDocument.HasByteOrderMark);
                }
                catch (ArgumentException)
                {
                    encoding = TextFileMetadata.NewDocument.Encoding;
                }

                tab.Document.RestoreSessionChanges(
                    file,
                    savedDocument.ModifiedText,
                    encoding,
                    savedDocument.LineEnding,
                    savedDocument.BaseFileVersion);
            }
            else if (file is not null && File.Exists(file.Path))
            {
                try
                {
                    await tab.Document.LoadAsync(_fileStore, file);
                    RememberRecentFile(file.Path);
                }
                catch (Exception exception) when (exception is IOException
                    or UnauthorizedAccessException
                    or DecoderFallbackException
                    or ArgumentException)
                {
                    restorationMessage = $"Could not restore {Path.GetFileName(savedDocument.Path)}: {exception.Message}";
                }
            }
            else if (file is not null)
            {
                restorationMessage = $"Could not restore {file.DisplayName} because it is no longer available.";
            }

            item.RefreshTitle();
        }

        var selectedIndex = Math.Clamp(session.SelectedIndex, 0, _tabs.Count - 1);
        SelectTabWithoutExternalChangeCheck(_tabs[selectedIndex]);
        RefreshTabSeparators();
        LoadSelectedTab();
        if (restorationMessage is not null)
        {
            DocumentSummary.Text = restorationMessage;
        }

        if (TabsList.SelectedItem is NotepadTabItem selectedItem)
        {
            await CheckSelectedDocumentForExternalChangesAsync(selectedItem);
        }
    }

    private static Encoding RestoreSessionEncoding(string name, bool hasByteOrderMark)
    {
        var encoding = Encoding.GetEncoding(name);
        return encoding.CodePage switch
        {
            65001 => new UTF8Encoding(hasByteOrderMark),
            1200 => new UnicodeEncoding(bigEndian: false, byteOrderMark: hasByteOrderMark),
            1201 => new UnicodeEncoding(bigEndian: true, byteOrderMark: hasByteOrderMark),
            _ => encoding,
        };
    }

    private bool RememberRecentFile(string path)
    {
        if (!_recentFilesEnabled)
        {
            return false;
        }

        _recentFiles = NotepadSettingsStore.RecordRecentFile(path);
        return true;
    }

    private void OnFileMenuOpened(object? sender, RoutedEventArgs e)
    {
        if (_recentFilesEnabled)
        {
            _recentFiles = NotepadSettingsStore.LoadRecentFiles();
        }

        RefreshRecentFilesMenu();
    }

    private void RefreshRecentFilesMenu()
    {
        RecentFilesMenuItem.IsVisible = _recentFilesEnabled;
        RecentFilesMenuItem.Items.Clear();
        if (!_recentFilesEnabled)
        {
            return;
        }

        foreach (var path in _recentFiles.Paths)
        {
            var item = new MenuItem
            {
                Header = Path.GetFileName(path),
                Tag = path,
            };
            ToolTip.SetTip(item, path);
            item.Click += OnRecentFileClick;
            RecentFilesMenuItem.Items.Add(item);
        }

        if (_recentFiles.Paths.Count == 0)
        {
            RecentFilesMenuItem.Items.Add(new MenuItem
            {
                Header = "No recent files",
                IsEnabled = false,
            });
            return;
        }

        var separator = new Separator();
        separator.Classes.Add("menuDivider");
        RecentFilesMenuItem.Items.Add(separator);
        var clearItem = new MenuItem { Header = "Clear list" };
        clearItem.Click += OnClearRecentFilesClick;
        RecentFilesMenuItem.Items.Add(clearItem);
    }

    private async void OnRecentFileClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: string path })
        {
            return;
        }

        if (!File.Exists(path))
        {
            _recentFiles = NotepadSettingsStore.RemoveRecentFile(path);
            RefreshRecentFilesMenu();
            DocumentSummary.Text = $"Could not find {Path.GetFileName(path)}.";
            return;
        }

        await OpenFilesAccordingToPreferenceAsync([new DocumentFileReference(path)]);
    }

    private void OnClearRecentFilesClick(object? sender, RoutedEventArgs e)
    {
        NotepadSettingsStore.ClearRecentFiles();
        _recentFiles = new RecentFileList();
        RefreshRecentFilesMenu();
    }

    private async void OnSaveClick(object? sender, RoutedEventArgs e) =>
        await SaveSelectedDocumentAsync(forcePicker: false);

    private async void OnSaveAsClick(object? sender, RoutedEventArgs e) =>
        await SaveSelectedDocumentAsync(forcePicker: true);

    private async void OnSaveAllClick(object? sender, RoutedEventArgs e)
        => await SaveAllDocumentsAsync();

    private async Task SaveAllDocumentsAsync()
    {
        foreach (var item in _tabs.Where(candidate => candidate.Tab.Document.IsModified).ToArray())
        {
            SelectTabWithoutExternalChangeCheck(item);
            if (!await SaveDocumentAsync(item, forcePicker: false))
            {
                break;
            }
        }
    }

    private Task<bool> SaveSelectedDocumentAsync(bool forcePicker) =>
        SaveDocumentAsync(
            (NotepadTabItem)TabsList.SelectedItem!,
            forcePicker);

    private async Task<bool> SaveDocumentAsync(NotepadTabItem item, bool forcePicker)
    {
        var document = item.Tab.Document;
        var destination = forcePicker ? null : document.File;
        if (destination is null)
        {
            if (_filePicker is null)
            {
                return false;
            }

            destination = await _filePicker.PickSaveFileAsync(
                document.SuggestedFileName);
            if (destination is null)
            {
                return false;
            }
        }

        try
        {
            await document.SaveAsync(_fileStore, destination);
            RefreshSavedDocument(item);
            return true;
        }
        catch (FileChangedExternallyException exception)
        {
            var decision = await AskAboutExternalChangesAsync(document, exception.Change);
            if (decision == ExternalChangesDecision.Cancel)
            {
                DocumentSummary.Text = $"Save cancelled. {destination.DisplayName} was not changed.";
                return false;
            }

            try
            {
                if (decision == ExternalChangesDecision.Reload)
                {
                    await document.LoadAsync(_fileStore, destination);
                }
                else
                {
                    await document.SaveAsync(
                        _fileStore,
                        destination,
                        overwriteExternalChanges: true);
                }

                RefreshSavedDocument(item);
                return true;
            }
            catch (Exception resolutionException) when (
                resolutionException is IOException
                    or UnauthorizedAccessException
                    or DecoderFallbackException
                    or EncoderFallbackException)
            {
                DocumentSummary.Text = $"Could not resolve changes to {destination.DisplayName}: {resolutionException.Message}";
                return false;
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or EncoderFallbackException)
        {
            DocumentSummary.Text = $"Could not save {destination.DisplayName}: {exception.Message}";
            return false;
        }
    }

    private void RefreshSavedDocument(NotepadTabItem item)
    {
        item.RefreshTitle();
        if (!ReferenceEquals(item.Tab, _workspace.SelectedTab))
        {
            return;
        }

        LoadSelectedTab();
        UpdateDocumentTitle();
        RefreshDocumentStatus();
    }

    private async void OnCloseTabClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { CommandParameter: NotepadTabItem item })
        {
            return;
        }

        await TryCloseTabAsync(item);
        e.Handled = true;
    }

    private async void OnCloseSelectedTabClick(object? sender, RoutedEventArgs e)
    {
        if (TabsList.SelectedItem is NotepadTabItem item)
        {
            await TryCloseTabAsync(item);
        }
    }

    private async Task TryCloseTabAsync(NotepadTabItem item)
    {
        if (item.Tab.Document.IsModified)
        {
            SelectTabWithoutExternalChangeCheck(item);
            var decision = await AskToSaveChangesAsync(item.Tab.Document);
            if (decision == UnsavedChangesDecision.Cancel
                || decision == UnsavedChangesDecision.Save
                && !await SaveSelectedDocumentAsync(forcePicker: false))
            {
                return;
            }
        }

        CloseTab(item);
    }

    private void OnCloseWindowClick(object? sender, RoutedEventArgs e) => Close();

    private void OnPageSetupClick(object? sender, RoutedEventArgs e) => ShowPageSetup();

    private void ShowPageSetup()
    {
        var result = _printService.ShowPageSetup(GetNativeWindowHandle(), ref _pageSettings);
        ReportPrintResult(result, "Page setup updated.");
    }

    private void OnEditMenuOpened(object? sender, RoutedEventArgs e)
    {
        var hasSelection = Editor.SelectionLength > 0;
        UndoMenuItem.IsEnabled = Editor.CanUndo;
        CutMenuItem.IsEnabled = hasSelection;
        CopyMenuItem.IsEnabled = hasSelection;
        PasteMenuItem.IsEnabled = Editor.CanPaste;
        DeleteMenuItem.IsEnabled = hasSelection;
        FindMenuItem.IsEnabled = Editor.CanSearch;
        FindNextMenuItem.IsEnabled = Editor.CanSearch;
        FindPreviousMenuItem.IsEnabled = Editor.CanSearch;
        ReplaceMenuItem.IsEnabled = Editor.CanSearch;
        GoToMenuItem.IsEnabled = Editor.Document?.LineCount > 0;
        // Windows Notepad keeps Select all available even for an empty document.
        SelectAllMenuItem.IsEnabled = true;
    }

    private void OnViewMenuOpened(object? sender, RoutedEventArgs e)
    {
        StatusBarMenuItem.IsChecked = _viewOptions.ShowStatusBar;
        WordWrapMenuItem.IsChecked = _viewOptions.WordWrap;
        ZoomInMenuItem.IsEnabled = _viewOptions.ZoomPercent < 500;
        ZoomOutMenuItem.IsEnabled = _viewOptions.ZoomPercent > 10;
        RestoreZoomMenuItem.IsEnabled = _viewOptions.ZoomPercent != 100;
    }

    private void OnEditorContextMenuOpened(object? sender, RoutedEventArgs e)
    {
        var correction = OperatingSystem.IsMacOS()
            ? _spellCheckController.CurrentAutocorrection
            : null;
        var issue = _spellCheckController.GetIssueAt(Editor.CaretOffset)
            ?? _spellCheckController.GetIssueAt(Math.Max(0, Editor.CaretOffset - 1));
        var items = new List<object>();
        if (correction is not null
            && Editor.CaretOffset >= correction.Start
            && Editor.CaretOffset <= correction.Start + correction.Replacement.Length)
        {
            var changeBackItem = new MenuItem
            {
                Header = $"Change Back to “{correction.Original}”",
            };
            changeBackItem.Click += (_, _) => ChangeBack(correction);
            items.Add(changeBackItem);
            items.Add(new Separator());
        }

        if (issue is not null)
        {
            var suggestions = _spellCheckController.Suggest(issue);
            if (suggestions.Count == 0)
            {
                items.Add(new MenuItem { Header = "No spelling suggestions", IsEnabled = false });
            }
            else
            {
                foreach (var suggestion in suggestions)
                {
                    var suggestionItem = new MenuItem { Header = suggestion };
                    suggestionItem.Click += (_, _) => ReplaceSpellingIssue(issue, suggestion);
                    items.Add(suggestionItem);
                }
            }

            items.Add(new Separator());
            var ignoreItem = new MenuItem { Header = "Ignore" };
            ignoreItem.Click += (_, _) => _spellCheckController.Ignore(issue);
            items.Add(ignoreItem);
            var addItem = new MenuItem { Header = "Add to dictionary" };
            addItem.Click += (_, _) => _spellCheckController.AddToDictionary(issue);
            items.Add(addItem);
            items.Add(new Separator());
        }

        items.Add(CreateEditorContextCommand("Cut", Editor.SelectionLength > 0, CutEditor));
        items.Add(CreateEditorContextCommand("Copy", Editor.SelectionLength > 0, Editor.Copy));
        items.Add(CreateEditorContextCommand("Paste", Editor.CanPaste, PasteEditor));
        items.Add(CreateEditorContextCommand("Delete", Editor.SelectionLength > 0, DeleteEditor));
        items.Add(new Separator());
        items.Add(CreateEditorContextCommand("Select all", true, Editor.SelectAll));
        items.Add(new Separator());
        var effectiveSpellCheck = IsSpellCheckEnabledForSelectedTab();
        var spellCheckItem = new MenuItem
        {
            Header = "Check spelling",
            ToggleType = MenuItemToggleType.CheckBox,
            IsChecked = effectiveSpellCheck,
            IsEnabled = _spellCheckController.IsAvailable,
        };
        spellCheckItem.Click += (_, _) =>
        {
            _workspace.SelectedTab.IsSpellCheckEnabledOverride = !effectiveSpellCheck;
            ApplySpellCheckPolicy();
        };
        items.Add(spellCheckItem);
        EditorContextMenu.Items.Clear();
        foreach (var item in items)
        {
            EditorContextMenu.Items.Add(item);
        }
    }

    private static MenuItem CreateEditorContextCommand(string header, bool enabled, Action action)
    {
        var item = new MenuItem { Header = header, IsEnabled = enabled };
        item.Click += (_, _) => action();
        return item;
    }

    private void ReplaceSpellingIssue(SpellingIssue issue, string replacement)
    {
        if (Editor.Document is null || issue.Start < 0 || issue.End > Editor.Document.TextLength)
        {
            return;
        }

        using (Editor.Document.RunUpdate())
        {
            Editor.Document.Replace(issue.Start, issue.Length, replacement);
        }

        Editor.SelectionStart = issue.Start + replacement.Length;
        Editor.SelectionLength = 0;
        RefreshAfterEditorCommand();
        _spellCheckController.Refresh();
    }

    private void OnAutocorrectionChanged(object? sender, AutocorrectionChangedEventArgs e)
    {
        _visibleAutocorrection = OperatingSystem.IsMacOS() ? e.Correction : null;
        ChangeBackButton.IsVisible = false;
        if (_visibleAutocorrection is null)
        {
            return;
        }

        ChangeBackButton.Content = $"Change Back to “{_visibleAutocorrection.Original}”";
        Dispatcher.UIThread.Post(PositionChangeBackButton, DispatcherPriority.Render);
    }

    private void PositionChangeBackButton()
    {
        var correction = _visibleAutocorrection;
        var document = Editor.Document;
        if (correction is null
            || document is null
            || correction.Start + correction.Replacement.Length > document.TextLength
            || !Editor.TextArea.TextView.VisualLinesValid)
        {
            ChangeBackButton.IsVisible = false;
            return;
        }

        var location = document.GetLocation(correction.Start + correction.Replacement.Length);
        var visualPosition = Editor.TextArea.TextView.GetVisualPosition(
            new TextViewPosition(location.Line, location.Column),
            VisualYPosition.LineBottom);
        var translated = Editor.TextArea.TextView.TranslatePoint(visualPosition, EditorOverlay);
        if (translated is null)
        {
            ChangeBackButton.IsVisible = false;
            return;
        }

        ChangeBackButton.Measure(Size.Infinity);
        var x = Math.Clamp(
            translated.Value.X - ChangeBackButton.DesiredSize.Width,
            0,
            Math.Max(0, EditorOverlay.Bounds.Width - ChangeBackButton.DesiredSize.Width));
        var y = Math.Clamp(
            translated.Value.Y + 3,
            0,
            Math.Max(0, EditorOverlay.Bounds.Height - ChangeBackButton.DesiredSize.Height));
        ChangeBackButton.Margin = new Thickness(x, y, 0, 0);
        ChangeBackButton.IsVisible = true;
    }

    private void OnChangeBackClick(object? sender, RoutedEventArgs e)
    {
        if (_visibleAutocorrection is { } correction)
        {
            ChangeBack(correction);
        }
    }

    private void ChangeBack(AutocorrectionChange correction)
    {
        if (_spellCheckController.ChangeBack(correction))
        {
            RefreshAfterEditorCommand();
        }
    }

    private void OnZoomInClick(object? sender, RoutedEventArgs e) => ChangeZoom(ZoomStep);

    private void OnZoomOutClick(object? sender, RoutedEventArgs e) => ChangeZoom(-ZoomStep);

    private void OnRestoreZoomClick(object? sender, RoutedEventArgs e) => SetZoom(100);

    private void ChangeZoom(double change) => SetZoom(_viewOptions.ZoomPercent + change);

    private void SetZoom(double zoomPercent)
    {
        _viewOptions = (_viewOptions with { ZoomPercent = zoomPercent }).Normalized();
        ApplyViewOptions();
        NotepadSettingsStore.SaveViewOptions(_viewOptions);
        Editor.Focus();
    }

    private void OnStatusBarClick(object? sender, RoutedEventArgs e)
    {
        _viewOptions = _viewOptions with { ShowStatusBar = !_viewOptions.ShowStatusBar };
        ApplyViewOptions();
        NotepadSettingsStore.SaveViewOptions(_viewOptions);
    }

    private void OnWordWrapClick(object? sender, RoutedEventArgs e)
    {
        _viewOptions = _viewOptions with { WordWrap = !_viewOptions.WordWrap };
        ApplyViewOptions();
        NotepadSettingsStore.SaveViewOptions(_viewOptions);
        Editor.Focus();
    }

    private void ApplyViewOptions()
    {
        Editor.FontFamily = new FontFamily(_fontOptions.Family);
        Editor.FontStyle = _fontOptions.Style is NotepadEditorFontStyle.Italic
            or NotepadEditorFontStyle.BoldItalic
                ? FontStyle.Italic
                : FontStyle.Normal;
        Editor.FontWeight = _fontOptions.Style is NotepadEditorFontStyle.Bold
            or NotepadEditorFontStyle.BoldItalic
                ? FontWeight.Bold
                : FontWeight.Normal;
        Editor.FontSize = _fontOptions.Size * _viewOptions.ZoomPercent / 100;
        Editor.WordWrap = _viewOptions.WordWrap;
        Editor.HorizontalScrollBarVisibility = _viewOptions.WordWrap
            ? ScrollBarVisibility.Disabled
            : ScrollBarVisibility.Auto;
        StatusBar.IsVisible = _viewOptions.ShowStatusBar;
        StatusBarMenuItem.IsChecked = _viewOptions.ShowStatusBar;
        WordWrapMenuItem.IsChecked = _viewOptions.WordWrap;
        SettingsPage.SetWordWrap(_viewOptions.WordWrap);
        SettingsPage.SetFormattingEnabled(_viewOptions.FormattingEnabled);
        NewMarkdownTabMenuItem.IsVisible = _viewOptions.FormattingEnabled;
        MarkdownMenuItem.IsVisible = _viewOptions.FormattingEnabled;
        ZoomStatus.Text = $"{_viewOptions.ZoomPercent:0}%";
    }

    private NotepadDocument? GetSelectedDocument() =>
        TabsList.SelectedItem is NotepadTabItem item ? item.Tab.Document : null;

    private void OnUndoClick(object? sender, RoutedEventArgs e)
    {
        UndoEditor();
    }

    private void OnCutClick(object? sender, RoutedEventArgs e)
    {
        CutEditor();
    }

    private void OnCopyClick(object? sender, RoutedEventArgs e)
    {
        Editor.Copy();
        Editor.Focus();
    }

    private void OnPasteClick(object? sender, RoutedEventArgs e)
    {
        PasteEditor();
    }

    private void OnDeleteClick(object? sender, RoutedEventArgs e)
    {
        DeleteEditor();
    }

    private void UndoEditor()
    {
        Editor.Undo();
        RefreshAfterEditorCommand();
    }

    private void CutEditor()
    {
        Editor.Cut();
        RefreshAfterEditorCommand();
    }

    private void PasteEditor()
    {
        Editor.Paste();
        RefreshAfterEditorCommand();
    }

    private void DeleteEditor()
    {
        Editor.Delete();
        RefreshAfterEditorCommand();
    }

    private void OnFindClick(object? sender, RoutedEventArgs e) => ShowFindAndReplace(showReplace: false);

    private void OnReplaceClick(object? sender, RoutedEventArgs e) => ShowFindAndReplace(showReplace: true);

    private void ShowFindAndReplace(bool showReplace)
    {
        var searchPanel = Editor.SearchPanel;
        searchPanel.IsReplaceMode = showReplace;
        var searchText = GetSelectedDocument() is { } document
            ? NotepadEditLogic.GetSearchText(document.Buffer, Editor.SelectionStart, Editor.SelectionLength)
            : string.Empty;
        if (searchText.Length > 0)
        {
            searchPanel.SearchPattern = searchText;
        }

        searchPanel.Open();
        searchPanel.Reactivate();
    }

    private void OnFindNextClick(object? sender, RoutedEventArgs e) => FindNext();

    private void FindNext()
    {
        var searchPanel = Editor.SearchPanel;
        if (string.IsNullOrEmpty(searchPanel.SearchPattern))
        {
            ShowFindAndReplace(showReplace: false);
            return;
        }

        searchPanel.FindNext(Editor.SelectionStart + Editor.SelectionLength);
        Editor.Focus();
    }

    private void OnFindPreviousClick(object? sender, RoutedEventArgs e) => FindPrevious();

    private void FindPrevious()
    {
        var searchPanel = Editor.SearchPanel;
        if (string.IsNullOrEmpty(searchPanel.SearchPattern))
        {
            ShowFindAndReplace(showReplace: false);
            return;
        }

        searchPanel.FindPrevious();
        Editor.Focus();
    }

    private void OnGoToClick(object? sender, RoutedEventArgs e) => ShowGoTo();

    private void ShowGoTo()
    {
        var document = GetSelectedDocument();
        if (document is null)
        {
            return;
        }

        var currentLine = document.Buffer.GetPosition(Editor.TextArea.Caret.Offset).Line;
        GoToLineRangeText.Text = $"Line number (1–{document.LineCount})";
        GoToLineTextBox.Text = currentLine.ToString();
        GoToOverlay.IsVisible = true;
        GoToLineTextBox.Focus();
        GoToLineTextBox.SelectAll();
    }

    private void OnConfirmGoToClick(object? sender, RoutedEventArgs e) => ConfirmGoTo();

    private void ConfirmGoTo()
    {
        var lineCount = Editor.Document?.LineCount ?? 0;
        if (!int.TryParse(GoToLineTextBox.Text, out var lineNumber)
            || lineNumber < 1
            || lineNumber > lineCount)
        {
            GoToLineRangeText.Text = $"Enter a line from 1 through {lineCount}.";
            GoToLineTextBox.Focus();
            GoToLineTextBox.SelectAll();
            return;
        }

        var line = Editor.Document!.GetLineByNumber(lineNumber);
        Editor.SelectionLength = 0;
        Editor.SelectionStart = line.Offset;
        Editor.TextArea.Caret.Offset = line.Offset;
        Editor.ScrollToLine(lineNumber);
        CloseGoTo();
    }

    private void OnCancelGoToClick(object? sender, RoutedEventArgs e) => CloseGoTo();

    private void CloseGoTo()
    {
        GoToOverlay.IsVisible = false;
        Editor.Focus();
    }

    private void OnSelectAllClick(object? sender, RoutedEventArgs e)
    {
        Editor.SelectAll();
        Editor.Focus();
    }

    private void OnTimeDateClick(object? sender, RoutedEventArgs e) => InsertTimeDate();

    private void InsertTimeDate()
    {
        var document = GetSelectedDocument();
        if (document is null)
        {
            return;
        }

        var text = NotepadEditLogic.GetDateTimeText();
        var insertionStart = Editor.SelectionStart;
        var undoStack = Editor.Document!.UndoStack;
        undoStack.StartUndoGroup();
        try
        {
            document.Buffer.Replace(insertionStart, Editor.SelectionLength, text);
        }
        finally
        {
            undoStack.EndUndoGroup();
        }

        Editor.SelectionLength = 0;
        Editor.SelectionStart = insertionStart + text.Length;
        RefreshAfterEditorCommand();
    }

    private void OnFontClick(object? sender, RoutedEventArgs e)
    {
        ShowSettingsPage();
        SettingsPage.ShowFontSettings();
    }

    private void OnPrintClick(object? sender, RoutedEventArgs e) => PrintSelectedDocument();

    private void PrintSelectedDocument()
    {
        var document = GetSelectedDocument();
        if (document is null)
        {
            return;
        }

        var printableDocument = new NotepadPrintDocument(
            document.DisplayName,
            document.CreateSnapshot(),
            _pageSettings);
        var result = _printService.Print(GetNativeWindowHandle(), printableDocument);
        ReportPrintResult(result, "The document was sent to the printer.");
    }

    private IntPtr GetNativeWindowHandle() => TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;

    private void ReportPrintResult(PlatformPrintResult result, string successMessage)
    {
        if (result == PlatformPrintResult.Accepted)
        {
            DocumentSummary.Text = successMessage;
        }
        else if (result is PlatformPrintResult.Failed or PlatformPrintResult.Unavailable)
        {
            DocumentSummary.Text = _printService.LastError ?? "The native printing service is unavailable.";
        }

        Editor.Focus();
    }

    private void OnExitClick(object? sender, RoutedEventArgs e)
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            App.RequestApplicationExit();
            foreach (var window in desktop.Windows.ToArray())
            {
                window.Close();
            }
        }
        else
        {
            Close();
        }
    }

    private void ConfigureFileMenuShortcuts()
    {
        Apply(NewTabMenuItem, NotepadShortcutIds.NewTab);
        Apply(NewWindowMenuItem, NotepadShortcutIds.NewWindow);
        Apply(OpenMenuItem, NotepadShortcutIds.Open);
        Apply(SaveMenuItem, NotepadShortcutIds.Save);
        Apply(SaveAsMenuItem, NotepadShortcutIds.SaveAs);
        Apply(SaveAllMenuItem, NotepadShortcutIds.SaveAll);
        Apply(PrintMenuItem, NotepadShortcutIds.Print);
        Apply(CloseTabMenuItem, NotepadShortcutIds.CloseTab);
        Apply(CloseWindowMenuItem, NotepadShortcutIds.CloseWindow);
        Apply(UndoMenuItem, NotepadShortcutIds.Undo);
        Apply(CutMenuItem, NotepadShortcutIds.Cut);
        Apply(CopyMenuItem, NotepadShortcutIds.Copy);
        Apply(PasteMenuItem, NotepadShortcutIds.Paste);
        Apply(DeleteMenuItem, NotepadShortcutIds.Delete);
        Apply(FindMenuItem, NotepadShortcutIds.Find);
        Apply(FindNextMenuItem, NotepadShortcutIds.FindNext);
        Apply(FindPreviousMenuItem, NotepadShortcutIds.FindPrevious);
        Apply(ReplaceMenuItem, NotepadShortcutIds.Replace);
        Apply(GoToMenuItem, NotepadShortcutIds.GoTo);
        Apply(SelectAllMenuItem, NotepadShortcutIds.SelectAll);
        Apply(TimeDateMenuItem, NotepadShortcutIds.TimeDate);
        Apply(ZoomInMenuItem, NotepadShortcutIds.ZoomIn);
        Apply(ZoomOutMenuItem, NotepadShortcutIds.ZoomOut);
        Apply(RestoreZoomMenuItem, NotepadShortcutIds.RestoreDefaultZoom);
        return;

        void Apply(MenuItem item, string shortcutId) =>
            item.InputGesture = AvaloniaShortcutAdapter.ToKeyGesture(_shortcutService.GetGesture(shortcutId));
    }

    private string FormatCommandToolTip(string commandName, string shortcutId) =>
        $"{commandName} ({_shortcutService.GetGestureDisplayText(shortcutId)})";

    private async void OnNotepadKeyDown(object? sender, KeyEventArgs e)
    {
        if (GoToOverlay.IsVisible)
        {
            if (e.Key == Key.Escape)
            {
                CloseGoTo();
                e.Handled = true;
            }
            else if (e.Key == Key.Enter)
            {
                ConfirmGoTo();
                e.Handled = true;
            }

            return;
        }

        if (UnsavedChangesOverlay.IsVisible
            || ExternalChangesOverlay.IsVisible
            || SettingsPage.IsVisible
            || !AvaloniaShortcutAdapter.TryCreateInput(e, out var input))
        {
            return;
        }

        var result = _shortcutService.Process(input);
        if (!result.WasMatched)
        {
            return;
        }

        e.Handled = result.Handled;
        switch (result[0].ShortcutId)
        {
            case NotepadShortcutIds.NewTab:
                CreateNewTab();
                break;
            case NotepadShortcutIds.NewWindow:
                OpenNewWindow();
                break;
            case NotepadShortcutIds.Open:
                await PickAndOpenFilesAsync();
                break;
            case NotepadShortcutIds.Save:
                await SaveSelectedDocumentAsync(forcePicker: false);
                break;
            case NotepadShortcutIds.SaveAs:
                await SaveSelectedDocumentAsync(forcePicker: true);
                break;
            case NotepadShortcutIds.SaveAll:
                await SaveAllDocumentsAsync();
                break;
            case NotepadShortcutIds.Print:
                PrintSelectedDocument();
                break;
            case NotepadShortcutIds.CloseTab:
                if (TabsList.SelectedItem is NotepadTabItem item)
                {
                    await TryCloseTabAsync(item);
                }
                break;
            case NotepadShortcutIds.CloseWindow:
                Close();
                break;
            case NotepadShortcutIds.Undo:
                UndoEditor();
                break;
            case NotepadShortcutIds.Cut:
                CutEditor();
                break;
            case NotepadShortcutIds.Copy:
                Editor.Copy();
                break;
            case NotepadShortcutIds.Paste:
                PasteEditor();
                break;
            case NotepadShortcutIds.Delete:
                DeleteEditor();
                break;
            case NotepadShortcutIds.Find:
                ShowFindAndReplace(showReplace: false);
                break;
            case NotepadShortcutIds.FindNext:
                FindNext();
                break;
            case NotepadShortcutIds.FindPrevious:
                FindPrevious();
                break;
            case NotepadShortcutIds.Replace:
                ShowFindAndReplace(showReplace: true);
                break;
            case NotepadShortcutIds.GoTo:
                ShowGoTo();
                break;
            case NotepadShortcutIds.SelectAll:
                Editor.SelectAll();
                break;
            case NotepadShortcutIds.TimeDate:
                InsertTimeDate();
                break;
            case NotepadShortcutIds.ZoomIn:
                ChangeZoom(ZoomStep);
                break;
            case NotepadShortcutIds.ZoomOut:
                ChangeZoom(-ZoomStep);
                break;
            case NotepadShortcutIds.RestoreDefaultZoom:
                SetZoom(100);
                break;
        }
    }

    private void OnWindowClosed(object? sender, EventArgs e)
    {
        NotepadSettingsStore.FontOptionsChanged -= OnGlobalFontOptionsChanged;
        NotepadSettingsStore.OpeningOptionsChanged -= OnGlobalOpeningOptionsChanged;
        NotepadSettingsStore.NativeMacOSMenuBarChanged -= OnGlobalNativeMacOSMenuBarChanged;
        NotepadSettingsStore.SpellingOptionsChanged -= OnGlobalSpellingOptionsChanged;
        _externalChangeCheckCancellation?.Cancel();
        _externalChangeCheckCancellation?.Dispose();
        _externalChangesDecision?.TrySetResult(ExternalChangesDecision.Cancel);
        _printService.Dispose();
        _spellCheckController.AutocorrectionChanged -= OnAutocorrectionChanged;
        _spellCheckController.Dispose();
        RemoveHandler(KeyDownEvent, OnNotepadKeyDown);
        RemoveHandler(PointerMovedEvent, OnWindowPointerMoved);
        PointerExited -= OnWindowPointerExited;
        foreach (var registration in _shortcutRegistrations)
        {
            registration.Dispose();
        }
    }

    private void CloseTab(NotepadTabItem item)
    {
        if (_tabs.Count == 1)
        {
            _discardSessionOnClose = true;
            NotepadSettingsStore.RemoveSessionWindow(_sessionId);
            _allowWindowClose = true;
            Close();
            return;
        }

        _isPointerOverTabBar |= TabBarArea.IsPointerOver;
        var tab = item.Tab;
        var replacement = _workspace.CloseTab(tab);
        _tabs.Remove(item);
        var replacementItem = _tabs.FirstOrDefault(candidate => ReferenceEquals(candidate.Tab, replacement));
        if (replacementItem is null)
        {
            replacementItem = CreateTabItem(replacement);
            _tabs.Add(replacementItem);
        }

        TabsList.SelectedItem = replacementItem;
        RefreshTabSeparators();
        LoadSelectedTab();
        ScheduleTabWidthUpdate(allowGrowth: true);
        ScheduleTabViewportUpdate(ensureSelectedTabIsVisible: true);
    }

    private async void OnWindowClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_allowWindowClose)
        {
            return;
        }

        var selectedBeforeClose = TabsList.SelectedItem as NotepadTabItem;
        if (_openingOptions.StartupMode == NotepadStartupMode.ContinuePreviousSession
            && ShouldPersistSessionForClose())
        {
            PersistSessionForClose(selectedBeforeClose);
            return;
        }

        var modifiedTabs = _tabs.Where(item => item.Tab.Document.IsModified).ToArray();
        if (modifiedTabs.Length == 0)
        {
            PersistSessionForClose(selectedBeforeClose);
            return;
        }

        e.Cancel = true;
        if (_isHandlingWindowClose)
        {
            return;
        }

        _isHandlingWindowClose = true;
        _discardedTabsOnClose.Clear();
        foreach (var item in modifiedTabs)
        {
            SelectTabWithoutExternalChangeCheck(item);
            var decision = await AskToSaveChangesAsync(item.Tab.Document);
            if (decision == UnsavedChangesDecision.Cancel
                || decision == UnsavedChangesDecision.Save
                && !await SaveSelectedDocumentAsync(forcePicker: false))
            {
                _discardedTabsOnClose.Clear();
                _isHandlingWindowClose = false;
                return;
            }

            if (decision == UnsavedChangesDecision.Discard)
            {
                _discardedTabsOnClose.Add(item.Tab.Id);
            }
        }

        PersistSessionForClose(selectedBeforeClose);
        _allowWindowClose = true;
        Close();
    }

    private void PersistSessionForClose(NotepadTabItem? selectedBeforeClose)
    {
        if (_discardSessionOnClose
            || _openingOptions.StartupMode != NotepadStartupMode.ContinuePreviousSession)
        {
            NotepadSettingsStore.RemoveSessionWindow(_sessionId);
            return;
        }

        if (!ShouldPersistSessionForClose())
        {
            NotepadSettingsStore.RemoveSessionWindow(_sessionId);
            return;
        }

        var documents = new List<NotepadSessionDocumentState>();
        var selectedIndex = 0;
        foreach (var item in _tabs)
        {
            var state = CreateSessionDocumentState(item);
            if (state is null)
            {
                continue;
            }

            if (ReferenceEquals(item, selectedBeforeClose))
            {
                selectedIndex = documents.Count;
            }

            documents.Add(state);
        }

        NotepadSettingsStore.SaveSessionWindow(new NotepadSessionWindowState(
            _sessionId,
            [.. documents],
            selectedIndex));
    }

    private bool ShouldPersistSessionForClose() =>
        App.IsApplicationExitRequested
        || Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop
        || desktop.Windows.Count <= 1;

    private NotepadSessionDocumentState? CreateSessionDocumentState(NotepadTabItem item)
    {
        var document = item.Tab.Document;
        if (_discardedTabsOnClose.Contains(item.Tab.Id))
        {
            return document.File is null
                ? null
                : new NotepadSessionDocumentState(document.File.Path, null);
        }

        return new NotepadSessionDocumentState(
            document.File?.Path,
            document.IsModified ? document.Text : null,
            document.IsModified ? document.Encoding.WebName : null,
            document.IsModified && document.Encoding.GetPreamble().Length > 0,
            document.LineEnding,
            document.IsModified ? document.FileVersion : null);
    }

    private void SelectTabWithoutExternalChangeCheck(NotepadTabItem item)
    {
        _suppressExternalChangeCheck = true;
        try
        {
            TabsList.SelectedItem = item;
        }
        finally
        {
            _suppressExternalChangeCheck = false;
        }
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

    private async void OnTabSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (TabsList.SelectedItem is not NotepadTabItem item || ReferenceEquals(item.Tab, _workspace.SelectedTab))
        {
            return;
        }

        _workspace.SelectTab(item.Tab);
        RefreshTabSeparators();
        LoadSelectedTab();
        ScheduleTabViewportUpdate(ensureSelectedTabIsVisible: true);

        if (!_suppressExternalChangeCheck
            && !_isHandlingWindowClose
            && _unsavedChangesDecision is null)
        {
            await CheckSelectedDocumentForExternalChangesAsync(item);
        }
    }

    private async Task CheckSelectedDocumentForExternalChangesAsync(NotepadTabItem item)
    {
        _externalChangeCheckCancellation?.Cancel();
        _externalChangeCheckCancellation?.Dispose();
        _externalChangeCheckCancellation = new CancellationTokenSource();
        var cancellationToken = _externalChangeCheckCancellation.Token;
        var document = item.Tab.Document;

        try
        {
            var change = await document.CheckForExternalChangesAsync(_fileStore, cancellationToken);
            if (change == ExternalFileChange.None
                || cancellationToken.IsCancellationRequested
                || !ReferenceEquals(item.Tab, _workspace.SelectedTab))
            {
                return;
            }

            if (change == ExternalFileChange.Modified && !document.IsModified)
            {
                await document.LoadAsync(_fileStore, document.File!, cancellationToken: cancellationToken);
                RefreshSavedDocument(item);
                DocumentSummary.Text = $"Reloaded {document.DisplayName} because it changed on disk.";
                return;
            }

            var decision = await AskAboutExternalChangesAsync(document, change);
            if (decision == ExternalChangesDecision.Reload)
            {
                await document.LoadAsync(_fileStore, document.File!, cancellationToken: cancellationToken);
                RefreshSavedDocument(item);
                return;
            }

            if (decision == ExternalChangesDecision.Overwrite)
            {
                await document.SaveAsync(
                    _fileStore,
                    document.File,
                    overwriteExternalChanges: true,
                    cancellationToken: cancellationToken);
                RefreshSavedDocument(item);
                return;
            }

            DocumentSummary.Text = change == ExternalFileChange.Deleted
                ? $"{document.DisplayName} no longer exists on disk; your editor copy is unchanged."
                : $"{document.DisplayName} differs from disk; your editor copy is unchanged.";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or DecoderFallbackException
                or EncoderFallbackException)
        {
            DocumentSummary.Text = $"Could not check {document.DisplayName}: {exception.Message}";
        }
    }

    private Task<ExternalChangesDecision> AskAboutExternalChangesAsync(
        NotepadDocument document,
        ExternalFileChange change)
    {
        if (_externalChangesDecision is not null)
        {
            return _externalChangesDecision.Task;
        }

        var wasDeleted = change == ExternalFileChange.Deleted;
        ExternalChangesTitle.Text = wasDeleted
            ? "File deleted or moved"
            : "File changed outside Redmond Notepad";
        ExternalChangesMessage.Text = wasDeleted
            ? $"{document.DisplayName} no longer exists at its original location. You can recreate it from your editor copy or cancel and keep working without changing the disk."
            : $"{document.DisplayName} changed on disk after this tab opened. Choose which version to keep.";
        ExternalChangesWarning.Text = wasDeleted
            ? "Recreate file writes your editor copy back to the original location."
            : "Reload discards your edits. Overwrite permanently replaces the other version.";
        ReloadExternalChangesButton.IsVisible = !wasDeleted;
        OverwriteExternalChangesButton.Content = wasDeleted ? "Recreate file" : "Overwrite";
        ExternalChangesOverlay.IsVisible = true;
        (wasDeleted ? OverwriteExternalChangesButton : ReloadExternalChangesButton).Focus();
        _externalChangesDecision = new TaskCompletionSource<ExternalChangesDecision>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        return _externalChangesDecision.Task;
    }

    private void OnReloadExternalChangesClick(object? sender, RoutedEventArgs e) =>
        ResolveExternalChanges(ExternalChangesDecision.Reload);

    private void OnOverwriteExternalChangesClick(object? sender, RoutedEventArgs e) =>
        ResolveExternalChanges(ExternalChangesDecision.Overwrite);

    private void OnCancelExternalChangesClick(object? sender, RoutedEventArgs e) =>
        ResolveExternalChanges(ExternalChangesDecision.Cancel);

    private void ResolveExternalChanges(ExternalChangesDecision decision)
    {
        var completion = _externalChangesDecision;
        _externalChangesDecision = null;
        ExternalChangesOverlay.IsVisible = false;
        completion?.TrySetResult(decision);
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

    private void OnWindowPointerMoved(object? sender, PointerEventArgs e)
    {
        var position = e.GetPosition(TabBarArea);
        var isPointerOverTabBar = DocumentTitleBar.IsVisible
            && position.X >= 0
            && position.X <= TabBarArea.Bounds.Width
            && position.Y >= 0
            && position.Y <= TabBarArea.Bounds.Height;

        if (_isPointerOverTabBar == isPointerOverTabBar)
        {
            return;
        }

        _isPointerOverTabBar = isPointerOverTabBar;
        if (!isPointerOverTabBar)
        {
            ScheduleTabWidthUpdate(allowGrowth: true);
        }
    }

    private void OnWindowPointerExited(object? sender, PointerEventArgs e)
    {
        if (!_isPointerOverTabBar)
        {
            return;
        }

        _isPointerOverTabBar = false;
        ScheduleTabWidthUpdate(allowGrowth: true);
    }

    private void OnTabBarSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        ScheduleTabWidthUpdate(allowGrowth: !_isPointerOverTabBar);
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

    private void ScheduleTabWidthUpdate(bool allowGrowth)
    {
        _allowTabGrowthOnNextUpdate |= allowGrowth;
        if (_isTabWidthUpdateScheduled)
        {
            return;
        }

        _isTabWidthUpdateScheduled = true;
        Dispatcher.UIThread.Post(() =>
        {
            _isTabWidthUpdateScheduled = false;
            var pointerIsOverTabBar = _isPointerOverTabBar || TabBarArea.IsPointerOver;
            var mayGrow = _allowTabGrowthOnNextUpdate && !pointerIsOverTabBar;
            _allowTabGrowthOnNextUpdate = false;
            ReconcileTabWidths(mayGrow);
        }, DispatcherPriority.Loaded);
    }

    private void ReconcileTabWidths(bool allowGrowth)
    {
        if (_tabs.Count == 0 || TabBarArea.Bounds.Width <= 0 || TabScrollViewer.Extent.Width <= 0)
        {
            return;
        }

        var currentWidth = CurrentTabWidth;
        var fixedContentWidth = Math.Max(0, TabScrollViewer.Extent.Width - currentWidth * _tabs.Count);
        var availableWidth = Math.Max(0, TabBarArea.Bounds.Width - NewTabButtonSlotWidth);
        var targetStep = _tabWidthStep;

        while (targetStep < TabWidthSteps.Length - 1
               && fixedContentWidth + TabWidthSteps[targetStep] * _tabs.Count > availableWidth + 0.5)
        {
            targetStep++;
        }

        if (allowGrowth && targetStep == _tabWidthStep)
        {
            while (targetStep > 0
                   && fixedContentWidth + TabWidthSteps[targetStep - 1] * _tabs.Count <= availableWidth + 0.5)
            {
                targetStep--;
            }
        }

        if (targetStep != _tabWidthStep)
        {
            _tabWidthStep = targetStep;
            ApplyCurrentTabWidth();
            ScheduleTabViewportUpdate(ensureSelectedTabIsVisible: true);
            return;
        }

        UpdateTabScrollButtons();
    }

    private void UpdateTabScrollButtons()
    {
        if (_isUpdatingTabScrollButtons)
        {
            return;
        }

        var tabAreaWidth = TabBarArea.Bounds.Width;
        var availableWidthWithoutChevrons = Math.Max(0, tabAreaWidth - NewTabButtonSlotWidth);
        var hasOverflow = TabScrollViewer.Extent.Width > availableWidthWithoutChevrons + 0.5;

        if (hasOverflow && _tabWidthStep < TabWidthSteps.Length - 1)
        {
            ScheduleTabWidthUpdate(allowGrowth: false);
            return;
        }

        TabBarControls.Width = hasOverflow
            ? tabAreaWidth
            : Math.Min(tabAreaWidth, TabScrollViewer.Extent.Width + NewTabButtonSlotWidth);

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
        SetTabScrollChevronState(TabScrollLeftButton, hasOverflow && TabScrollViewer.Offset.X > 0.5);
        SetTabScrollChevronState(
            TabScrollRightButton,
            hasOverflow && TabScrollViewer.Offset.X < maximumOffset - 0.5);
    }

    private static void SetTabScrollChevronState(RepeatButton button, bool canScroll)
    {
        button.IsEnabled = true;
        button.IsHitTestVisible = canScroll;
        button.Opacity = canScroll ? 1 : 0.5;
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

        var tabStart = selectedIndex * CurrentTabWidth;
        var tabEnd = tabStart + CurrentTabWidth;
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

    private double CurrentTabWidth => TabWidthSteps[_tabWidthStep];

    private NotepadTabItem CreateTabItem(NotepadTab tab) => new(tab, CurrentTabWidth);

    private void ApplyCurrentTabWidth()
    {
        foreach (var tab in _tabs)
        {
            tab.LayoutWidth = CurrentTabWidth;
        }
    }

    private static double[] CreateTabWidthSteps()
    {
        var widths = new List<double> { MaximumTabWidth };
        var width = MaximumTabWidth;
        while (width * TabWidthReductionFactor > MinimumTabWidth)
        {
            width *= TabWidthReductionFactor;
            widths.Add(width);
        }

        widths.Add(MinimumTabWidth);
        return [.. widths];
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
        ApplySpellCheckPolicy();
        Editor.Focus();
    }

    private void ApplySpellCheckPolicy()
    {
        var enabled = IsSpellCheckEnabledForSelectedTab();
        _spellCheckController.SetEnabled(enabled);
        _spellCheckController.SetAutocorrectMode(
            enabled ? _spellingOptions.AutocorrectMode : AutocorrectMode.Off);
    }

    private bool IsSpellCheckEnabledForSelectedTab()
    {
        var tab = _workspace.SelectedTab;
        return tab.IsSpellCheckEnabledOverride
            ?? _spellingOptions.IsEnabledForPath(tab.Document.File?.Path);
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
        WindowSurface.Classes.Set("backdrop", _appearanceController.IsBackdropActive);
        DocumentTitleBar.Background = _appearanceController.IsBackdropActive
            ? Brushes.Transparent
            : new SolidColorBrush(Color.Parse(dark ? "#202020" : "#F3F3F3"));
        WindowSurface.Background = new SolidColorBrush(Color.Parse(
            _appearanceController.IsBackdropActive
                ? dark ? "#18383D40" : "#18F3F3F3"
                : dark ? "#202020" : "#F3F3F3"));
    }

    private void OnEditorTextChanged(object? sender, EventArgs e)
    {
        if (_isLoadingTab)
        {
            return;
        }

        _spellCheckController.Refresh();
        RefreshAfterEditorCommand(focusEditor: false);
    }

    private void RefreshAfterEditorCommand(bool focusEditor = true)
    {
        RefreshDocumentStatus();
        RefreshCursorStatus();
        UpdateDocumentTitle();
        if (TabsList.SelectedItem is NotepadTabItem item)
        {
            item.RefreshTitle();
        }

        if (focusEditor)
        {
            Editor.Focus();
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
        Title = $"{document.DisplayName} - Notepad";
    }
}

internal sealed class NotepadTabItem(NotepadTab tab, double layoutWidth) : INotifyPropertyChanged
{
    private bool _showLeadingSeparator;
    private bool _showTrailingSeparator;
    private double _layoutWidth = layoutWidth;

    public NotepadTab Tab { get; } = tab;

    public string Title => Tab.Title;

    public bool ShowCloseGlyph => !Tab.Document.IsModified;

    public bool ShowModifiedGlyph => Tab.Document.IsModified;

    public double LayoutWidth
    {
        get => _layoutWidth;
        set
        {
            if (Math.Abs(_layoutWidth - value) < 0.01)
            {
                return;
            }

            _layoutWidth = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(LayoutWidth)));
        }
    }

    public void RefreshTitle()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Title)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ShowCloseGlyph)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ShowModifiedGlyph)));
    }

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

internal enum ExternalChangesDecision
{
    Reload,
    Overwrite,
    Cancel,
}
