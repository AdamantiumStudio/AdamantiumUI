using Adamantium.Graphics;
using Adamantium.Graphics.Core;
using Adamantium.Imaging;
using Adamantium.Mathematics;
using Adamantium.UI.WebView.Cef.Interop;

namespace Adamantium.UI.WebView;

/// <summary>
/// Hands a page's frames - BGRA pixels in memory - to the panel that shows them, as a producer on the panel's own
/// device does: a ring of two surfaces, each frame written into the one the panel gave back. CEF always hands the whole
/// frame, so a surface gets the rectangles that changed since it was last written, read from the frame at hand. Runs on
/// the thread CEF paints on, the UI loop, with the engine's device - not the panel's.
/// </summary>
internal sealed unsafe class WebViewFrameProducer : IDisposable
{
    private const int RingSize = 2;

    private readonly IGraphicsDevice device;
    private readonly Action<SharedSurfaceDescriptor> show;
    private readonly Action frameWritten;
    private readonly ulong[] written = new ulong[RingSize];
    private readonly Rectangle<int>?[] changed = new Rectangle<int>?[RingSize];
    private SharedSurface ring;
    private ulong frame;
    private byte[] scratch = [];

    public WebViewFrameProducer(IGraphicsDevice device, Action<SharedSurfaceDescriptor> show, Action frameWritten)
    {
        this.device = device;
        this.show = show;
        this.frameWritten = frameWritten;
    }

    /// <summary>Writes a frame of <paramref name="width"/> × <paramref name="height"/> pixels whose
    /// <paramref name="dirty"/> rectangles changed. False when no surface was free for it: the frame is not shown, and
    /// the page is to be asked for it again.</summary>
    public bool Write(ReadOnlySpan<CefRect> dirty, byte* pixels, int width, int height)
    {
        if (width <= 0 || height <= 0)
        {
            return true;
        }

        if (ring == null || ring.Width != width || ring.Height != height)
        {
            StartRing(pixels, width, height);
            return true;
        }

        foreach (var rect in dirty)
        {
            var area = new Rectangle<int>(rect.x, rect.y, rect.width, rect.height);
            for (var i = 0; i < RingSize; i++)
            {
                changed[i] = changed[i] is { } before ? Rectangle<int>.Union(before, area) : area;
            }
        }

        var slot = FreeSurface();
        if (slot < 0)
        {
            return false;
        }

        if (changed[slot] is { } region && Clip(region, width, height) is { Width: > 0, Height: > 0 } inFrame)
        {
            WriteRegion(ring.SurfaceAt(slot), slot, pixels, width, inFrame);
        }

        return true;
    }

    /// <summary>Whether the panel has given back a surface to write the next frame into.</summary>
    public bool HasFreeSurface => ring == null || FreeSurface() >= 0;

    /// <summary>Retires the ring; the panel is to have let go of it first.</summary>
    public void Dispose()
    {
        Retire(ring);
        ring = null;
    }

    private void StartRing(byte* pixels, int width, int height)
    {
        var previous = ring;
        ring = SharedSurface.CreateInDevice(device, (uint)width, (uint)height, SurfaceFormat.B8G8R8A8.UNorm,
            bufferCount: RingSize);
        Array.Clear(written);
        var whole = new Rectangle<int>(0, 0, width, height);
        changed[0] = whole;
        changed[1] = whole;
        WriteRegion(ring.SurfaceAt(0), 0, pixels, width, whole);
        show(ring.Descriptor);
        Retire(previous);
    }

    private void Retire(SharedSurface surface)
    {
        if (surface != null)
        {
            device.AddToDeferDisposeQueue(surface);
        }
    }

    private void WriteRegion(SharedSurface surface, int slot, byte* pixels, int width, Rectangle<int> region)
    {
        var size = region.Width * region.Height * 4;
        if (scratch.Length < size)
        {
            scratch = new byte[size];
        }

        var rowBytes = region.Width * 4;
        for (var row = 0; row < region.Height; row++)
        {
            new ReadOnlySpan<byte>(pixels + ((long)(region.Y + row) * width + region.X) * 4, rowBytes)
                .CopyTo(scratch.AsSpan(row * rowBytes, rowBytes));
        }

        frame++;
        surface.WriteRegions(device,
            [new TextureRegionUpdate(0, 0, region.X, region.Y, (uint)region.Width, (uint)region.Height,
                scratch.AsMemory(0, size))],
            frame);
        written[slot] = frame;
        changed[slot] = null;
        ring.Descriptor.NoteSubmitted(frame);
        frameWritten();
    }

    private int FreeSurface()
    {
        var oldest = written[0] <= written[1] ? 0 : 1;
        return ring.SurfaceAt(oldest).ConsumeValue >= written[oldest] ? oldest : -1;
    }

    private static Rectangle<int> Clip(Rectangle<int> region, int width, int height)
    {
        var x = Math.Clamp(region.X, 0, width);
        var y = Math.Clamp(region.Y, 0, height);
        var right = Math.Clamp(region.X + region.Width, x, width);
        var bottom = Math.Clamp(region.Y + region.Height, y, height);
        return new Rectangle<int>(x, y, right - x, bottom - y);
    }
}
