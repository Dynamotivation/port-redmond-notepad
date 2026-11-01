using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using System.Collections.ObjectModel;
using Redmond.Avalonia.Controls;
using Redmond.Notepad.Core;
using Redmond.Shortcuts;

namespace Redmond.Notepad.Avalonia;

public partial class SettingsPage : UserControl
{
    private const double WideLayoutThreshold = 900;
    private readonly ObservableCollection<string> _fontFamilies = [];
    private bool _isUpdatingTheme;
    private bool _isUpdatingFont;
    private bool _isUpdatingDocumentOptions;
    private bool _isUpdatingSpelling;
    private bool _isUpdatingShortcutModifier;

    public SettingsPage()
    {
        InitializeComponent();
        foreach (var family in FontManager.Current.SystemFonts
                     .Select(item => item.Name)
                     .Distinct(StringComparer.OrdinalIgnoreCase)
                     .OrderBy(item => item, StringComparer.CurrentCultureIgnoreCase))
        {
            _fontFamilies.Add(family);
        }

        FontFamilyComboBox.ItemsSource = _fontFamilies;
        NativeMacOSMenuBarCard.IsVisible = OperatingSystem.IsMacOS();
        MacOSShortcutModifierCard.IsVisible = OperatingSystem.IsMacOS();
        WindowAppearancePanel.AppearanceChanged += OnAppearanceChanged;
        SizeChanged += (_, _) => UpdateResponsiveLayout(Bounds.Width);
        AttachedToVisualTree += (_, _) => UpdateResponsiveLayout(Bounds.Width);
    }

    public event EventHandler<WindowAppearanceOptions>? AppearanceChanged;

    public event EventHandler<AppThemePreference>? ThemeChanged;

    public event EventHandler<bool>? RecentFilesEnabledChanged;

    public event EventHandler<bool>? WordWrapChanged;

    public event EventHandler<bool>? FormattingEnabledChanged;

    public event EventHandler<bool>? NativeMacOSMenuBarChanged;

    internal event EventHandler<MacPrimaryModifier>? MacPrimaryModifierChanged;

    internal event EventHandler<NotepadOpeningOptions>? OpeningOptionsChanged;

    internal event EventHandler<NotepadFontOptions>? FontOptionsChanged;

    internal event EventHandler<NotepadSpellingOptions>? SpellingOptionsChanged;

    public void SetAppearance(WindowAppearanceOptions appearance) =>
        WindowAppearancePanel.SetOptions(appearance);

    public void SetThemePreference(AppThemePreference preference)
    {
        _isUpdatingTheme = true;
        SystemThemeButton.IsChecked = preference == AppThemePreference.System;
        LightThemeButton.IsChecked = preference == AppThemePreference.Light;
        DarkThemeButton.IsChecked = preference == AppThemePreference.Dark;
        _isUpdatingTheme = false;
    }

    public void SetRecentFilesEnabled(bool enabled) => RecentFilesToggle.IsChecked = enabled;

    public void SetWordWrap(bool enabled) => WordWrapToggle.IsChecked = enabled;

    public void SetFormattingEnabled(bool enabled) => FormattingToggle.IsChecked = enabled;

    public void SetUseNativeMacOSMenuBar(bool enabled) =>
        NativeMacOSMenuBarToggle.IsChecked = enabled;

    internal void SetMacPrimaryModifier(MacPrimaryModifier modifier)
    {
        _isUpdatingShortcutModifier = true;
        MacOSShortcutModifierComboBox.SelectedIndex = modifier == MacPrimaryModifier.Control ? 1 : 0;
        _isUpdatingShortcutModifier = false;
    }

    internal void SetSpellingOptions(NotepadSpellingOptions options)
    {
        options = options.Normalized();
        _isUpdatingSpelling = true;
        SpellCheckToggle.IsChecked = options.Enabled;
        TxtSpellCheckToggle.IsChecked = options.TxtEnabled;
        MarkdownSpellCheckToggle.IsChecked = options.MarkdownEnabled;
        SubtitleSpellCheckToggle.IsChecked = options.SubtitleEnabled;
        LyricsSpellCheckToggle.IsChecked = options.LyricsEnabled;
        LicenseSpellCheckToggle.IsChecked = options.LicenseEnabled;
        AutocorrectModeComboBox.SelectedIndex = (int)options.AutocorrectMode;
        SetSpellingChildrenEnabled(options.Enabled);
        _isUpdatingSpelling = false;
    }

    internal void SetOpeningOptions(NotepadOpeningOptions options)
    {
        options = options.Normalized();
        _isUpdatingDocumentOptions = true;
        OpeningFilesComboBox.SelectedIndex = options.FileOpeningMode == NotepadFileOpeningMode.NewWindow ? 1 : 0;
        NewWindowStartupButton.IsChecked = options.StartupMode == NotepadStartupMode.NewWindow;
        ContinueSessionStartupButton.IsChecked = options.StartupMode == NotepadStartupMode.ContinuePreviousSession;
        _isUpdatingDocumentOptions = false;
    }

    internal void SetFontOptions(NotepadFontOptions options)
    {
        options = options.Normalized();
        _isUpdatingFont = true;
        var installedFamily = _fontFamilies.FirstOrDefault(
            item => string.Equals(item, options.Family, StringComparison.OrdinalIgnoreCase));
        if (installedFamily is null)
        {
            installedFamily = options.Family;
            _fontFamilies.Add(installedFamily);
        }

        FontFamilyComboBox.SelectedItem = installedFamily;
        FontStyleComboBox.SelectedIndex = (int)options.Style;
        FontSizeBox.Value = (decimal)options.Size;
        _isUpdatingFont = false;
    }

    public void ShowFontSettings()
    {
        FontSettingsExpander.IsExpanded = true;
        FontSettingsExpander.BringIntoView();
        FontSettingsExpander.Focus();
    }

    private void OnThemeChecked(object? sender, RoutedEventArgs e)
    {
        if (_isUpdatingTheme)
        {
            return;
        }

        var preference = ReferenceEquals(sender, DarkThemeButton)
            ? AppThemePreference.Dark
            : ReferenceEquals(sender, LightThemeButton)
                ? AppThemePreference.Light
                : AppThemePreference.System;
        ThemeChanged?.Invoke(this, preference);
    }

    private void OnAppearanceChanged(object? sender, WindowAppearanceOptions options) =>
        AppearanceChanged?.Invoke(this, options);

    private void OnRecentFilesToggleClick(object? sender, RoutedEventArgs e) =>
        RecentFilesEnabledChanged?.Invoke(this, RecentFilesToggle.IsChecked == true);

    private void OnWordWrapToggleClick(object? sender, RoutedEventArgs e) =>
        WordWrapChanged?.Invoke(this, WordWrapToggle.IsChecked == true);

    private void OnFormattingToggleClick(object? sender, RoutedEventArgs e) =>
        FormattingEnabledChanged?.Invoke(this, FormattingToggle.IsChecked == true);

    private void OnNativeMacOSMenuBarToggleClick(object? sender, RoutedEventArgs e) =>
        NativeMacOSMenuBarChanged?.Invoke(this, NativeMacOSMenuBarToggle.IsChecked == true);

    private void OnMacOSShortcutModifierSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_isUpdatingShortcutModifier)
        {
            return;
        }

        MacPrimaryModifierChanged?.Invoke(
            this,
            MacOSShortcutModifierComboBox.SelectedIndex == 1
                ? MacPrimaryModifier.Control
                : MacPrimaryModifier.Command);
    }

    private void OnSpellingToggleClick(object? sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (_isUpdatingSpelling)
        {
            return;
        }

        RaiseSpellingOptionsChanged();
    }

    private void RaiseSpellingOptionsChanged()
    {
        var enabled = SpellCheckToggle.IsChecked == true;
        SetSpellingChildrenEnabled(enabled);
        SpellingOptionsChanged?.Invoke(this, new NotepadSpellingOptions(
            enabled,
            TxtSpellCheckToggle.IsChecked == true,
            MarkdownSpellCheckToggle.IsChecked == true,
            SubtitleSpellCheckToggle.IsChecked == true,
            LyricsSpellCheckToggle.IsChecked == true,
            LicenseSpellCheckToggle.IsChecked == true,
            (AutocorrectMode)Math.Max(0, AutocorrectModeComboBox.SelectedIndex)));
    }

    private void OnAutocorrectModeSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (!_isUpdatingSpelling)
        {
            RaiseSpellingOptionsChanged();
        }
    }

    private void SetSpellingChildrenEnabled(bool enabled)
    {
        TxtSpellCheckToggle.IsEnabled = enabled;
        MarkdownSpellCheckToggle.IsEnabled = enabled;
        SubtitleSpellCheckToggle.IsEnabled = enabled;
        LyricsSpellCheckToggle.IsEnabled = enabled;
        LicenseSpellCheckToggle.IsEnabled = enabled;
        AutocorrectModeComboBox.IsEnabled = enabled;
    }

    private void OnOpeningFilesSelectionChanged(object? sender, SelectionChangedEventArgs e) =>
        RaiseOpeningOptionsChanged();

    private void OnStartupModeChecked(object? sender, RoutedEventArgs e) =>
        RaiseOpeningOptionsChanged();

    private void RaiseOpeningOptionsChanged()
    {
        if (_isUpdatingDocumentOptions)
        {
            return;
        }

        OpeningOptionsChanged?.Invoke(this, new NotepadOpeningOptions(
            OpeningFilesComboBox.SelectedIndex == 1
                ? NotepadFileOpeningMode.NewWindow
                : NotepadFileOpeningMode.NewTab,
            ContinueSessionStartupButton.IsChecked == true
                ? NotepadStartupMode.ContinuePreviousSession
                : NotepadStartupMode.NewWindow));
    }

    private void OnFontSelectionChanged(object? sender, SelectionChangedEventArgs e) =>
        RaiseFontOptionsChanged();

    private void OnFontSizeChanged(object? sender, NumericUpDownValueChangedEventArgs e) =>
        RaiseFontOptionsChanged();

    private void RaiseFontOptionsChanged()
    {
        if (_isUpdatingFont || FontFamilyComboBox.SelectedItem is not string family)
        {
            return;
        }

        var styleIndex = Math.Clamp(FontStyleComboBox.SelectedIndex, 0, 3);
        FontOptionsChanged?.Invoke(this, new NotepadFontOptions(
            family,
            (NotepadEditorFontStyle)styleIndex,
            (double)(FontSizeBox.Value ?? (decimal)NotepadFontOptions.Default.Size)));
    }

    private void UpdateResponsiveLayout(double width)
    {
        var useWideLayout = width >= WideLayoutThreshold;
        SettingsLayout.ColumnDefinitions = useWideLayout
            ? new ColumnDefinitions("*,300")
            : new ColumnDefinitions("*,0");
        WideAboutPanel.IsVisible = useWideLayout;
        NarrowAboutPanel.IsVisible = !useWideLayout;
    }
}
