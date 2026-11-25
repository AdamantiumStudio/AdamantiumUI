namespace Adamantium.UI.Platforms.MacOS;

/// <summary>A motor of a gamepad.</summary>
internal interface IHapticMotor
{
    /// <summary>Whether the system stopped it since it was run.</summary>
    bool WasStopped { get; }

    /// <summary>Runs it, 0 to 1 - at 0 it stands still, ready to run again at once - starting it first where it does not
    /// run; false when it cannot be.</summary>
    bool Run(float strength);

    /// <summary>Lets go of it, stopping it; it can be run again.</summary>
    void LetGo();
}
