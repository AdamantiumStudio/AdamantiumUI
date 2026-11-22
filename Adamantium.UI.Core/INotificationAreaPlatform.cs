namespace Adamantium.UI.Core;

/// <summary>A platform's way to <see cref="NotificationArea"/>.</summary>
public interface INotificationAreaPlatform
{
    /// <summary>Puts an icon of the application's own in the notification area, with <paramref name="toolTip"/>.</summary>
    INotificationAreaIcon AddIcon(string toolTip);
}
