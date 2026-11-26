using Adamantium.UI.Core;

namespace Adamantium.UI.Platforms.MacOS;

/// <summary>Applications' icons on the right of the menu bar.</summary>
internal sealed class MacOSMenuBar(MacOSPlatform platform) : INotificationAreaPlatform
{
    public bool IsAvailable => true;

    public INotificationAreaIcon AddIcon(string toolTip, INotificationAreaMenu menu) =>
        new MacOSMenuBarIcon(platform, toolTip, menu);
}
