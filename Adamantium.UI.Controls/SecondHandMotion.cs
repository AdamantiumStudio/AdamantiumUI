namespace Adamantium.UI.Controls;

/// <summary>How the second hand of a <see cref="Clock"/> moves.</summary>
public enum SecondHandMotion
{
    /// <summary>A step every second, as a quartz movement.</summary>
    Tick,

    /// <summary>Round without stopping, as a mechanical movement.</summary>
    Sweep
}
