using Redmond.Shortcuts;

namespace Redmond.Notepad.Core;

/// <summary>
/// The modifier key that carries application shortcuts on macOS. Command is the
/// native convention; Control lets users coming from Windows or Linux keep the
/// muscle memory of Ctrl-based shortcuts. Other platforms ignore the preference.
/// </summary>
public enum MacOSShortcutModifier
{
    Command,
    Control,
}

public static class MacOSShortcutModifierMapping
{
    /// <summary>
    /// Returns <paramref name="definition"/> with every macOS gesture's Command
    /// modifier replaced by <paramref name="modifier"/>. Keys, the other
    /// modifiers, and the gestures of other platforms are left unchanged.
    /// </summary>
    public static ShortcutDefinition Apply(ShortcutDefinition definition, MacOSShortcutModifier modifier)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (modifier == MacOSShortcutModifier.Command)
        {
            return definition;
        }

        var platformGestures = definition.PlatformGestures.ToDictionary(
            pair => pair.Key,
            pair => pair.Key == ShortcutPlatform.MacOS ? Remap(pair.Value) : pair.Value);
        var platformAlternates = definition.PlatformAlternateGestures.ToDictionary(
            pair => pair.Key,
            pair => pair.Key == ShortcutPlatform.MacOS
                ? (IReadOnlyList<ShortcutGesture>)pair.Value.Select(Remap).ToArray()
                : pair.Value);

        return new ShortcutDefinition(
            definition.Id,
            definition.DefaultGesture,
            platformGestures,
            definition.Scope,
            definition.Priority,
            definition.AllowRepeat,
            definition.IsEnabled,
            definition.InputHandling,
            definition.AlternateGestures,
            platformAlternates,
            definition.DisabledPlatforms);
    }

    private static ShortcutGesture Remap(ShortcutGesture gesture) =>
        gesture.Modifiers.HasFlag(ShortcutModifiers.Command)
            ? new ShortcutGesture(
                gesture.Key,
                (gesture.Modifiers & ~ShortcutModifiers.Command) | ShortcutModifiers.Control)
            : gesture;
}
