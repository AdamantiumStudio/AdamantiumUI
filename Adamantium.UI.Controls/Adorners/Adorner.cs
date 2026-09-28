using Adamantium.Mathematics;
using Adamantium.UI.Controls.Base;
using Adamantium.UI.Controls.Shapes;
using Adamantium.UI.Core;

namespace Adamantium.UI.Controls.Adorners;

/// <summary>A visual drawn on top of an adorned element by the adorner stage, outside the content tree. It draws itself or
/// hosts a themed template, in the adorned element's coordinate space.</summary>
public class Adorner : TemplatedUIComponent
{
    public Adorner()
    {
    }

    public Adorner(IUIComponent adornedElement)
    {
        AdornedElement = adornedElement;
    }

    /// <summary>How far outside the adorned element this adorner is entitled to draw. A VIEWPORT that clips adorners
    /// (see <see cref="IUIComponent.ClipsAdorners"/>) admits this much beyond its edge, so a control standing flush
    /// against the edge of a scroll area still gets its whole ring - the alternative was every such control wearing a
    /// shaved one, or every author remembering to pad scrollable content by exactly the ring's standoff.</summary>
    public virtual double ClipStandoff => 0;

    private IUIComponent _adornedElement;

    /// <summary>The element this adorner decorates and draws in. It becomes the inheritance parent - not a logical parent,
    /// which would re-theme the adorner on every cue - so a themed adorner can bind.</summary>
    public IUIComponent AdornedElement
    {
        get => _adornedElement;
        set
        {
            if (ReferenceEquals(_adornedElement, value)) return;
            _adornedElement = value;
            InheritanceParent = value as AdamantiumComponent;
        }
    }

    // Stated as the RENDER PARENT, not a WorldTransform override: the render pass composes each component's transform from
    // LocalTransform x RenderParent, so an adorner outside that chain would draw at the window origin. Pointing RenderParent
    // at the adorned element places the adorner - and its arranged template - exactly on its target.
    public override IUIComponent RenderParent => AdornedElement;

    // ...but NOT clipped to that element's box: an adorner exists to draw AROUND its target, and a control whose
    // template clips its own content would otherwise erase the whole decoration. Everything above the target still
    // clips it - see IUIComponent.ClippedByRenderParent.
    public override bool ClippedByRenderParent => false;

    /// <summary>The adorned element's painted rectangle in its OWN local space: a Shape's stroke-aware RenderBounds,
    /// otherwise its arranged box. What an OnRender-drawing adorner (a selection / hover frame) decorates. Virtual
    /// because an adorner may decorate a box OTHER than the element's own - the focus ring stands off it.</summary>
    public virtual Rect AdornedBounds =>
        AdornedElement is Shape shape ? shape.RenderBounds : new Rect(AdornedElement.RenderSize);

    /// <summary>True if this adorner's template should be sized to fill the adorned element's bounds (a frame that wraps its
    /// target). The adorner stage themes it and re-lays it out to <see cref="AdornedBounds"/> each frame. False for a cue
    /// placed at a specific spot (the drop insertion indicator), which the drag engine sizes and positions itself.</summary>
    public virtual bool FillsAdornedBounds => false;

    /// <summary>Where the adorner goes in the adorned element's space, once it knows how big it came out. A frame fills
    /// the target; a BADGE (a key tip) hangs off an edge and is the reason this is asked at all - the stage used to lay
    /// out frames only, so anything else was themed but never sized and drew nothing.</summary>
    public virtual Rect PlaceIn(Size desired) => AdornedBounds;

    /// <summary>Set by the adorner stage once it has applied the theme (so a themed frame is templated, not re-themed each
    /// frame). Reset if the adorner is reused for a different element.</summary>
    public bool ThemeApplied { get; set; }
}
