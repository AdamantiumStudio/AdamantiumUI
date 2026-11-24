### Added

- `TrayIcon` on Linux: the icon is a StatusNotifierItem, the protocol KDE Plasma, the GNOME AppIndicator extension,
  Xfce and the wlroots bars show status icons by. It is served on a session bus name of its own with the application's
  own icon and its tool tip, and told to the panel's watcher - the moment one starts, if none runs yet, and again
  when the panel restarts. A click on it runs its command. Its `ContextMenu` is given to the panel as a
  com.canonical.dbusmenu and drawn by the panel itself: its rows as they stand each time it opens, separators,
  submenus, checks and rows turned off, a row picked running as a click on it would. A menu read changed is said
  (LayoutUpdated), a click names the row of the layout the panel was given, and an underscore stays an underscore. An
  icon without a `ContextMenu` offers no menu. Where another process holds the icon's bus name - a sandboxed one with
  the same process id - the watcher is told the connection's own name. A desktop with no such panel shows no icon, as
  before.
- `INotificationAreaIcon.Menu` and `NotificationAreaMenuItem`: the menu of an icon for a desktop that shows the menu
  itself, read on the loop thread each time it is about to show. Where the application opens the menu, as on Windows,
  `MenuAsked` is raised as before and the menu is not read.
