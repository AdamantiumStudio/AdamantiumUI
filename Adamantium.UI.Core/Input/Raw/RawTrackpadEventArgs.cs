using Adamantium.Mathematics;

namespace Adamantium.UI.Core.Input.Raw;

/// <summary>A step of a gesture of fingers on a trackpad, as a platform reports it: one kind at a time, though two may go
/// on together. The pointer stands still under it.</summary>
public class RawTrackpadEventArgs : RawInputEventArgs
{
    public RawTrackpadEventArgs(RawTrackpadEventType type, ManipulationKinds kind, IInputComponent rootComponent,
        Vector2<double> position, InputModifiers modifiers, uint timestamp)
        : base(modifiers, timestamp)
    {
        Type = type;
        Kind = kind;
        RootComponent = rootComponent;
        Position = position;
    }

    public RawTrackpadEventType Type { get; }

    /// <summary>A pan, a pinch or a rotation.</summary>
    public ManipulationKinds Kind { get; }

    public IInputComponent RootComponent { get; }

    /// <summary>The pointer, in the root's units.</summary>
    public Vector2<double> Position { get; }

    /// <summary>How far the fingers moved since the last step, in the root's units.</summary>
    public Vector2<double> Translation { get; init; }

    /// <summary>How much they spread since the last step: above 1 apart, below 1 together.</summary>
    public double Scale { get; init; } = 1;

    /// <summary>How far they turned since the last step, in degrees, clockwise on screen.</summary>
    public double Rotation { get; init; }
}
