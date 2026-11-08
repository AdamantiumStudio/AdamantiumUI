using Adamantium.Graphics;
using Adamantium.Graphics.Core;
using Adamantium.Imaging;
using Adamantium.Mathematics;
using Adamantium.UI.WebView.Cef.Interop;

namespace Adamantium.UI.WebView;

/// <summary>
/// Hands a page's frames to the panel that shows them, as a producer on the panel's own device does: a ring of two
/// surfaces, each frame written into the one the panel gave back. A frame comes whole - BGRA pixels in memory, or
/// Chromium's texture on the same GPU, copied there before it goes back to Chromium - so a surface gets the rectangles
/// that changed since it was last written, read from the frame at hand. Runs on the thread CEF paints on, the UI loop,
/// with the engine's device - not the panel's.
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
    private SharedSurface retired;
    private bool shown;
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
        if (!NextWrite(dirty, width, height, out var slot, out var region))
        {
            return false;
        }

        if (slot >= 0)
        {
            WriteRegion(slot, pixels, width, region);
        }

        return true;
    }

    /// <summary>Copies a frame of <paramref name="width"/> × <paramref name="height"/> pixels from Chromium's
    /// <paramref name="texture"/> (a D3D11 texture's NT handle, <paramref name="textureWidth"/> ×
    /// <paramref name="textureHeight"/>) whose <paramref name="dirty"/> rectangles changed, done before the call
    /// returns. False as for <see cref="Write"/>; a texture the device does not take throws
    /// <see cref="NotSupportedException"/>.</summary>
    public bool Copy(ReadOnlySpan<CefRect> dirty, IntPtr texture, int textureWidth, int textureHeight, int width, int height)
    {
        if (!NextWrite(dirty, width, height, out var slot, out var region))
        {
            return false;
        }

        if (slot >= 0 && ring.SurfaceAt(slot).CopyFrom(device, texture, SharedHandleType.D3D11Texture,
                (uint)textureWidth, (uint)textureHeight, [region], frame + 1))
        {
            frame++;
            Written(slot);
        }

        return true;
    }

    /// <summary>Whether the panel has given back a surface to write the next frame into.</summary>
    public bool HasFreeSurface => ring == null || FreeSurface() >= 0;

    /// <summary>Retires the ring; the panel is to have let go of it first.</summary>
    public void Dispose()
    {
        Retire(ref retired);
        Retire(ref ring);
    }

    private bool NextWrite(ReadOnlySpan<CefRect> dirty, int width, int height, out int slot, out Rectangle<int> region)
    {
        slot = -1;
        region = default;
        if (width <= 0 || height <= 0)
        {
            return true;
        }

        if (ring == null || ring.Width != width || ring.Height != height)
        {
            StartRing(width, height);
        }
        else
        {
            foreach (var rect in dirty)
            {
                var area = new Rectangle<int>(rect.x, rect.y, rect.width, rect.height);
                for (var i = 0; i < RingSize; i++)
                {
                    changed[i] = changed[i] is { } before ? Rectangle<int>.Union(before, area) : area;
                }
            }
        }

        var free = FreeSurface();
        if (free < 0)
        {
            return false;
        }

        if (changed[free] is { } changedRegion && Clip(changedRegion, width, height) is { Width: > 0, Height: > 0 } inFrame)
        {
            slot = free;
            region = inFrame;
        }

        return true;
    }

    private void StartRing(int width, int height)
    {
        if (shown)
        {
            retired = ring;
            ring = null;
        }
        else
        {
            Retire(ref ring);
        }

        ring = SharedSurface.CreateInDevice(device, (uint)width, (uint)height, SurfaceFormat.B8G8R8A8.UNorm,
            bufferCount: RingSize);
        Array.Clear(written);
        shown = false;
        var whole = new Rectangle<int>(0, 0, width, height);
        changed[0] = whole;
        changed[1] = whole;
    }

    private void Retire(ref SharedSurface surface)
    {
        if (surface != null)
        {
            device.AddToDeferDisposeQueue(surface);
            surface = null;
        }
    }

    private void WriteRegion(int slot, byte* pixels, int width, Rectangle<int> region)
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

        if (ring.SurfaceAt(slot).WriteRegions(device,
                [new TextureRegionUpdate(0, 0, region.X, region.Y, (uint)region.Width, (uint)region.Height,
                    scratch.AsMemory(0, size))],
                frame + 1))
        {
            frame++;
            Written(slot);
        }
    }

    private void Written(int slot)
    {
        written[slot] = frame;
        changed[slot] = null;
        ring.Descriptor.NoteSubmitted(frame);
        if (!shown)
        {
            shown = true;
            show(ring.Descriptor);
            Retire(ref retired);
        }

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
