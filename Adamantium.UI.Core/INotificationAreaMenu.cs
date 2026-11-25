namespace Adamantium.UI.Core;

/// <summary>The menu of a <see cref="INotificationAreaIcon"/>, for a desktop that shows the menu itself - a Linux panel.
/// </summary>
public interface INotificationAreaMenu
{
    /// <summary>Whether the icon offers a menu now - a panel shows none for an icon without; read on any thread.
    /// </summary>
    bool IsOffered { get; }

    /// <summary>The rows as they stand now; read on the application's loop thread each time the menu is about to show.
    /// </summary>
    IReadOnlyList<NotificationAreaMenuItem> ReadRows();
}
