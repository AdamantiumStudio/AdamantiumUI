using Adamantium.UI.Core.RoutedEvents;

namespace Adamantium.UI.Core.Input;

/// <summary>Text an input method is composing: shown where it will go, but not yet part of the text.</summary>
public class TextCompositionEventArgs : RoutedEventArgs
{
    public TextCompositionEventArgs(string text, int cursorPosition)
    {
        Text = text;
        CursorPosition = cursorPosition;
    }

    /// <summary>The text being composed; empty when the composition ends.</summary>
    public string Text { get; }

    /// <summary>Where the composition's own cursor stands in <see cref="Text"/>.</summary>
    public int CursorPosition { get; }
}
