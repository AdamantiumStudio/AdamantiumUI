using System;

namespace Adamantium.UI.Platforms.Linux;

/// <summary>Names a window to the desktop portals, so a dialog a portal opens stays over it. The display server's to say:
/// the name is a handle of its protocol.</summary>
internal interface IPortalParent
{
    /// <summary>The portal's name of the window with <paramref name="owner"/> as its handle, or of the active window for
    /// none; empty when it has no name to give.</summary>
    string PortalParentOf(IntPtr owner);
}
