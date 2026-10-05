using System;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Input;

namespace Adamantium.UI.Automation;

/// <summary>Where the pointer is, for the length of a simulated gesture: the point of the event being made, not the
/// system cursor's, which a gesture made inside the application never moves - and must not move. Code that asks where
/// the pointer is on the desktop, such as a drag's threshold and its drop target, follows the gesture.</summary>
internal sealed class SimulatedPointer : INativeMouse, IDisposable
{
    private readonly INativeMouse _system;

    private SimulatedPointer(INativeMouse system)
    {
        _system = system;
    }

    public PixelPoint Position { get; set; }

    /// <summary>Stands in for the system pointer until disposed.</summary>
    public static SimulatedPointer Install()
    {
        var pointer = new SimulatedPointer(Mouse.Platform);
        Mouse.Platform = pointer;
        return pointer;
    }

    public void Dispose() => Mouse.Platform = _system;
}
