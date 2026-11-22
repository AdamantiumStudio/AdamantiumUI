namespace Adamantium.UI.Core;

/// <summary>An icon of the application's in the <see cref="NotificationArea"/>, showing the application's own icon, until
/// disposed. What is done to it is told on the application's loop thread.</summary>
public interface INotificationAreaIcon : IDisposable
{
    /// <summary>What the icon says when the pointer rests on it.</summary>
    string ToolTip { get; set; }

    /// <summary>Clicked with the left button, or picked from the keyboard.</summary>
    event EventHandler Clicked;

    /// <summary>Its menu asked for - a right click, or the menu key - at a point of the desktop.</summary>
    event EventHandler<NotificationAreaMenuEventArgs> MenuAsked;
}

/// <summary>Where on the desktop the menu of a <see cref="INotificationAreaIcon"/> was asked for, in physical pixels.
/// </summary>
public sealed class NotificationAreaMenuEventArgs(PixelPoint point) : EventArgs
{
    public PixelPoint Point { get; } = point;
}
