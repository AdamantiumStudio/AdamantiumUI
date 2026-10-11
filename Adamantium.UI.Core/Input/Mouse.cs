using Adamantium.UI.Core.RoutedEvents;

namespace Adamantium.UI.Core.Input;

/// <summary>What only the mouse has: the cursor, its place on the desktop, and its raw movement. Pressing, moving and
/// hovering are the same for every pointer - see <see cref="PointerEvents"/>.</summary>
public static class Mouse
{
    /// <summary>How far the mouse itself moved, unbound by the screen's edges; raised on the focused element.</summary>
    public static readonly RoutedEvent RawMouseMoveEvent = EventManager.RegisterRoutedEvent("RawMouseMove",
        RoutingStrategy.Direct, typeof(RawMouseEventHandler), typeof(Mouse));

    private static Cursor _cursor = Cursor.Default;
    private static Cursor _overrideCursor;

    /// <summary>The mouse's pointer.</summary>
    public static Pointer Pointer => PointerDevice.Current.MousePointer;

    /// <summary>The platform that answers live pointer queries, registered at startup. Null falls back to the last
    /// position an input event carried.</summary>
    public static INativeMouse Platform { get; set; }

    /// <summary>Where the pointer is on the DESKTOP - see <see cref="PixelPoint"/>. It is not a logical point and cannot
    /// be compared with one: the desktop spans monitors that may not share a scale.</summary>
    public static PixelPoint ScreenCoordinates
    {
        get => Pointer.GetScreenPosition();
        set
        {
            if (Platform != null)
            {
                Platform.Position = value;
            }
        }
    }

    public static Cursor Cursor
    {
        get => _cursor;
        set
        {
            _cursor = value;
            Cursor.Platform?.Apply(_overrideCursor ?? value);
        }
    }

    /// <summary>An app-wide cursor that wins over per-element ones while set - how a drag shows its feedback. Null
    /// restores them.</summary>
    public static Cursor OverrideCursor
    {
        get => _overrideCursor;
        set
        {
            _overrideCursor = value;
            Cursor.Platform?.Apply(value ?? _cursor);
        }
    }
}
