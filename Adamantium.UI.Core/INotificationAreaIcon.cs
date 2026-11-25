namespace Adamantium.UI.Core;

/// <summary>An icon of the application's in the <see cref="NotificationArea"/>, showing the application's own icon, until
/// disposed. What is done to it is told on the application's loop thread.</summary>
public interface INotificationAreaIcon : IDisposable
{
    /// <summary>What the icon says when the pointer rests on it.</summary>
    string ToolTip { get; set; }

    /// <summary>Clicked with the left button, or picked from the keyboard.</summary>
    event EventHandler Clicked;

    /// <summary>Its menu asked for - a right click, or the menu key - at a point of the desktop - where the application
    /// opens the menu itself.</summary>
    event EventHandler<NotificationAreaMenuEventArgs> MenuAsked;

    /// <summary>Whether the desktop shows the icon now - not while the Windows task bar has not taken it, nor on a Linux
    /// session whose panel has not started yet, or that has none.</summary>
    bool IsDisplayed { get; }

    /// <summary>Raised on the application's loop thread when <see cref="IsDisplayed"/> changes - the panel started.
    /// </summary>
    event EventHandler IsDisplayedChanged;
}

/// <summary>Where on the desktop the menu of a <see cref="INotificationAreaIcon"/> was asked for, in physical pixels.
/// </summary>
public sealed class NotificationAreaMenuEventArgs(PixelPoint point) : EventArgs
{
    public PixelPoint Point { get; } = point;
}
