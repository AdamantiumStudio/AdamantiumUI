namespace Adamantium.UI.Core.Input.Raw;

public class RawTextCompositionEventArgs : RawInputEventArgs
{
    public RawTextCompositionEventArgs(RawTextCompositionEventType eventType, string text, int cursorPosition,
        InputModifiers modifiers, uint timestamp) : base(modifiers, timestamp)
    {
        EventType = eventType;
        Text = text;
        CursorPosition = cursorPosition;
    }

    public RawTextCompositionEventType EventType { get; }

    public string Text { get; }

    public int CursorPosition { get; }
}
