namespace Adamantium.UI.Core.Input;

/// <summary>What a gesture of fingers was recognized as. Each part counts once it passes its own threshold, and stays.
/// </summary>
[Flags]
public enum ManipulationKinds
{
    None = 0,

    /// <summary>The fingers moved together, past the drag threshold.</summary>
    Pan = 1,

    /// <summary>The fingers spread or closed, past <see cref="Gestures.PinchThreshold"/>.</summary>
    Pinch = 2,

    /// <summary>The fingers turned, past <see cref="Gestures.RotationThreshold"/>.</summary>
    Rotate = 4
}
