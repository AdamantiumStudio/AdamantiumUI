using System;
using System.Collections.Generic;
using Adamantium.Graphics.Core;
using Adamantium.Graphics.Core.Presentation;

namespace Adamantium.UI.WebView.Tests;

/// <summary>A device service whose main device is not made yet, as at an application's start.</summary>
internal sealed class NoDevices : IGraphicsDeviceService
{
    public bool IsInDebugMode { get; set; }

    public IGraphicsDeviceFactory GraphicsDeviceFactory => null;

    public bool IsReady => false;

    public bool DeviceUpdateNeeded { get; set; }

    public MainGraphicsDevice MainGraphicsDevice => null;

    public IGraphicsDevice ResourceLoaderDevice => null;

    public IReadOnlyList<IGraphicsDevice> GraphicsDevices => [];

    public event EventHandler<EventArgs> DeviceCreated { add { } remove { } }

    public event EventHandler<EventArgs> DeviceChangeBegin { add { } remove { } }

    public event EventHandler<EventArgs> DeviceChangeEnd { add { } remove { } }

    public event EventHandler<EventArgs> DeviceLost { add { } remove { } }

    public event EventHandler<EventArgs> DeviceDisposing { add { } remove { } }

    public void CreateMainDevice(string name)
    {
    }

    public IGraphicsDevice CreateRenderDevice() => null;

    public void ChangeOrCreateMainDevice(string name, bool forceUpdate)
    {
    }

    public void RaiseFrameFinished()
    {
    }
}
