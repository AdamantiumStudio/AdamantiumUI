using Adamantium.Graphics;
using Adamantium.Graphics.Core;
using NUnit.Framework;

namespace Adamantium.UITests.Rendering;

// One Vulkan device for every GPU fixture in this namespace (SetUpFixture lifetime), as in an application.
[SetUpFixture]
public class GpuTestDevice
{
    private static readonly object Gate = new();
    private static MainGraphicsDevice _main;
    private static IGraphicsDevice _device;

    /// <summary>The shared render device, created by the first test that asks for it: a test in this namespace that
    /// never asks runs on a machine without a GPU. Valid for the whole run of this namespace's fixtures.</summary>
    public static IGraphicsDevice Device
    {
        get
        {
            lock (Gate)
            {
                if (_device == null)
                {
                    _main = MainGraphicsDevice.Create(new GraphicsDeviceFactory(), 3, "UITests", true);
                    _device = _main.CreateRenderDevice();
                }

                return _device;
            }
        }
    }

    /// <summary>Waits idle and frees retired resources, which otherwise wait for a next frame that tests never begin.
    /// Call when a harness is done with its resources.</summary>
    public static void Reclaim()
    {
        if (_device == null)
        {
            return;
        }

        _device.DeviceWaitIdle();
        _main.FlushRetiredAfterIdle();
    }

    [OneTimeTearDown]
    public void Destroy()
    {
        lock (Gate)
        {
            _main?.Dispose();
            _main = null;
            _device = null;
        }
    }
}
