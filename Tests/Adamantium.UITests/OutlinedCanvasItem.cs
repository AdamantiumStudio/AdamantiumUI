using System.Collections.Generic;
using System.ComponentModel;
using Adamantium.Mathematics;
using Adamantium.UI.Controls.Canvases;
using Adamantium.UI.Core;

namespace Adamantium.UITests;

/// <summary>An item with no grips that says what marks it selected - as a mark on lines of text does.</summary>
internal sealed class OutlinedCanvasItem : ICanvasItem
{
    public OutlinedCanvasItem(IReadOnlyList<IReadOnlyList<Vector2<double>>> outlines, Rect bounds)
    {
        SelectionOutlines = outlines;
        Bounds = bounds;
    }

    public event PropertyChangedEventHandler PropertyChanged
    {
        add { }
        remove { }
    }

    public Rect Bounds { get; }

    public int Order { get; set; }

    public CanvasHandles Handles => CanvasHandles.None;

    public IReadOnlyList<IReadOnlyList<Vector2<double>>> SelectionOutlines { get; }

    public bool HitTest(Vector2<double> world, double tolerance) => Bounds.Contains(world);

    public void Render(CanvasDrawing drawing)
    {
    }

    public void Move(Vector2<double> worldDelta)
    {
    }

    public void Resize(Rect world)
    {
    }
}
