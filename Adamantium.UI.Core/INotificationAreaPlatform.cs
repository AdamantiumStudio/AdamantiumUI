namespace Adamantium.UI.Core;

/// <summary>A platform's way to <see cref="NotificationArea"/>.</summary>
public interface INotificationAreaPlatform
{
    /// <summary>Puts an icon of the application's own in the notification area, with <paramref name="toolTip"/>.</summary>
    INotificationAreaIcon AddIcon(string toolTip);

    /// <summary>Whether the desktop shows the icons now - asked of it each time: a Linux session may run no panel that
    /// does, or start one later.</summary>
    bool IsAvailable => true;
}
