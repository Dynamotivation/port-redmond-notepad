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
    private static NotepadProcessRegistration? s_processRegistration;

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
            s_processRegistration = NotepadSettingsStore.RegisterProcess();
            desktop.Exit += (_, _) =>
            {
                s_processRegistration?.Dispose();
                s_processRegistration = null;
            };
            ApplyThemePreference(NotepadSettingsStore.LoadThemePreference());
            var rawArguments = desktop.Args ?? [];
            var forceNewWindow = rawArguments.Contains(
                ProcessLaunchService.NewWindowArgument,
                StringComparer.Ordinal);
            var arguments = rawArguments
                .Where(argument => !string.Equals(
                    argument,
                    ProcessLaunchService.NewWindowArgument,
                    StringComparison.Ordinal))
                .ToArray();
            var openingOptions = NotepadSettingsStore.LoadOpeningOptions();
            if (arguments.Length > 0)
            {
                if (openingOptions.FileOpeningMode == NotepadFileOpeningMode.NewWindow)
                {
                    desktop.MainWindow = new MainWindow([arguments[0]]);
                    foreach (var path in arguments.Skip(1))
                    {
                        ProcessLaunchService.TryStartNewWindow([path], out _);
                    }
                }
                else
                {
                    desktop.MainWindow = new MainWindow(arguments);
                }
            }
            else if (!forceNewWindow
                && !s_processRegistration.HasOtherActiveProcesses
                && openingOptions.StartupMode == NotepadStartupMode.ContinuePreviousSession)
            {
                var sessionWindow = NotepadSettingsStore.LoadMostRecentSessionWindow();
                if (sessionWindow is null)
                {
                    desktop.MainWindow = new MainWindow();
                }
                else
                {
                    desktop.MainWindow = new MainWindow(sessionWindow);
                }
            }
            else
            {
                if (!s_processRegistration.HasOtherActiveProcesses
                    && openingOptions.StartupMode == NotepadStartupMode.NewWindow)
                {
                    NotepadSettingsStore.ClearSession();
                }

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
