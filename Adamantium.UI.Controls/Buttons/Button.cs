using Adamantium.UI.Controls.Primitives;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Input;

namespace Adamantium.UI.Controls.Buttons;

/// <summary>A standard clickable button. Click/command/press behavior lives in <see cref="ButtonBase"/>.</summary>
public class Button : ButtonBase
{
    public static readonly AdamantiumProperty IsDefaultProperty = AdamantiumProperty.Register(nameof(IsDefault),
        typeof(bool), typeof(Button), new PropertyMetadata(false));

    public static readonly AdamantiumProperty IsCancelProperty = AdamantiumProperty.Register(nameof(IsCancel),
        typeof(bool), typeof(Button), new PropertyMetadata(false));

    /// <summary>When true, Enter (unhandled by the focused element) activates this button - the window's default button.</summary>
    public bool IsDefault
    {
        get => GetValue<bool>(IsDefaultProperty);
        set => SetValue(IsDefaultProperty, value);
    }

    /// <summary>When true, Escape activates this button - the window's cancel button.</summary>
    public bool IsCancel
    {
        get => GetValue<bool>(IsCancelProperty);
        set => SetValue(IsCancelProperty, value);
    }

    /// <summary>The button a key nobody took answers in <paramref name="root"/>'s subtree, shown and enabled, the root
    /// included: Enter its <see cref="IsDefault"/> one, Escape its <see cref="IsCancel"/> one, each pressed alone and not
    /// repeated; null for any other key - what a window, an overlay window and a popup's content all do with them.
    /// </summary>
    internal static Button AnsweringKey(IUIComponent root, Key key, InputModifiers modifiers, bool isRepeated)
    {
        const InputModifiers chords = InputModifiers.LeftControl | InputModifiers.RightControl | InputModifiers.LeftShift |
                                      InputModifiers.RightShift | InputModifiers.LeftAlt | InputModifiers.RightAlt;
        if (isRepeated || (modifiers & chords) != 0)
        {
            return null;
        }

        return key switch
        {
            Key.Enter => FindIn(root, button => button.IsDefault),
            Key.Escape => FindIn(root, button => button.IsCancel),
            _ => null
        };
    }

    private static Button FindIn(IUIComponent node, Func<Button, bool> match)
    {
        if (node.Visibility != Visibility.Visible || !node.IsEnabled) return null;
        if (node is Button button && match(button)) return button;
        foreach (var child in node.VisualChildren)
        {
            if (FindIn(child, match) is { } found) return found;
        }

        return null;
    }
}
