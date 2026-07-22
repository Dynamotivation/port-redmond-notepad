using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using System.Collections.ObjectModel;
using Redmond.Avalonia.Controls;

namespace Redmond.Notepad.Avalonia;

public partial class SettingsPage : UserControl
{
    private const double WideLayoutThreshold = 900;
    private readonly ObservableCollection<string> _fontFamilies = [];
    private bool _isUpdatingTheme;
    private bool _isUpdatingFont;

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
        WindowAppearancePanel.AppearanceChanged += OnAppearanceChanged;
        SizeChanged += (_, _) => UpdateResponsiveLayout(Bounds.Width);
        AttachedToVisualTree += (_, _) => UpdateResponsiveLayout(Bounds.Width);
    }

    public event EventHandler<WindowAppearanceOptions>? AppearanceChanged;

    public event EventHandler<AppThemePreference>? ThemeChanged;

    public event EventHandler<bool>? RecentFilesEnabledChanged;

    public event EventHandler<bool>? WordWrapChanged;

    internal event EventHandler<NotepadFontOptions>? FontOptionsChanged;

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
