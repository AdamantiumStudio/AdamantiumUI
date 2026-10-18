namespace Adamantium.UI.Core.Input;

/// <summary>A key with the modifiers held down, written as "Ctrl+Shift+K": how a <see cref="RoutedCommand"/> is asked
/// for from the keyboard, and what a menu row shows beside it.</summary>
public readonly record struct KeyGesture(Key Key, ModifierKeys Modifiers = ModifierKeys.None)
{
    private static readonly (ModifierKeys Key, string Name)[] Names =
    [
        (ModifierKeys.Control, "Ctrl"), (ModifierKeys.Alt, "Alt"), (ModifierKeys.Shift, "Shift"),
        (ModifierKeys.Meta, "Meta")
    ];

    /// <summary>Reads "Ctrl+Shift+K": modifiers Ctrl (or Control), Alt, Shift and Meta (or Win, Cmd), then the key by
    /// the name of <see cref="Input.Key"/>, a digit standing for itself.</summary>
    public static KeyGesture Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var parts = text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0 || text.TrimEnd().EndsWith('+'))
        {
            throw new FormatException($"'{text}' names no key.");
        }

        var modifiers = ModifierKeys.None;
        foreach (var part in parts[..^1])
        {
            modifiers |= part.ToLowerInvariant() switch
            {
                "ctrl" or "control" => ModifierKeys.Control,
                "alt" => ModifierKeys.Alt,
                "shift" => ModifierKeys.Shift,
                "meta" or "win" or "cmd" => ModifierKeys.Meta,
                _ => throw new FormatException($"'{part}' in '{text}' is not Ctrl, Alt, Shift or Meta.")
            };
        }

        var name = parts[^1];
        if (name.Length == 1 && char.IsAsciiDigit(name[0]))
        {
            name = "D" + name;
        }

        if (!Enum.TryParse<Key>(name, true, out var key) || char.IsAsciiDigit(name[0]))
        {
            throw new FormatException($"'{parts[^1]}' in '{text}' is not a key.");
        }

        return new KeyGesture(key, modifiers);
    }

    /// <summary>The modifiers of a key event, the left and the right key alike.</summary>
    public static ModifierKeys Of(InputModifiers modifiers)
    {
        var keys = ModifierKeys.None;
        if ((modifiers & (InputModifiers.LeftControl | InputModifiers.RightControl)) != 0)
        {
            keys |= ModifierKeys.Control;
        }

        if ((modifiers & (InputModifiers.LeftAlt | InputModifiers.RightAlt)) != 0)
        {
            keys |= ModifierKeys.Alt;
        }

        if ((modifiers & (InputModifiers.LeftShift | InputModifiers.RightShift)) != 0)
        {
            keys |= ModifierKeys.Shift;
        }

        if ((modifiers & (InputModifiers.LeftWindows | InputModifiers.RightWindows)) != 0)
        {
            keys |= ModifierKeys.Meta;
        }

        return keys;
    }

    public bool Matches(Key key, InputModifiers modifiers) => key == Key && Of(modifiers) == Modifiers;

    public override string ToString()
    {
        var text = new System.Text.StringBuilder();
        foreach (var (modifier, name) in Names)
        {
            if ((Modifiers & modifier) != 0)
            {
                text.Append(name).Append('+');
            }
        }

        var key = Key.ToString();
        return text.Append(key.Length == 2 && key[0] == 'D' && char.IsAsciiDigit(key[1]) ? key[1..] : key).ToString();
    }
}
