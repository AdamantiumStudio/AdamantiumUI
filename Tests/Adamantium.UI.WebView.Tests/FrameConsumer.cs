using System;
using System.Runtime.InteropServices;
using Adamantium.Graphics;
using Adamantium.Graphics.Core;

namespace Adamantium.UI.WebView.Tests;

/// <summary>Takes a browser's frames as the view's panel does: copies the newest from the surfaces shown, which gives
/// them back to the browser for its next frames, and reads a pixel of the last one taken.</summary>
internal sealed class FrameConsumer : IDisposable
{
    private readonly IGraphicsDevice device;
    private SharedSurfaceDescriptor descriptor;
    private SharedSurface taken;

    public FrameConsumer(IGraphicsDeviceService devices)
    {
        device = devices.CreateRenderDevice();
    }

    /// <summary>Takes the newest frame in <paramref name="shown"/>, the surfaces the browser shows now.</summary>
    public void Take(SharedSurfaceDescriptor shown)
    {
        if (shown == null)
        {
            return;
        }

        if (!ReferenceEquals(shown, descriptor))
        {
            device.DeviceWaitIdle();
            taken?.Dispose();
            taken = SharedSurface.Import(device, shown);
            descriptor = shown;
        }

        taken.LatchCopy(device);
    }

    /// <summary>The middle pixel of the last frame taken, as BGRA; null before the first.</summary>
    public byte[] Center()
    {
        if (taken?.LatchedCopy == null)
        {
            return null;
        }

        device.DeviceWaitIdle();
        using var image = taken.LatchedCopy.ToImage();
        var pixel = new byte[4];
        Marshal.Copy(image.DataPointer + (int)((descriptor.Height / 2 * descriptor.Width + descriptor.Width / 2) * 4),
            pixel, 0, 4);
        return pixel;
    }

    public void Dispose()
    {
        device.DeviceWaitIdle();
        taken?.Dispose();
        device.Dispose();
    }
}
