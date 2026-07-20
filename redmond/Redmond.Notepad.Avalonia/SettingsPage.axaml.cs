using Avalonia.Controls;
using Redmond.Avalonia.Controls;

namespace Redmond.Notepad.Avalonia;

public partial class SettingsPage : UserControl
{
    private const double WideLayoutThreshold = 900;

    public SettingsPage()
    {
        InitializeComponent();
        WindowAppearancePanel.AppearanceChanged += OnAppearanceChanged;
        SizeChanged += (_, _) => UpdateResponsiveLayout(Bounds.Width);
        AttachedToVisualTree += (_, _) => UpdateResponsiveLayout(Bounds.Width);
    }

    public event EventHandler<WindowAppearanceOptions>? AppearanceChanged;

    public void SetAppearance(WindowAppearanceOptions appearance) =>
        WindowAppearancePanel.SetOptions(appearance);

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
