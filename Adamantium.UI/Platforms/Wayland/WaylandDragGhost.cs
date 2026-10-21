using System;
using Adamantium.UI.Core.Input;
using Adamantium.Wayland;

namespace Adamantium.UI.Platforms.Wayland;

/// <summary>
/// The image that follows a drag the application carries itself - one a mouse does not make, or one made where the
/// compositor has no drag and drop (a mouse drag goes to the compositor, see <see cref="WaylandDragDrop"/>, and its icon
/// is the compositor's to move). It is a subsurface of the window the pointer is in: a Wayland client places no window of
/// its own on the desktop, but it places a subsurface anywhere relative to its parent, and the compositor does not clip
/// one to it. The drag's desktop point is a point of that window, so the ghost follows it past the window's edges.
/// <para>
/// What a subsurface cannot do is stand above other windows: one in front of the source window covers the ghost.
/// </para>
/// <para>
/// A subsurface's position is its parent's state, so a move commits the window's surface: an empty commit, besides the
/// frames the Vulkan presentation commits to it from the render thread. The requests go from the thread that dispatches
/// the surfaces' events, so none is destroyed under its own event.
/// </para>
/// </summary>
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

    // The drag stays in the window it started in until the button goes up: there is no other scale to cross onto.
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
        buffer.Destroy();

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

    // The window's scale, where a viewport shows a buffer drawn at it; 1 where none does.
    private double ScaleOf(WaylandWindowWorker window) => platform.Viewporter != null ? window?.Scale ?? 1 : 1;

    // The ghost's surface, a child of the window's: made again when the drag is in another window than the last one.
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

        // No input: the pointer goes through the ghost to the window under it.
        var region = platform.Compositor.CreateRegion();
        surface.SetInputRegion(region);
        region.Destroy();

        if (platform.Viewporter != null)
        {
            viewport = platform.Viewporter.GetViewport(surface);
        }

        subsurface = platform.Subcompositor.GetSubsurface(surface, window.Surface);

        // A new image shows when it is committed, not with the window's next frame.
        subsurface.SetDesync();
        return true;
    }

    // Takes the ghost off the screen at once: a destroyed subsurface needs no commit of its parent.
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
