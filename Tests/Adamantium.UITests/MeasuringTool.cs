using Adamantium.UI.Controls.Canvases;

namespace Adamantium.UITests;

/// <summary>A tool whose points are pulled to what the canvas shows, as a measurement's are; it does nothing else.</summary>
internal sealed class MeasuringTool : ICanvasTool
{
    public bool IsBusy => false;

    public bool SnapsToContent => true;

    public void OnPressed(InfiniteCanvas canvas, CanvasPointerEventArgs e)
    {
    }

    public void OnMoved(InfiniteCanvas canvas, CanvasPointerEventArgs e)
    {
    }

    public void OnReleased(InfiniteCanvas canvas, CanvasPointerEventArgs e)
    {
    }

    public void Render(Adamantium.UI.Core.Graphics.IDrawingSession session, InfiniteCanvas canvas)
    {
    }

    public void Cancel(InfiniteCanvas canvas)
    {
    }
}
