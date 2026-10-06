using System;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Automation;

namespace Adamantium.UI.Controls.Automation;

/// <summary>The peer of an <see cref="OverlayWindow"/>: a window inside the window, called by its title and closed as its
/// close button closes it, when it may be. It is neither minimized nor maximized.</summary>
public class OverlayWindowAutomationPeer : UIComponentAutomationPeer, IWindowProvider, ITransformProvider
{
    private readonly OverlayWindow _window;

    public OverlayWindowAutomationPeer(OverlayWindow owner) : base(owner)
    {
        _window = owner;
    }

    public override AutomationControlType ControlType => AutomationControlType.Window;

    public override string AutomationId => base.AutomationId is { Length: > 0 } id ? id : ClassName;

    public WindowState VisualState => WindowState.Normal;

    public bool CanMinimize => false;

    public bool CanMaximize => false;

    public void SetVisualState(WindowState state)
    {
        if (state != WindowState.Normal)
        {
            throw new InvalidOperationException($"'{Name}' is a window inside a window; it cannot be {state}.");
        }
    }

    public void Close()
    {
        if (!_window.CanClose)
        {
            throw new InvalidOperationException($"'{Name}' cannot be closed.");
        }

        _window.Close();
    }

    public bool CanMove => _window.AllowMove;

    public bool CanResize => _window.CanResize;

    public bool CanZoom => false;

    public double ZoomLevel => 100;

    public double ZoomMinimum => 100;

    public double ZoomMaximum => 100;

    /// <summary>Moves it within the window it is shown over, kept inside it, as a drag by its title bar does.</summary>
    public void Move(double x, double y)
    {
        if (!CanMove)
        {
            throw new InvalidOperationException($"'{Name}' does not move.");
        }

        var bounds = BoundingRectangle;
        var units = UnitsPerPixel();
        _window.MoveCard(new Mathematics.Vector2((x - bounds.X) * units.X, (y - bounds.Y) * units.Y));
    }

    public void Resize(double width, double height)
    {
        if (!CanResize)
        {
            throw new InvalidOperationException($"'{Name}' does not resize.");
        }

        var units = UnitsPerPixel();
        _window.ResizeCard(width * units.X, height * units.Y);
    }

    public void Zoom(double percent) => throw new InvalidOperationException("A window is not zoomed.");

    protected override string NameCore() => _window.Title as string;
}
