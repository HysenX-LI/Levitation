using System.Text.Json.Serialization;

namespace Levitation.Core;

[Flags]
public enum ShortcutModifiers : uint
{
    Alt = 1,
    Control = 2,
    Shift = 4,
    Windows = 8
}

// Store Windows virtual keys so the binding does not depend on its display label or WPF enums.
public readonly record struct QuickCaptureShortcut(ShortcutModifiers Modifiers, int VirtualKey)
{
    public static QuickCaptureShortcut Default => new(ShortcutModifiers.Windows, 0x57);

    [JsonIgnore]
    public bool IsValid => Modifiers != 0 &&
        (Modifiers & ~(ShortcutModifiers.Alt | ShortcutModifiers.Control | ShortcutModifiers.Shift | ShortcutModifiers.Windows)) == 0 &&
        VirtualKey is >= 0x30 and <= 0x39 or >= 0x41 and <= 0x5A      // digits and letters
            or >= 0x60 and <= 0x87                                 // number pad and F1–F24
            or 0x20 or >= 0x21 and <= 0x28 or 0x2D or 0x2E          // Space, navigation, Insert, Delete
            or >= 0xBA and <= 0xC0 or >= 0xDB and <= 0xDF or 0xE2;  // punctuation
}
