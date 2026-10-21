using System;
using Adamantium.UI.Core.Input;

namespace Adamantium.UI.Platforms.Wayland;

/// <summary>
/// No ghost yet. A Wayland client cannot place a window of its own at a desktop point, nor keep one on top: the image
/// that follows a drag there is the drag's icon, a surface the compositor moves with the pointer (wl_data_device
/// .start_drag), and it comes with drag and drop between applications. Until then a drag shows its drop feedback only.
/// </summary>
internal sealed class WaylandDragGhost : IDragGhost
{
    public uint Dpi => 96;

    public event Action<uint> DpiChanged
    {
        add { }
        remove { }
    }

    public void Show(byte[] premultipliedBgra, int width, int height, int screenX, int screenY)
    {
    }

    public void Move(int screenX, int screenY)
    {
    }

    public void Hide()
    {
    }

    public void Dispose()
    {
    }
}
