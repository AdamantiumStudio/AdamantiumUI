namespace Adamantium.UI.Platforms.MacOS;

/// <summary>A motor of a gamepad.</summary>
internal interface IHapticMotor
{
    /// <summary>Runs it, 0 to 1, starting it first where it does not run; false when it cannot be.</summary>
    bool Run(float strength);

    /// <summary>Stops it and lets go of it; it can be run again.</summary>
    void Stop();
}
