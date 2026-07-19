using Avalonia.Controls;

namespace Redmond.Notepad.Avalonia;

public partial class SettingsWindow : Window
{
    private const double WideLayoutThreshold = 900;

    public SettingsWindow()
    {
        InitializeComponent();
        SizeChanged += (_, _) => UpdateResponsiveLayout(Bounds.Width);
        Opened += (_, _) => UpdateResponsiveLayout(Bounds.Width);
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
