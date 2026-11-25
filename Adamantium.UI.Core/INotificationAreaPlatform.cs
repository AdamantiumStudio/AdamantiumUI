namespace Adamantium.UI.Core;

/// <summary>A platform's way to <see cref="NotificationArea"/>.</summary>
public interface INotificationAreaPlatform
{
    /// <summary>Puts an icon of the application's own in the notification area, with <paramref name="toolTip"/> and
    /// <paramref name="menu"/>, null for none. A desktop that shows the menu itself reads it; where the application opens
    /// its menu, <see cref="INotificationAreaIcon.MenuAsked"/> is raised instead and it is never read.</summary>
    INotificationAreaIcon AddIcon(string toolTip, INotificationAreaMenu menu);

    /// <summary>Whether the desktop shows the icons now - asked of it each time: a Linux session may run no panel that
    /// does, or start one later; Windows may run another shell than Explorer, with no task bar.</summary>
    bool IsAvailable { get; }
}
