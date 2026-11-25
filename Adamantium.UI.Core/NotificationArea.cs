namespace Adamantium.UI.Core;

/// <summary>The place of the desktop where applications keep small icons of their own - the notification area of the
/// Windows task bar, the status icons of a Linux panel.</summary>
public static class NotificationArea
{
    /// <summary>The platform that shows the icons, registered once at startup; null where none is written.</summary>
    public static INotificationAreaPlatform Platform { get; set; }

    /// <summary>Whether this desktop shows icons of applications now - asked of the desktop each time, so it is not for a
    /// frame's work: on Linux it takes a call to the session bus. An application that lives in the notification area
    /// shows its window where this is false - GNOME without the AppIndicator extension has no such place.</summary>
    public static bool IsAvailable => Platform?.IsAvailable == true;

    /// <summary>Puts an icon of the application's own there, with <paramref name="toolTip"/> and
    /// <paramref name="menu"/>, null for none; null where there is no such place. Disposing it takes it away.</summary>
    public static INotificationAreaIcon AddIcon(string toolTip, INotificationAreaMenu menu) =>
        Platform?.AddIcon(toolTip, menu);
}
