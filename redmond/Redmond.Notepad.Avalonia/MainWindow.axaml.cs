using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
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
using Redmond.Notepad.Avalonia.Printing;
using Redmond.Shortcuts;

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
    private readonly IShortcutService _shortcutService;
    private readonly IReadOnlyList<IDisposable> _shortcutRegistrations;
    private readonly WindowAppearanceController _appearanceController;
    private readonly IPlatformPrintService _printService;
    private WindowAppearanceOptions _appearance;
    private NotepadPageSettings _pageSettings = new();
    private AppThemePreference _themePreference;
    private bool _isLoadingTab;
    private bool _isUpdatingTabScrollButtons;
    private bool _allowWindowClose;
    private bool _isHandlingWindowClose;
    private bool _suppressExternalChangeCheck;
    private INotepadFilePicker? _filePicker;
    private TaskCompletionSource<UnsavedChangesDecision>? _unsavedChangesDecision;
    private TaskCompletionSource<ExternalChangesDecision>? _externalChangesDecision;
    private CancellationTokenSource? _externalChangeCheckCancellation;

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
        _shortcutService = ShortcutServices.CreateForCurrentPlatform();
        _shortcutRegistrations = NotepadShortcutCatalog.CreateDefinitions()
            .Select(definition => _shortcutService.Register(definition))
            .ToArray();
        _printService = PlatformPrintServiceFactory.Create();
        ConfigureFileMenuShortcuts();
        AddHandler(KeyDownEvent, OnNotepadKeyDown, RoutingStrategies.Tunnel);
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

        Editor.Focus();
        ScheduleTabViewportUpdate(ensureSelectedTabIsVisible: true);
    }

    private void OnSettingsClick(object? sender, RoutedEventArgs e)
        => ShowSettingsPage();

    private void ShowSettingsPage()
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

    private void OnNewTabClick(object? sender, RoutedEventArgs e) => CreateNewTab();

    private void CreateNewTab()
    {
        var tab = _workspace.CreateTab();
        var item = new NotepadTabItem(tab);
        _tabs.Add(item);
        TabsList.SelectedItem = item;
        RefreshTabSeparators();
        LoadSelectedTab();
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

    private void OnEditMenuOpened(object? sender, EventArgs e)
    {
        UndoMenuItem.IsEnabled = Editor.CanUndo;
        CutMenuItem.IsEnabled = Editor.CanCut;
        CopyMenuItem.IsEnabled = Editor.CanCopy;
        PasteMenuItem.IsEnabled = Editor.CanPaste;
        DeleteMenuItem.IsEnabled = Editor.CanDelete;
        FindMenuItem.IsEnabled = Editor.CanSearch;
        FindNextMenuItem.IsEnabled = Editor.CanSearch;
        FindPreviousMenuItem.IsEnabled = Editor.CanSearch;
        ReplaceMenuItem.IsEnabled = Editor.CanSearch;
        GoToMenuItem.IsEnabled = Editor.Document?.LineCount > 0;
        SelectAllMenuItem.IsEnabled = Editor.CanSelectAll;
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
        return;

        void Apply(MenuItem item, string shortcutId) =>
            item.InputGesture = AvaloniaShortcutAdapter.ToKeyGesture(_shortcutService.GetGesture(shortcutId));
    }

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
        }
    }

    private void OnWindowClosed(object? sender, EventArgs e)
    {
        _externalChangeCheckCancellation?.Cancel();
        _externalChangeCheckCancellation?.Dispose();
        _externalChangesDecision?.TrySetResult(ExternalChangesDecision.Cancel);
        _printService.Dispose();
        RemoveHandler(KeyDownEvent, OnNotepadKeyDown);
        foreach (var registration in _shortcutRegistrations)
        {
            registration.Dispose();
        }
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
            SelectTabWithoutExternalChangeCheck(item);
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

internal sealed class NotepadTabItem(NotepadTab tab) : INotifyPropertyChanged
{
    private bool _showLeadingSeparator;
    private bool _showTrailingSeparator;

    public NotepadTab Tab { get; } = tab;

    public string Title => Tab.Title;

    public bool ShowCloseGlyph => !Tab.Document.IsModified;

    public bool ShowModifiedGlyph => Tab.Document.IsModified;

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
