namespace Adamantium.UI.Core.Input.Raw;

/// <summary>What a platform reports about a pointer. A double click is not among them: it is counted from two presses
/// once, in <see cref="PointerDevice"/>, so every platform counts it the same.</summary>
public enum RawPointerEventType
{
    Move,
    LeaveWindow,
    Down,
    Up,
    Wheel,

    /// <summary>The OS took the pointer away mid-press: a system gesture claimed the finger.</summary>
    Cancel
}
