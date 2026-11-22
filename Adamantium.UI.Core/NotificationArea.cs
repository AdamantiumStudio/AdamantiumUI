namespace Adamantium.UI.Core;

/// <summary>The place of the desktop where applications keep small icons of their own - the notification area of the
/// Windows task bar, the status icons of a Linux panel.</summary>
public static class NotificationArea
{
    /// <summary>The platform that shows the icons, registered once at startup; null where none is written.</summary>
    public static INotificationAreaPlatform Platform { get; set; }

    /// <summary>Whether this desktop shows icons of applications at all.</summary>
    public static bool IsAvailable => Platform != null;

    /// <summary>Puts an icon of the application's own there, with <paramref name="toolTip"/>; null where there is no
    /// such place. Disposing it takes it away.</summary>
    public static INotificationAreaIcon AddIcon(string toolTip) => Platform?.AddIcon(toolTip);
}
