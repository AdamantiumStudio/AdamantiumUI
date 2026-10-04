using Adamantium.Mathematics;
using Adamantium.UI.Core.Graphics;
using Adamantium.UI.Core.RoutedEvents;
using Transform = Adamantium.UI.Core.Media.Transform;

namespace Adamantium.UI.Core;

public interface IUIComponent : IFundamentalUIComponent
{
    event EventHandler<VisualParentChangedEventArgs> VisualParentChanged;
    
    Guid RenderId { get; }
    Boolean ClipToBounds { get; set; }

    /// <summary>How this clip's corners are rounded (TL, TR, BR, BL). A scissor is square, so rounding is applied by the
    /// shaders; zero means the plain rectangular clip. Unset falls back to the container's own CornerRadius, where it
    /// has one - see UIComponent.ClipCornerRadius.</summary>
    Vector4F ClipRadii { get; }

    Double Opacity { get; set; }
    Double SelfOpacity { get; set; }
    bool IsEnabled { get; set; }
    Boolean AllowDrop { get; set; }
    Boolean IsHitTestVisible { get; set; }
    bool IsGeometryValid { get; }

    /// <summary>Whether its last record produced no draw commands; set by the recorder, false until first recorded. Says
    /// nothing about its children.</summary>
    bool DrawsNothing { get; set; }

    /// <summary>Geometry is stale because what it draws changed, not because of layout; cleared by
    /// <see cref="Render"/>.</summary>
    bool GeometryStaleByContent { get; }
    Size RenderSize { get; set; }
    //Vector2 Location { get; }
    Visibility Visibility { get; set; }
    Rect Bounds { get; set; }
    Rect ClipRectangle { get; }

    /// <summary>The soft band around this component's outline, if it wears one. Read at RECORD time and baked into the
    /// draw command - never dereferenced by the renderer, which is editing-thread state.</summary>
    Media.Aura Aura { get; set; }

    /// <summary>The shadow this component casts, if any. Same record-time rule as <see cref="Aura"/>.</summary>
    Media.Shadow Shadow { get; set; }

    Vector2 ClipPosition { get; set; }
    IUIComponent VisualParent { get; }

    /// <summary>The component in whose coordinate space this one is DRAWN - normally the visual parent. An adorner is not
    /// in the visual tree at all (its VisualParent is null) yet draws in its adorned element's space, so it reports that
    /// element here. Both the live <see cref="WorldTransform"/> and the renderer's frozen composition go through this, so
    /// there is one answer to "whose space am I in", not two that can drift apart.</summary>
    IUIComponent RenderParent { get; }

    /// <summary>Whether the render parent's <see cref="ClipToBounds"/> applies; false for adorners, which draw around
    /// their target and are clipped only by <see cref="ClipsAdorners"/>.</summary>
    bool ClippedByRenderParent { get; }

    /// <summary>Whether this <see cref="ClipToBounds"/> also clips adorners inside it; true only for viewports such as a
    /// scroll presenter.</summary>
    bool ClipsAdorners { get; }

    IRootVisualComponent RootVisual { get; }

    /// <summary>The subtree root owning this element's layout when it is not the visual root (the popup layer for overlay
    /// content); null otherwise. Only one owner may lay it out.</summary>
    IUIComponent LayoutRoot { get; }

    /// <summary>Which stage draws this element and so owns its dirty marks; inherited at attach. Null means the window
    /// content.</summary>
    RenderDirtyScope RenderScope { get; }

    Int32 ZIndex { get; set; }

    /// <summary>Whether this component's shapes are anti-aliased. Off for an axis-aligned rectangle on whole pixels,
    /// which needs no fringe and is only harmed by one - see UIComponent.UseAnalyticAA.</summary>
    Boolean UseAnalyticAA { get; set; }

    bool IsAttachedToVisualTree { get; }

    /// <summary>True while this subtree is PARKED: deliberately out of the tree, but kept - so what the renderer cached
    /// for it must survive. Detachment alone means "gone"; parking is what tells the difference.</summary>
    bool IsParked { get; }

    /// <summary>Draws the subtree once per matrix (composed as <c>clone * world</c>) instead of once; clones have no
    /// layout, hit-test or state. Null or empty means a single draw.</summary>
    IReadOnlyList<Matrix4x4F> RenderClones { get; }

    /// <summary>A motion node: its subtree is baked in its own space, so moving it is one matrix write. Set by virtualizing
    /// hosts and by the compositor for transforms it animates.</summary>
    bool IsRenderMotionNode { get; set; }

    bool IsRootComponent { get; }

    Transform LayoutTransform { get; set; }
    
    Transform RenderTransform { get; set; }

    /// <summary>The point <see cref="RenderTransform"/> turns/scales about, as a FRACTION of the element's own size (0.5,0.5
    /// = its center). Relative, so one template stays centered at any size. Read by the compositor when it composes the
    /// element's matrix itself.</summary>
    Vector2 RenderTransformOrigin { get; set; }

    Matrix4x4F WorldTransform { get; }

    /// <summary>This element's transform in its parent's space (the parent-relative part of <see cref="WorldTransform"/>),
    /// so a frame-scoped consumer can compose world transforms top-down without re-walking to the root per node.</summary>
    Matrix4x4F LocalTransform { get; }

    /// <summary>Every element below this one at any depth, depth first; <see cref="VisualChildren"/> for the children alone.</summary>
    IEnumerable<IUIComponent> GetVisualDescendants();
        
    IReadOnlyCollection<IUIComponent> VisualChildren { get; }

    void InvalidateRender(bool invalidateChildren);

    /// <summary>Emits this element's draw commands into <paramref name="context"/> read-only - runs OnRender WITHOUT
    /// touching IsGeometryValid (no RenderDirty mark, no loop wake) or the clean-frame gate. For an off-screen snapshot of
    /// a LIVE element through a parallel render cache, where the ordinary <c>Render()</c> would no-op on a valid element.</summary>
    void RenderReadOnly(IDrawingContext context);

    /// <summary>Only this element's PAINT changed - same shape, same draw commands, a new color/brush/opacity. It is NOT
    /// re-rendered: the renderer re-bakes the GPU data of the units it already holds (see
    /// <see cref="PropertyMetadataOptions.AffectsPaint"/>).</summary>
    void InvalidatePaint();

    /// <summary>Narrow-phase hit test of a local point against the element's own geometry; the default is its bounds,
    /// shapes override it.</summary>
    bool HitTestCore(Vector2 localPoint);

    void Render(IDrawingContext context);
    
    /// <summary>
    /// Raised when the control is attached to a rooted logical tree.
    /// </summary>
    public event EventHandler<VisualTreeAttachmentEventArgs> AttachedToVisualTreeEvent;

    /// <summary>
    /// Raised when the control is detached from a rooted logical tree.
    /// </summary>
    public event EventHandler<VisualTreeAttachmentEventArgs> DetachedFromVisualTreeEvent;
}