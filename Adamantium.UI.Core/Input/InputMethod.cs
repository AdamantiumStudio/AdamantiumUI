using Adamantium.Mathematics;

namespace Adamantium.UI.Core.Input;

/// <summary>Where an input method editor (IME) shows the text being composed and its candidates.</summary>
public static class InputMethod
{
    /// <summary>Puts the input method's windows at the caret, <paramref name="bounds"/> in <paramref name="element"/>'s
    /// own coordinates. A text control calls it while it has the keyboard focus, whenever its caret moves.</summary>
    public static void SetCaretBounds(IUIComponent element, Rect bounds)
    {
        if (element?.RootVisual is not IWindow window)
        {
            return;
        }

        window.InputMethodCaret = new Rect(element.TranslatePoint(new Vector2(bounds.X, bounds.Y), window),
            element.TranslatePoint(new Vector2(bounds.Right, bounds.Bottom), window));
    }
}
