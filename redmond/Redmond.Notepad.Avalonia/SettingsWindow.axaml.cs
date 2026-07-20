using Avalonia.Controls;
using Redmond.Avalonia.Windowing;

namespace Redmond.Notepad.Avalonia;

public partial class SettingsWindow : Window
{
    private const double WideLayoutThreshold = 900;

    public SettingsWindow()
        : this(new WindowAppearanceOptions())
    {
    }

    public SettingsWindow(WindowAppearanceOptions appearance)
    {
        InitializeComponent();
        WindowAppearancePanel.SetOptions(appearance);
        WindowAppearancePanel.AppearanceChanged += (_, options) => AppearanceChanged?.Invoke(this, options);
        SizeChanged += (_, _) => UpdateResponsiveLayout(Bounds.Width);
        Opened += (_, _) => UpdateResponsiveLayout(Bounds.Width);
    }

    public event EventHandler<WindowAppearanceOptions>? AppearanceChanged;

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
