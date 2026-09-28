using Adamantium.Graphics;
using Adamantium.Graphics.Core;
using NUnit.Framework;

namespace Adamantium.UITests.Rendering;

// One Vulkan device for every GPU fixture in this namespace (SetUpFixture lifetime), as in an application.
[SetUpFixture]
public class GpuTestDevice
{
    private static MainGraphicsDevice _main;

    /// <summary>The shared render device. Valid for the whole run of this namespace's fixtures.</summary>
    public static IGraphicsDevice Device { get; private set; }

    /// <summary>Waits idle and frees retired resources, which otherwise wait for a next frame that tests never begin.
    /// Call when a harness is done with its resources.</summary>
    public static void Reclaim()
    {
        if (Device == null) return;

        Device.DeviceWaitIdle();
        _main.FlushRetiredAfterIdle();
    }

    [OneTimeSetUp]
    public void Create()
    {
        _main = MainGraphicsDevice.Create(new GraphicsDeviceFactory(), 3, "UITests", true);
        Device = _main.CreateRenderDevice();
    }

    [OneTimeTearDown]
    public void Destroy()
    {
        _main?.Dispose();
        _main = null;
        Device = null;
    }
}
