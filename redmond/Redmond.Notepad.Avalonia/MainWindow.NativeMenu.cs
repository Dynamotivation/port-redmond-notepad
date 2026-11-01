using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Redmond.Notepad.Core;
using Redmond.Shortcuts;

namespace Redmond.Notepad.Avalonia;

public partial class MainWindow
{
    private readonly List<(NativeMenuItem Item, string ShortcutId)> _nativeShortcutItems = [];
    private NativeMenu? _nativeMacOSWindowMenu;
    private NativeMenu? _nativeRecentFilesMenu;
    private NativeMenuItem? _nativeRecentFilesItem;
    private NativeMenuItem? _nativeNewMarkdownTabItem;
    private NativeMenuItem? _nativeClearFormattingItem;
    private NativeMenuItem? _nativeMarkdownItem;
    private NativeMenuItem? _nativeUndoItem;
    private NativeMenuItem? _nativeCutItem;
    private NativeMenuItem? _nativeCopyItem;
    private NativeMenuItem? _nativePasteItem;
    private NativeMenuItem? _nativeDeleteItem;
    private NativeMenuItem? _nativeFindItem;
    private NativeMenuItem? _nativeFindNextItem;
    private NativeMenuItem? _nativeFindPreviousItem;
    private NativeMenuItem? _nativeReplaceItem;
    private NativeMenuItem? _nativeGoToItem;
    private NativeMenuItem? _nativeZoomInItem;
    private NativeMenuItem? _nativeZoomOutItem;
    private NativeMenuItem? _nativeRestoreZoomItem;
    private NativeMenuItem? _nativeStatusBarItem;
    private NativeMenuItem? _nativeWordWrapItem;

    private void ApplyMacOSMenuPresentation()
    {
        var useNativeMenu = OperatingSystem.IsMacOS() && _useNativeMacOSMenuBar;
        DocumentMenuBarHost.IsVisible = !useNativeMenu;
        DocumentPage.RowDefinitions[0].Height =
            useNativeMenu ? new GridLength(0) : new GridLength(38);

        if (!OperatingSystem.IsMacOS())
        {
            return;
        }

        NativeMenu.SetMenu(this, useNativeMenu ? EnsureNativeMacOSWindowMenu() : null);
        App.SetNativeMacOSApplicationMenuEnabled(useNativeMenu);
    }

    private NativeMenu EnsureNativeMacOSWindowMenu()
    {
        if (_nativeMacOSWindowMenu is not null)
        {
            return _nativeMacOSWindowMenu;
        }

        _nativeMacOSWindowMenu = new NativeMenu();
        _nativeMacOSWindowMenu.Add(CreateNativeFileMenu());
        _nativeMacOSWindowMenu.Add(CreateNativeEditMenu());
        _nativeMacOSWindowMenu.Add(CreateNativeViewMenu());
        return _nativeMacOSWindowMenu;
    }

    private NativeMenuItem CreateNativeFileMenu()
    {
        var menu = new NativeMenu();
        menu.Add(CreateNativeCommand("New tab", () => CreateNewTab(), NotepadShortcutIds.NewTab));
        menu.Add(CreateNativeCommand("New window", OpenNewWindow, NotepadShortcutIds.NewWindow));
        _nativeNewMarkdownTabItem = new NativeMenuItem("New Markdown tab")
        {
            IsEnabled = false,
        };
        menu.Add(_nativeNewMarkdownTabItem);
        menu.Add(CreateNativeCommand(
            "Open…",
            () => OnOpenFilesClick(null, new RoutedEventArgs()),
            NotepadShortcutIds.Open));

        _nativeRecentFilesMenu = new NativeMenu();
        _nativeRecentFilesMenu.NeedsUpdate += (_, _) => RefreshNativeRecentFilesMenu();
        _nativeRecentFilesItem = new NativeMenuItem("Recent")
        {
            Menu = _nativeRecentFilesMenu,
        };
        menu.Add(_nativeRecentFilesItem);
        menu.Add(CreateNativeCommand(
            "Save",
            () => OnSaveClick(null, new RoutedEventArgs()),
            NotepadShortcutIds.Save));
        menu.Add(CreateNativeCommand(
            "Save As…",
            () => OnSaveAsClick(null, new RoutedEventArgs()),
            NotepadShortcutIds.SaveAs));
        menu.Add(CreateNativeCommand(
            "Save All",
            () => OnSaveAllClick(null, new RoutedEventArgs()),
            NotepadShortcutIds.SaveAll));
        menu.Add(new NativeMenuItemSeparator());
        menu.Add(CreateNativeCommand("Page Setup…", ShowPageSetup));
        menu.Add(CreateNativeCommand("Print…", PrintSelectedDocument, NotepadShortcutIds.Print));
        menu.Add(new NativeMenuItemSeparator());
        menu.Add(CreateNativeCommand(
            "Close Tab",
            () => OnCloseSelectedTabClick(null, new RoutedEventArgs()),
            NotepadShortcutIds.CloseTab));
        menu.Add(CreateNativeCommand("Close Window", Close, NotepadShortcutIds.CloseWindow));
        menu.NeedsUpdate += (_, _) => RefreshNativeFileMenu();

        return new NativeMenuItem("File") { Menu = menu };
    }

    private NativeMenuItem CreateNativeEditMenu()
    {
        var menu = new NativeMenu();
        _nativeUndoItem = CreateNativeCommand("Undo", () => OnUndoClick(null, new RoutedEventArgs()), NotepadShortcutIds.Undo);
        menu.Add(_nativeUndoItem);
        menu.Add(new NativeMenuItemSeparator());
        _nativeCutItem = CreateNativeCommand("Cut", () => OnCutClick(null, new RoutedEventArgs()), NotepadShortcutIds.Cut);
        _nativeCopyItem = CreateNativeCommand("Copy", () => OnCopyClick(null, new RoutedEventArgs()), NotepadShortcutIds.Copy);
        _nativePasteItem = CreateNativeCommand("Paste", () => OnPasteClick(null, new RoutedEventArgs()), NotepadShortcutIds.Paste);
        _nativeDeleteItem = CreateNativeCommand("Delete", () => OnDeleteClick(null, new RoutedEventArgs()), NotepadShortcutIds.Delete);
        menu.Add(_nativeCutItem);
        menu.Add(_nativeCopyItem);
        menu.Add(_nativePasteItem);
        menu.Add(_nativeDeleteItem);
        menu.Add(new NativeMenuItemSeparator());
        _nativeClearFormattingItem = new NativeMenuItem("Clear Formatting")
        {
            IsEnabled = false,
        };
        menu.Add(_nativeClearFormattingItem);
        menu.Add(new NativeMenuItemSeparator());
        _nativeFindItem = CreateNativeCommand("Find…", () => OnFindClick(null, new RoutedEventArgs()), NotepadShortcutIds.Find);
        _nativeFindNextItem = CreateNativeCommand("Find Next", () => OnFindNextClick(null, new RoutedEventArgs()), NotepadShortcutIds.FindNext);
        _nativeFindPreviousItem = CreateNativeCommand("Find Previous", () => OnFindPreviousClick(null, new RoutedEventArgs()), NotepadShortcutIds.FindPrevious);
        _nativeReplaceItem = CreateNativeCommand("Replace…", () => OnReplaceClick(null, new RoutedEventArgs()), NotepadShortcutIds.Replace);
        _nativeGoToItem = CreateNativeCommand("Go To…", () => OnGoToClick(null, new RoutedEventArgs()), NotepadShortcutIds.GoTo);
        menu.Add(_nativeFindItem);
        menu.Add(_nativeFindNextItem);
        menu.Add(_nativeFindPreviousItem);
        menu.Add(_nativeReplaceItem);
        menu.Add(_nativeGoToItem);
        menu.Add(new NativeMenuItemSeparator());
        menu.Add(CreateNativeCommand("Select All", () => OnSelectAllClick(null, new RoutedEventArgs()), NotepadShortcutIds.SelectAll));
        menu.Add(CreateNativeCommand("Time/Date", InsertTimeDate, NotepadShortcutIds.TimeDate));
        menu.Add(new NativeMenuItemSeparator());
        menu.Add(CreateNativeCommand("Font…", () =>
        {
            ShowSettingsPage();
            SettingsPage.ShowFontSettings();
        }));
        menu.NeedsUpdate += (_, _) => RefreshNativeEditMenu();

        return new NativeMenuItem("Edit") { Menu = menu };
    }

    private NativeMenuItem CreateNativeViewMenu()
    {
        var menu = new NativeMenu();
        var zoomMenu = new NativeMenu();
        _nativeZoomInItem = CreateNativeCommand("Zoom In", () => ChangeZoom(ZoomStep), NotepadShortcutIds.ZoomIn);
        _nativeZoomOutItem = CreateNativeCommand("Zoom Out", () => ChangeZoom(-ZoomStep), NotepadShortcutIds.ZoomOut);
        _nativeRestoreZoomItem = CreateNativeCommand("Actual Size", () => SetZoom(100), NotepadShortcutIds.RestoreDefaultZoom);
        zoomMenu.Add(_nativeZoomInItem);
        zoomMenu.Add(_nativeZoomOutItem);
        zoomMenu.Add(_nativeRestoreZoomItem);
        zoomMenu.NeedsUpdate += (_, _) => RefreshNativeViewMenu();
        menu.Add(new NativeMenuItem("Zoom") { Menu = zoomMenu });

        _nativeStatusBarItem = CreateNativeCommand("Status Bar", () => OnStatusBarClick(null, new RoutedEventArgs()));
        _nativeStatusBarItem.ToggleType = MenuItemToggleType.CheckBox;
        _nativeWordWrapItem = CreateNativeCommand("Word Wrap", () => OnWordWrapClick(null, new RoutedEventArgs()));
        _nativeWordWrapItem.ToggleType = MenuItemToggleType.CheckBox;
        menu.Add(_nativeStatusBarItem);
        menu.Add(_nativeWordWrapItem);
        _nativeMarkdownItem = new NativeMenuItem("Markdown")
        {
            IsEnabled = false,
        };
        menu.Add(_nativeMarkdownItem);
        menu.NeedsUpdate += (_, _) => RefreshNativeViewMenu();

        return new NativeMenuItem("View") { Menu = menu };
    }

    private NativeMenuItem CreateNativeCommand(string header, Action action, string? shortcutId = null)
    {
        var item = new NativeMenuItem(header);
        if (shortcutId is not null)
        {
            item.Gesture = AvaloniaShortcutAdapter.ToKeyGesture(_shortcutService.GetGesture(shortcutId));
            _nativeShortcutItems.Add((item, shortcutId));
        }

        item.Click += (_, _) => action();
        return item;
    }

    private void RefreshNativeMenuGestures()
    {
        foreach (var (item, shortcutId) in _nativeShortcutItems)
        {
            item.Gesture = AvaloniaShortcutAdapter.ToKeyGesture(_shortcutService.GetGesture(shortcutId));
        }
    }

    private void RefreshNativeFileMenu()
    {
        if (_recentFilesEnabled)
        {
            _recentFiles = NotepadSettingsStore.LoadRecentFiles();
        }

        if (_nativeRecentFilesItem is not null)
        {
            _nativeRecentFilesItem.IsVisible = _recentFilesEnabled;
        }

        if (_nativeNewMarkdownTabItem is not null)
        {
            _nativeNewMarkdownTabItem.IsVisible = _viewOptions.FormattingEnabled;
        }

        RefreshNativeRecentFilesMenu();
    }

    private void RefreshNativeRecentFilesMenu()
    {
        if (_nativeRecentFilesMenu is null)
        {
            return;
        }

        _nativeRecentFilesMenu.Items.Clear();
        if (!_recentFilesEnabled)
        {
            return;
        }

        foreach (var path in _recentFiles.Paths)
        {
            var recentPath = path;
            var item = CreateNativeCommand(
                Path.GetFileName(recentPath),
                () => OpenRecentFileFromNativeMenu(recentPath));
            item.ToolTip = recentPath;
            _nativeRecentFilesMenu.Add(item);
        }

        if (_recentFiles.Paths.Count == 0)
        {
            _nativeRecentFilesMenu.Add(new NativeMenuItem("No Recent Files")
            {
                IsEnabled = false,
            });
            return;
        }

        _nativeRecentFilesMenu.Add(new NativeMenuItemSeparator());
        _nativeRecentFilesMenu.Add(CreateNativeCommand("Clear List", () =>
        {
            NotepadSettingsStore.ClearRecentFiles();
            _recentFiles = new RecentFileList();
            RefreshRecentFilesMenu();
            RefreshNativeRecentFilesMenu();
        }));
    }

    private async void OpenRecentFileFromNativeMenu(string path)
    {
        if (!File.Exists(path))
        {
            _recentFiles = NotepadSettingsStore.RemoveRecentFile(path);
            RefreshRecentFilesMenu();
            RefreshNativeRecentFilesMenu();
            DocumentSummary.Text = $"Could not find {Path.GetFileName(path)}.";
            return;
        }

        await OpenFilesAccordingToPreferenceAsync([new DocumentFileReference(path)]);
    }

    private void RefreshNativeEditMenu()
    {
        var hasSelection = Editor.SelectionLength > 0;
        if (_nativeUndoItem is not null) _nativeUndoItem.IsEnabled = Editor.CanUndo;
        if (_nativeCutItem is not null) _nativeCutItem.IsEnabled = hasSelection;
        if (_nativeCopyItem is not null) _nativeCopyItem.IsEnabled = hasSelection;
        if (_nativePasteItem is not null) _nativePasteItem.IsEnabled = Editor.CanPaste;
        if (_nativeDeleteItem is not null) _nativeDeleteItem.IsEnabled = hasSelection;
        if (_nativeFindItem is not null) _nativeFindItem.IsEnabled = Editor.CanSearch;
        if (_nativeFindNextItem is not null) _nativeFindNextItem.IsEnabled = Editor.CanSearch;
        if (_nativeFindPreviousItem is not null) _nativeFindPreviousItem.IsEnabled = Editor.CanSearch;
        if (_nativeReplaceItem is not null) _nativeReplaceItem.IsEnabled = Editor.CanSearch;
        if (_nativeGoToItem is not null) _nativeGoToItem.IsEnabled = Editor.Document?.LineCount > 0;
    }

    private void RefreshNativeViewMenu()
    {
        if (_nativeStatusBarItem is not null) _nativeStatusBarItem.IsChecked = _viewOptions.ShowStatusBar;
        if (_nativeWordWrapItem is not null) _nativeWordWrapItem.IsChecked = _viewOptions.WordWrap;
        if (_nativeZoomInItem is not null) _nativeZoomInItem.IsEnabled = _viewOptions.ZoomPercent < 500;
        if (_nativeZoomOutItem is not null) _nativeZoomOutItem.IsEnabled = _viewOptions.ZoomPercent > 10;
        if (_nativeRestoreZoomItem is not null) _nativeRestoreZoomItem.IsEnabled = _viewOptions.ZoomPercent != 100;
        if (_nativeMarkdownItem is not null)
        {
            _nativeMarkdownItem.IsVisible = _viewOptions.FormattingEnabled;
        }
    }
}
