using System;
using System.Threading;
using Adamantium.UI.Core;

namespace Adamantium.UI.Platforms.Linux;

/// <summary>Icons of a Linux panel's status area (StatusNotifierItem, as KDE Plasma, the GNOME AppIndicator extension,
/// Xfce and the wlroots bars show them): each on a session bus connection of its own, registered with the panel's watcher
/// - again whenever a watcher starts - and its menu served for the panel to show itself.</summary>
internal sealed class StatusNotifierArea : INotificationAreaPlatform
{
    private int _lastId;

    public INotificationAreaIcon AddIcon(string toolTip) =>
        StatusNotifierIcon.Start(toolTip, Interlocked.Increment(ref _lastId));
}
