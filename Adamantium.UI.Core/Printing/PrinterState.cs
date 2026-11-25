namespace Adamantium.UI.Core.Printing;

/// <summary>What a printer is doing, as the system says.</summary>
public enum PrinterState
{
    /// <summary>Waiting for work.</summary>
    Ready,

    /// <summary>Printing a job; more wait their turn.</summary>
    Printing,

    /// <summary>Not reachable - switched off or off the network.</summary>
    Offline,

    /// <summary>Out of paper: a job waits in its queue until there is some.</summary>
    OutOfPaper,

    /// <summary>Paper caught inside it.</summary>
    PaperJam,

    /// <summary>Held by the user or the system.</summary>
    Paused,

    /// <summary>Something else stops it.</summary>
    Error,
}
