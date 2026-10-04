namespace Adamantium.UI.Controls;

/// <summary>Where a window appears when it has no remembered place (<see cref="WindowBase.StartupLocation"/>).</summary>
public enum WindowStartupLocation
{
    /// <summary>At <see cref="WindowBase.Left"/> and <see cref="WindowBase.Top"/>.</summary>
    Manual,

    /// <summary>In the middle of the work area of the screen the pointer is on.</summary>
    CenterScreen,

    /// <summary>In the middle of the window that was active when this one opened; as <see cref="CenterScreen"/> when
    /// there is none.</summary>
    CenterOwner
}
