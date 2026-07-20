using Avalonia.Controls;
using Redmond.Avalonia.Windowing;

namespace Redmond.Notepad.Avalonia;

public partial class SettingsWindow : Window
{
    private const double WideLayoutThreshold = 900;
    private readonly WindowAppearanceController _appearanceController;

    public SettingsWindow()
        : this(new WindowAppearanceOptions())
    {
    }

    public SettingsWindow(WindowAppearanceOptions appearance)
    {
        InitializeComponent();
        _appearanceController = WindowAppearanceController.Attach(
            this,
            options: appearance,
            manageDecorations: false);
        WindowAppearancePanel.SetOptions(appearance);
        WindowAppearancePanel.AppearanceChanged += OnAppearanceChanged;
        UpdateMaterialSurface(appearance);
        SizeChanged += (_, _) => UpdateResponsiveLayout(Bounds.Width);
        Opened += (_, _) => UpdateResponsiveLayout(Bounds.Width);
    }

    public event EventHandler<WindowAppearanceOptions>? AppearanceChanged;

    private void OnAppearanceChanged(object? sender, WindowAppearanceOptions options)
    {
        _appearanceController.Apply(options);
        UpdateMaterialSurface(options);
        AppearanceChanged?.Invoke(this, options);
    }

    private void UpdateMaterialSurface(WindowAppearanceOptions options) =>
        SettingsSurface.Classes.Set("noBackdrop", !options.UseSystemBackdrop);

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
