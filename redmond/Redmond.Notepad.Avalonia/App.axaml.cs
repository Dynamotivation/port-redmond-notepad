using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using Redmond.Notepad.Core;

namespace Redmond.Notepad.Avalonia;

public partial class App : Application
{
    private static NativeMenu? s_nativeMacOSApplicationMenu;
    private static NativeMenuItem? s_nativeMacOSSettingsItem;

    internal static bool IsApplicationExitRequested { get; private set; }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        if (OperatingSystem.IsMacOS())
        {
            NativeMenu.SetMenu(this, EnsureNativeMacOSApplicationMenu());
            SetNativeMacOSApplicationMenuEnabled(
                NotepadSettingsStore.LoadUseNativeMacOSMenuBar());
        }
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            IsApplicationExitRequested = false;
            desktop.ShutdownRequested += (_, _) => IsApplicationExitRequested = true;
            ApplyThemePreference(NotepadSettingsStore.LoadThemePreference());
            var arguments = desktop.Args ?? [];
            var openingOptions = NotepadSettingsStore.LoadOpeningOptions();
            if (arguments.Length > 0)
            {
                if (openingOptions.FileOpeningMode == NotepadFileOpeningMode.NewWindow)
                {
                    desktop.MainWindow = new MainWindow([arguments[0]]);
                    foreach (var path in arguments.Skip(1))
                    {
                        new MainWindow([path]).Show();
                    }
                }
                else
                {
                    desktop.MainWindow = new MainWindow(arguments);
                }
            }
            else if (openingOptions.StartupMode == NotepadStartupMode.ContinuePreviousSession)
            {
                var sessionWindows = NotepadSettingsStore.LoadSession().Windows;
                if (sessionWindows.Length == 0)
                {
                    desktop.MainWindow = new MainWindow();
                }
                else
                {
                    desktop.MainWindow = new MainWindow(sessionWindows[0]);
                    foreach (var sessionWindow in sessionWindows.Skip(1))
                    {
                        new MainWindow(sessionWindow).Show();
                    }
                }
            }
            else
            {
                NotepadSettingsStore.ClearSession();
                desktop.MainWindow = new MainWindow();
            }
        }

        base.OnFrameworkInitializationCompleted();
    }

    internal static void RequestApplicationExit() => IsApplicationExitRequested = true;

    internal static void SetNativeMacOSApplicationMenuEnabled(bool enabled)
    {
        if (!OperatingSystem.IsMacOS() || Current is null)
        {
            return;
        }

        EnsureNativeMacOSApplicationMenu();
        if (s_nativeMacOSSettingsItem is not null)
        {
            s_nativeMacOSSettingsItem.IsVisible = enabled;
        }
    }

    private static NativeMenu EnsureNativeMacOSApplicationMenu()
    {
        if (s_nativeMacOSApplicationMenu is not null)
        {
            return s_nativeMacOSApplicationMenu;
        }

        s_nativeMacOSApplicationMenu = new NativeMenu();
        s_nativeMacOSSettingsItem = new NativeMenuItem("Settings…")
        {
            Gesture = new KeyGesture(Key.OemComma, KeyModifiers.Meta),
        };
        s_nativeMacOSSettingsItem.Click += (_, _) =>
        {
            if (Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
            {
                return;
            }

            var window = desktop.Windows
                .OfType<MainWindow>()
                .FirstOrDefault(candidate => candidate.IsActive)
                ?? desktop.Windows.OfType<MainWindow>().FirstOrDefault();
            window?.ShowSettingsPage();
        };
        s_nativeMacOSApplicationMenu.Add(s_nativeMacOSSettingsItem);
        return s_nativeMacOSApplicationMenu;
    }

    internal static void ApplyThemePreference(AppThemePreference preference)
    {
        if (Current is null)
        {
            return;
        }

        Current.RequestedThemeVariant = preference switch
        {
            AppThemePreference.Light => ThemeVariant.Light,
            AppThemePreference.Dark => ThemeVariant.Dark,
            _ => ThemeVariant.Default,
        };
    }
}
