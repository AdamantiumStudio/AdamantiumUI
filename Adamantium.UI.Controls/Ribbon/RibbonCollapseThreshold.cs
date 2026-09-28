namespace Adamantium.UI.Controls;

/// <summary>Which of its group's size steps (Large, Medium, Small) a command follows, so it can sit out a step it would be
/// unreadable at.</summary>
public enum RibbonCollapseThreshold
{
    /// <summary>Follow the group at this step - the ordinary case.</summary>
    WhenGroupIsMedium,

    /// <summary>Hold out until the group's last step.</summary>
    WhenGroupIsSmall,

    /// <summary>Never take this size, however narrow the tab gets.</summary>
    Never
}
