using System;
using System.Collections.Generic;
using Adamantium.Imaging;
using Adamantium.Mathematics;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Media.Imaging;
using Adamantium.UI.Core.Rendering;

namespace Adamantium.UITests.Printing;

/// <summary>Stands in for the GPU when an element is printed: each snapshot asked for is noted and answered at once
/// with a blank bitmap of the pixels the region covers at its scale.</summary>
internal sealed class SnapshotRecorder : IVisualRenderer
{
    public List<(IUIComponent Visual, Rect Region, double Scale, SnapshotContent Content)> Asked { get; } = [];

    public List<ImageSource> Given { get; } = [];

    /// <summary>Whether every snapshot fails, as with no GPU to spare.</summary>
    public bool Fails { get; init; }

    /// <summary>From which snapshot on, counted from 0, they fail - the GPU running out partway.</summary>
    public int FailsFrom { get; init; } = int.MaxValue;

    public void RequestSnapshot(IUIComponent visual, Rect region, double scale, SnapshotContent content,
        Action<ImageSource> onReady)
    {
        Asked.Add((visual, region, scale, content));
        if (Fails || Asked.Count > FailsFrom)
        {
            onReady(null);
            return;
        }

        var width = (uint)Math.Ceiling(region.Width * scale);
        var height = (uint)Math.Ceiling(region.Height * scale);
        var image = new BitmapSource(width, height, 1 / scale, 1 / scale, SurfaceFormat.B8G8R8A8.UNorm,
            new byte[width * height * 4]);
        Given.Add(image);
        onReady(image);
    }

    public ImageSource Render(IUIComponent visual, Size<double> size, double scale = 1, Color? clearColor = null) =>
        throw new NotSupportedException();

    public ImageSource Render(string aumlText, Size<double> size, double scale = 1, Color? clearColor = null) =>
        throw new NotSupportedException();

    public ImageSource Render(IUIComponent visual, double scale = 1, Color? clearColor = null) =>
        throw new NotSupportedException();

    public ImageSource Render(string aumlText, double scale = 1, Color? clearColor = null) =>
        throw new NotSupportedException();

    public void RequestSnapshot(IUIComponent visual, Action<ImageSource> onReady) => throw new NotSupportedException();

    public void RequestRender(IUIComponent visual, Size<double> size, double scale, Color? clearColor,
        Action<ImageSource> onReady) => throw new NotSupportedException();

    public void RecordPendingSnapshots()
    {
    }

    public void DrawPendingSnapshots()
    {
    }

    public bool HasPendingDraws => false;
}
