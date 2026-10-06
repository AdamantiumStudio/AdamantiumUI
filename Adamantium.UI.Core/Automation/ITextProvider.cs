namespace Adamantium.UI.Core.Automation;

/// <summary>An element holding text a reader moves through: its characters, the lines they are laid out on, the
/// selection, and where a piece of it is on screen. Indexes are positions between characters, 0 to the text's length.</summary>
public interface ITextProvider
{
    string Text { get; }

    int SelectionStart { get; }

    int SelectionLength { get; }

    /// <summary>Selects <paramref name="length"/> characters from <paramref name="start"/>; a length of 0 puts the caret there.</summary>
    void Select(int start, int length);

    /// <summary>The laid-out line the character at <paramref name="index"/> is on, from 0.</summary>
    int LineOf(int index);

    /// <summary>The first index on <paramref name="line"/> and the index just past its last character.</summary>
    (int Start, int End) LineRange(int line);

    /// <summary>The screen rectangles, in pixels, of the characters from <paramref name="start"/> to
    /// <paramref name="end"/> - one per line they cover.</summary>
    IReadOnlyList<Rect> Bounds(int start, int end);

    /// <summary>The index nearest a point on screen.</summary>
    int IndexAt(PixelPoint screen);

    /// <summary>Scrolls what holds the text until <paramref name="index"/> is in view.</summary>
    void ScrollIntoView(int index);
}
