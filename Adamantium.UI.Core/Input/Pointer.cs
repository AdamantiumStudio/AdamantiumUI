using Adamantium.Mathematics;

namespace Adamantium.UI.Core.Input;

/// <summary>One pointer: the mouse, a finger, or a pen. Each has its own position, capture and element under it, so two
/// fingers drag two things at once.</summary>
public sealed class Pointer
{
    internal PixelPoint ScreenPosition;
    internal IInputComponent PositionRoot;
    internal IInputComponent HoverRoot;
    internal int ClickCount;

    // The button a finger or a pen pressed when it touched, kept until it lifts: its moves and its lift say the same
    // button, whatever changes on the way.
    internal MouseButtons ContactButton;
    internal uint LastClickTime;
    internal IInputComponent LastClicked;
    internal Vector2 LastClickPosition;

    internal Pointer(uint id, PointerType type, bool isPrimary)
    {
        Id = id;
        Type = type;
        IsPrimary = isPrimary;
        Properties = new PointerPointProperties { Pressure = PointerPointProperties.DefaultPressure };
    }

    /// <summary>The OS's id for the pointer, stable while it is in use. The mouse is always 1.</summary>
    public uint Id { get; }

    public PointerType Type { get; }

    /// <summary>The mouse, or the first finger or pen down: the one that clicks and that the OS moves the cursor with.</summary>
    public bool IsPrimary { get; internal set; }

    /// <summary>Whether a finger or a pen is held still past <see cref="Gestures.HoldingTime"/>: a lift now completes a
    /// long press rather than a tap.</summary>
    public bool IsHolding { get; internal set; }

    /// <summary>Whether this finger belongs to a gesture (<see cref="Gestures.ManipulationEvent"/>): its moves and its
    /// lift go to no element.</summary>
    public bool IsManipulating { get; internal set; }

    /// <summary>Whether this finger rests on an element that takes gestures together with another finger: a gesture may
    /// be about to start, so what follows one finger alone - a scroll, a drag - holds still meanwhile.</summary>
    public bool IsInGesture { get; internal set; }

    /// <summary>The element this pointer's events go to whatever is under it, or null.</summary>
    public IInputComponent Captured { get; private set; }

    /// <summary>The element directly under the pointer, or null.</summary>
    public IInputComponent DirectlyOver { get; internal set; }

    /// <summary>What the pointer last reported: buttons, pressure, tilt.</summary>
    public PointerPointProperties Properties { get; internal set; }

    internal void ForgetReleased()
    {
        if (IsReleased(PositionRoot))
        {
            PositionRoot = null;
        }

        if (IsReleased(HoverRoot))
        {
            HoverRoot = null;
        }

        if (IsReleased(LastClicked))
        {
            LastClicked = null;
        }

        if (IsReleased(DirectlyOver))
        {
            DirectlyOver = null;
        }

        if (IsReleased(Captured))
        {
            Captured = null;
        }
    }

    private static bool IsReleased(IInputComponent component) =>
        component is FundamentalUIComponent { ReleasedInBatch: > 0 };

    /// <summary>Gives this pointer's events to <paramref name="component"/> whatever is under it; null releases it. The
    /// element that had it is told it lost it.</summary>
    public bool Capture(IInputComponent component)
    {
        var previous = Captured;
        if (ReferenceEquals(previous, component))
        {
            return component != null;
        }

        Captured = component;
        previous?.RaiseEvent(new PointerEventArgs(this, InputModifiers.None, 0) { RoutedEvent = PointerEvents.PointerCaptureLostEvent });
        component?.RaiseEvent(new PointerEventArgs(this, InputModifiers.None, 0) { RoutedEvent = PointerEvents.GotPointerCaptureEvent });
        return component != null;
    }

    /// <summary>Where the pointer is, in <paramref name="relativeTo"/>'s own units.</summary>
    public Vector2 GetPosition(IInputComponent relativeTo)
    {
        IUIComponent root = relativeTo;
        while (root.VisualParent != null)
        {
            root = root.VisualParent;
        }

        var clientRoot = root is IWindow ? root : ((PositionRoot as IUIComponent) ?? root);
        return ToLocal(relativeTo, clientRoot.PointToClient(ScreenPosition));
    }

    internal static Vector2 ToLocal(IUIComponent element, Vector2 client)
    {
        if (Transformed(element))
        {
            var local = Vector3F.TransformCoordinate(new Vector3F((float)client.X, (float)client.Y, 0),
                Matrix4x4F.Invert(element.WorldTransform));

            return new Vector2(local.X, local.Y);
        }

        for (var v = element; v != null; v = v.VisualParent)
        {
            client -= v.Bounds.Location;
        }

        return client;
    }

    /// <summary>The pointer in <paramref name="relativeTo"/>'s units, with what it reports.</summary>
    public PointerPoint GetCurrentPoint(IInputComponent relativeTo) => new(this, GetPosition(relativeTo), Properties);

    /// <summary>Where the pointer is on the desktop.</summary>
    public PixelPoint GetScreenPosition() =>
        Type == PointerType.Mouse && Mouse.Platform is { } platform ? platform.Position : ScreenPosition;

    private static bool Transformed(IUIComponent of)
    {
        for (var at = of; at != null; at = at.VisualParent)
        {
            // A layout transform scales what is drawn inside the footprint as much as a render transform moves it.
            if (at.RenderTransform != null || at.LayoutTransform != null)
            {
                return true;
            }
        }

        return false;
    }
}
