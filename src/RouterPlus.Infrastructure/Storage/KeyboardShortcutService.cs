using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace RouterPlus.Infrastructure.Storage;

/// <summary>
/// Parses and validates shortcut gesture strings such as "Ctrl+Alt+1".
/// Keeps parsing logic out of the UI layer and testable in isolation.
/// </summary>
public static partial class KeyboardShortcutService
{
    public const int MaxShortcutLength = 32;

    private static readonly HashSet<string> KnownModifiers = new(StringComparer.OrdinalIgnoreCase)
    {
        "Ctrl", "Control", "Alt", "Shift", "Win", "Meta"
    };

    public static bool TryParse(string? gesture, [NotNullWhen(true)] out ParsedShortcut? parsed)
    {
        parsed = null;
        if (string.IsNullOrWhiteSpace(gesture))
        {
            return false;
        }

        var gestureText = gesture.Trim();
        if (gestureText.Length > MaxShortcutLength)
        {
            return false;
        }

        var parts = gestureText
            .Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var modifiers = 0;
        var keyIndex = -1;
        for (var index = 0; index < parts.Length; index++)
        {
            var part = parts[index];
            if (KnownModifiers.Contains(part))
            {
                modifiers |= ToModifierFlag(part);
                continue;
            }

            if (keyIndex >= 0)
            {
                return false;
            }

            keyIndex = index;
        }

        if (keyIndex < 0 || parts[keyIndex].Length == 0)
        {
            return false;
        }

        var keyName = parts[keyIndex];
        if (keyName.Length > 1 && !IsFunctionKey(keyName) && !IsNumberKey(keyName))
        {
            return false;
        }

        parsed = new ParsedShortcut(modifiers, keyName);
        return true;
    }

    public static bool IsFunctionKey(string keyName) =>
        keyName.Length >= 2 && keyName[0] == 'F' && int.TryParse(keyName[1..], out var fn) && fn is >= 1 and <= 24;

    public static bool IsNumberKey(string keyName) =>
        keyName.Length == 2 && keyName[0] == 'D' && keyName[1] is >= '0' and <= '9';

    public static int ToModifierFlag(string modifier) => modifier.ToUpperInvariant() switch
    {
        "CTRL" or "CONTROL" => 1,
        "ALT" => 2,
        "SHIFT" => 4,
        "WIN" or "META" => 8,
        _ => 0
    };

    public static string ToDisplayValue(ParsedShortcut shortcut)
    {
        var builder = new StringBuilder();
        if ((shortcut.ModifierFlags & 1) != 0) builder.Append("Ctrl+");
        if ((shortcut.ModifierFlags & 2) != 0) builder.Append("Alt+");
        if ((shortcut.ModifierFlags & 4) != 0) builder.Append("Shift+");
        if ((shortcut.ModifierFlags & 8) != 0) builder.Append("Win+");
        builder.Append(shortcut.KeyName);
        return builder.ToString();
    }
}

/// <summary>Parsed representation of a shortcut gesture.</summary>
public sealed record ParsedShortcut(int ModifierFlags, string KeyName)
{
    public string DisplayValue => KeyboardShortcutService.ToDisplayValue(this);
}
