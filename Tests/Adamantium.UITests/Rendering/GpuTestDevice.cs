using System;
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
                    _main = MainGraphicsDevice.Create(new GraphicsDeviceFactory(), Settings(), "UITests", true);
                    _device = _main.CreateRenderDevice();
                }

                return _device;
            }
        }
    }

    /// <summary>The default settings, with the descriptor model the run asks for:
    /// <c>dotnet test -- TestRunParameters.Parameter(name="DescriptorModel", value="Sets")</c> runs every GPU test on
    /// descriptor sets on a device that has the heap.</summary>
    private static GraphicsSettings Settings()
    {
        var settings = GraphicsSettings.CreateDefault();
        if (Enum.TryParse<DescriptorModel>(TestContext.Parameters.Get("DescriptorModel", ""), out var model))
        {
            settings.DescriptorModel = model;
        }

        return settings;
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
