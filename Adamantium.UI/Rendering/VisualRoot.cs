using Adamantium.Mathematics;
using Adamantium.UI.Controls.Base;
using Adamantium.UI.Core;

namespace Adamantium.UI.Rendering;

// A minimal off-screen root hosting one detached visual for VisualRenderer: identity screen transforms, no UI context.
// Live, parented elements must not be hosted here; they are baked without reparenting.
internal sealed class VisualRoot : MeasurableUIComponent, IRootVisualComponent
{
    private readonly IUIComponent _content;

    public VisualRoot(IUIComponent content, double width, double height)
    {
        ClientWidth = width;
        ClientHeight = height;
        _content = content;
        if (_content != null) AddVisualChild(_content);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        if (_content is IMeasurableComponent measurable) measurable.Measure(availableSize);
        return availableSize;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (_content is IMeasurableComponent measurable) measurable.Arrange(new Rect(finalSize));
        return finalSize;
    }

    /// <summary>A detached render is the definition of one-shot: it is laid out, recorded and drawn once, and whatever
    /// was left for later is simply missing from the picture.</summary>
    public bool RendersOnce => true;

    public double Left { get; set; }
    public double Top { get; set; }
    public string Title { get; set; }
    public double ClientWidth { get; set; }
    public double ClientHeight { get; set; }

    // Off-screen: no OS window, no context, identity screen<->client (the projection maps client space 1:1).
    public IUIContext UIContext => null;
    public void AttachContextAndInitialize(IUIContext context) { }
    // Identity: with no OS window there is no display scale to cross, so the two units coincide here.
    public PixelPoint Position { get; set; }
    public Vector2 PointToClient(PixelPoint point) => new((float)point.X, (float)point.Y);
    public PixelPoint PointToScreen(Vector2 point) => new(point.X, point.Y);
}
