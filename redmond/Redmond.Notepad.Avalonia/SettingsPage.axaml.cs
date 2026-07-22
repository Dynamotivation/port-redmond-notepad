using Avalonia.Controls;
using Avalonia.Interactivity;
using Redmond.Avalonia.Controls;

namespace Redmond.Notepad.Avalonia;

public partial class SettingsPage : UserControl
{
    private const double WideLayoutThreshold = 900;
    private bool _isUpdatingTheme;

    public SettingsPage()
    {
        InitializeComponent();
        WindowAppearancePanel.AppearanceChanged += OnAppearanceChanged;
        SizeChanged += (_, _) => UpdateResponsiveLayout(Bounds.Width);
        AttachedToVisualTree += (_, _) => UpdateResponsiveLayout(Bounds.Width);
    }

    public event EventHandler<WindowAppearanceOptions>? AppearanceChanged;

    public event EventHandler<AppThemePreference>? ThemeChanged;

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
