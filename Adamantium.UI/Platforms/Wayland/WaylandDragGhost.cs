using System;
using Adamantium.UI.Core.Input;
using Adamantium.Wayland;

namespace Adamantium.UI.Platforms.Wayland;

/// <summary>The ghost of a drag the application carries itself: a subsurface of the window the pointer is in. Mouse
/// drags go to the compositor instead (<see cref="WaylandDragDrop"/>).</summary>
internal sealed class WaylandDragGhost : IDragGhost
{
    private readonly WaylandPlatform platform;

    private WaylandWindowWorker parent;
    private WlSurface surface;
    private WlSubsurface subsurface;
    private WpViewport viewport;

    public WaylandDragGhost(IApplicationPlatform platform)
    {
        this.platform = (WaylandPlatform)platform;
    }

    public uint Dpi => (uint)Math.Round(96 * ScaleOf(platform.Seat.PointerWindow));

    public event Action<uint> DpiChanged
    {
        add { }
        remove { }
    }

    public void Show(byte[] premultipliedBgra, int width, int height, int screenX, int screenY)
    {
        platform.Invoke(() => ShowOnEventThread(premultipliedBgra, width, height, screenX, screenY));
    }

    public void Move(int screenX, int screenY)
    {
        platform.Invoke(() => MoveOnEventThread(screenX, screenY));
    }

    public void Hide()
    {
        platform.Invoke(Detach);
    }

    public void Dispose()
    {
        Hide();
    }

    private void ShowOnEventThread(byte[] bgra, int width, int height, int screenX, int screenY)
    {
        if (platform.Subcompositor == null || !AttachTo(platform.Seat.PointerWindow))
        {
            return;
        }

        var buffer = WaylandShm.CreateBuffer(platform, bgra, width, height);
        if (buffer == null)
        {
            return;
        }

        var scale = ScaleOf(parent);
        viewport?.SetDestination(Math.Max(1, (int)Math.Round(width / scale)), Math.Max(1, (int)Math.Round(height / scale)));
        surface.Attach(buffer, 0, 0);
        surface.DamageBuffer(0, 0, width, height);
        surface.Commit();
        WaylandShm.DestroyWhenReleased(buffer);

        MoveOnEventThread(screenX, screenY);
    }

    private void MoveOnEventThread(int screenX, int screenY)
    {
        if (subsurface == null || parent.Window == null)
        {
            return;
        }

        var scale = ScaleOf(parent);
        subsurface.SetPosition((int)Math.Round(screenX / scale), (int)Math.Round(screenY / scale));
        parent.Surface.Commit();
        platform.Display.Flush();
    }

    private double ScaleOf(WaylandWindowWorker window) => platform.Viewporter != null ? window?.Scale ?? 1 : 1;

    private bool AttachTo(WaylandWindowWorker window)
    {
        if (window?.Window == null)
        {
            Detach();
            return false;
        }

        if (window == parent)
        {
            return true;
        }

        Detach();
        parent = window;
        surface = platform.Compositor.CreateSurface();

        var region = platform.Compositor.CreateRegion();
        surface.SetInputRegion(region);
        region.Destroy();

        if (platform.Viewporter != null)
        {
            viewport = platform.Viewporter.GetViewport(surface);
        }

        subsurface = platform.Subcompositor.GetSubsurface(surface, window.Surface);
        subsurface.SetDesync();
        return true;
    }

    private void Detach()
    {
        if (surface == null)
        {
            return;
        }

        subsurface.Destroy();
        viewport?.Destroy();
        surface.Destroy();
        subsurface = null;
        viewport = null;
        surface = null;
        parent = null;
        platform.Display.Flush();
    }
}
