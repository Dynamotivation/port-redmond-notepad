using Avalonia.Input;
using Redmond.Shortcuts;

namespace Redmond.Notepad.Avalonia;

internal static class AvaloniaShortcutAdapter
{
    public static bool TryCreateInput(KeyEventArgs args, out ShortcutInput input)
    {
        var keyName = args.Key.ToString().ToUpperInvariant();
        if (args.Key is Key.None
            or Key.LeftCtrl or Key.RightCtrl
            or Key.LeftAlt or Key.RightAlt
            or Key.LeftShift or Key.RightShift
            or Key.LWin or Key.RWin)
        {
            keyName = string.Empty;
        }
        if (keyName.Length == 0)
        {
            input = default;
            return false;
        }

        var modifiers = ShortcutModifiers.None;
        if (args.KeyModifiers.HasFlag(KeyModifiers.Control)) modifiers |= ShortcutModifiers.Control;
        if (args.KeyModifiers.HasFlag(KeyModifiers.Alt)) modifiers |= ShortcutModifiers.Alt;
        if (args.KeyModifiers.HasFlag(KeyModifiers.Shift)) modifiers |= ShortcutModifiers.Shift;
        if (args.KeyModifiers.HasFlag(KeyModifiers.Meta)) modifiers |= ShortcutModifiers.Command;

        input = new ShortcutInput(new ShortcutGesture(ShortcutKey.Named(keyName), modifiers));
        return true;
    }

    public static KeyGesture ToKeyGesture(ShortcutGesture gesture)
    {
        if (!Enum.TryParse<Key>(gesture.Key.Value, ignoreCase: true, out var key))
        {
            throw new NotSupportedException($"The Avalonia adapter cannot display the key '{gesture.Key.Value}'.");
        }

        var modifiers = KeyModifiers.None;
        if (gesture.Modifiers.HasFlag(ShortcutModifiers.Control)) modifiers |= KeyModifiers.Control;
        if (gesture.Modifiers.HasFlag(ShortcutModifiers.Alt)) modifiers |= KeyModifiers.Alt;
        if (gesture.Modifiers.HasFlag(ShortcutModifiers.Shift)) modifiers |= KeyModifiers.Shift;
        if (gesture.Modifiers.HasFlag(ShortcutModifiers.Command)) modifiers |= KeyModifiers.Meta;
        return new KeyGesture(key, modifiers);
    }
}
