namespace Adamantium.UI.Controls;

/// <summary>How much an <see cref="InfoBar"/>'s message matters. The theme gives each its own colour and icon, so it is
/// told apart without the colour too.</summary>
public enum InfoBarSeverity
{
    /// <summary>Something worth knowing: a file not made yet.</summary>
    Informational,

    /// <summary>Something done: a layer renamed.</summary>
    Success,

    /// <summary>Something to keep in mind: a change that waits for the next run.</summary>
    Warning,

    /// <summary>Something that went wrong: a file that cannot be read.</summary>
    Error
}
