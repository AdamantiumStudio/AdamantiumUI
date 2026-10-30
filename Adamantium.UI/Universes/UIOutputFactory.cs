using System;
using Adamantium.Multiverse;
using Adamantium.Graphics.Core;
using Adamantium.Imaging;
using Adamantium.UI.Controls.Panels;
using Adamantium.UI.Core;

namespace Adamantium.UI.Universes;

/// <summary>
/// Outputs over the UI's surfaces: a window, or a <see cref="RenderTargetPanel"/> inside one.
/// </summary>
public class UIOutputFactory : IOutputFactory
{
    private readonly IGraphicsDeviceService _uiDevices;

    /// <summary>Outputs whose panels draw with <paramref name="uiDevices"/>: a universe on its device hands them frames
    /// within it.</summary>
    public UIOutputFactory(IGraphicsDeviceService uiDevices)
    {
        _uiDevices = uiDevices;
    }

    public UniverseOutput Create(OutputContext context, IUniverseEventAggregator events)
    {
        if (context.Context is IWindow)
        {
            return new WindowUniverseOutput(events, context);
        }

        if (context.Context is RenderTargetPanel)
        {
            return new RenderTargetUniverseOutput(events, context, _uiDevices);
        }

        throw new NotSupportedException($"context of type {context.Context?.GetType()} is not supported");
    }

    public UniverseOutput Create(OutputContext context, IUniverseEventAggregator events, SurfaceFormat pixelFormat, DepthFormat depthFormat, MSAALevel msaaLevel)
    {
        if (context.Context is RenderTargetPanel)
        {
            return new RenderTargetUniverseOutput(events, context, _uiDevices, pixelFormat, depthFormat, msaaLevel);
        }

        throw new NotSupportedException($"context of type {context.Context?.GetType()} is not supported");
    }
}
