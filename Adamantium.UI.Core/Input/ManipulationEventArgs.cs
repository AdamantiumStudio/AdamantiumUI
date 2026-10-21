using Adamantium.Mathematics;

namespace Adamantium.UI.Core.Input;

/// <summary>A step of a gesture of fingers on an element, in that element's units. Only what <see cref="Recognized"/>
/// names moves: a pan alone leaves the scale at 1 and the rotation at 0.</summary>
public class ManipulationEventArgs : InputEventArgs
{
    public ManipulationEventArgs(ManipulationState state, InputModifiers modifiers, uint timestamp)
        : base(modifiers, timestamp)
    {
        State = state;
    }

    public ManipulationState State { get; }

    /// <summary>What the gesture is: a pan, a pinch, a rotation, or any of them together.</summary>
    public ManipulationKinds Recognized { get; init; }

    /// <summary>How many fingers it has now.</summary>
    public int Contacts { get; init; }

    /// <summary>The middle of the fingers, the point a pinch scales and a rotation turns about.</summary>
    public Vector2 Origin { get; init; }

    /// <summary>How far the fingers moved since the last step.</summary>
    public Vector2 Translation { get; init; }

    /// <summary>How much the fingers spread since the last step: above 1 apart, below 1 together.</summary>
    public double Scale { get; init; } = 1;

    /// <summary>How far the fingers turned since the last step, in degrees, clockwise on screen.</summary>
    public double Rotation { get; init; }

    public Vector2 TotalTranslation { get; init; }

    public double TotalScale { get; init; } = 1;

    public double TotalRotation { get; init; }
}

public delegate void ManipulationEventHandler(object sender, ManipulationEventArgs e);
