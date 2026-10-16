using System;
using System.Runtime.CompilerServices;
using Adamantium.Mathematics;
using Adamantium.UI.Core.Data;
using Adamantium.UI.Core.Graphics;

namespace Adamantium.UI.Core.Media.Drawings;

/// <summary>A RETAINED, resolution-independent picture: a description of draw calls rather than pixels. Unlike a bitmap
/// it is replayed straight into whatever session is drawing it (see <see cref="Render"/>), so the same drawing used at
/// 16px and at 512px is one shared mesh drawn twice - crisp at both, and nothing to re-bake when a size or the DPI
/// changes. That is the whole reason it exists next to <see cref="Imaging.ImageSource"/>.</summary>
public abstract class Drawing : AdamantiumComponent
{
    private static readonly ConditionalWeakTable<AdamantiumComponent, WeakReference<AdamantiumComponent>> HungFrom = new();

    protected Drawing()
    {
        PropertyChanged += (_, _) => RaiseChanged();
    }

    /// <summary>Raised when anything about the picture changes - a property here, a nested child, or a brush/geometry one
    /// of them draws with. Whoever shows the drawing subscribes and re-renders; without it a change buried three levels
    /// down inside a <see cref="DrawingGroup"/> would never reach the element holding the outermost drawing.</summary>
    public event EventHandler Changed;

    protected void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);

    /// <summary>Bubble a nested drawing's change out as this one's own.</summary>
    protected void OnChildChanged(object sender, EventArgs e) => RaiseChanged();

    internal void BrushChanged() => RaiseChanged();

    private protected BrushChangeRelay Follow(BrushChangeRelay current, Brush brush)
    {
        current?.Release();
        return brush == null ? null : new BrushChangeRelay(brush, this);
    }

    /// <summary>The picture's extent in ITS OWN coordinates. This is the viewbox a consumer maps onto its destination,
    /// so an icon authored in a 0..24 box scales by destination/24 - the drawing itself never knows its output size.</summary>
    public abstract Rect Bounds { get; }

    /// <summary>Replay into <paramref name="session"/>, placing this drawing through <paramref name="transform"/>
    /// (the consumer's viewbox-to-destination mapping, with any enclosing group transforms already folded in).
    /// <paramref name="currentColor"/> paints whatever is written <see cref="CurrentColorBrush">CurrentColor</see>: the
    /// Foreground of the element showing the drawing, null where there is none.</summary>
    public abstract void Render(IDrawingSession session, Matrix4x4F transform, Brush currentColor);

    /// <summary>Hang this drawing, and everything inside it, under the component that shows it. A drawing lives in a
    /// RESOURCE - outside the tree entirely - so on its own a <c>{Binding}</c> anywhere inside has no DataContext to
    /// resolve against and silently produces null: a drawing whose brushes bind to the view model then draws NOTHING.
    /// The inheritance parent is the same route <see cref="Transform"/> takes for exactly the same reason.</summary>
    public void Attach(AdamantiumComponent parent)
    {
        InheritanceParent = parent;
        // A resource is BUILT before it is ever shown, so its bindings were established with no parent and nothing else
        // would re-run them - a drawing has no attach event of its own (again as Transform).
        BindingEngine.RefreshBindings(this);
        AttachChildren();
    }

    /// <summary>Take this drawing, and everything it hung, off the component that showed it: the bindings inside stop
    /// resolving against that component, and a transform a text effect also draws with passes to the effect.</summary>
    public void Detach()
    {
        InheritanceParent = null;
        DetachChildren();
    }

    /// <summary>Pass the attachment on to whatever this drawing owns - nested drawings, brushes, transforms.</summary>
    protected virtual void AttachChildren() { }

    /// <summary>Pass the detachment on to whatever this drawing owns.</summary>
    protected virtual void DetachChildren() { }

    /// <summary>Hang one owned component (a brush, a geometry) under this drawing and re-establish its bindings.</summary>
    protected void AttachOwned(AdamantiumComponent owned)
    {
        if (owned == null) return;

        if (owned is not Transform && owned.InheritanceParent is { } before && !ReferenceEquals(before, this))
        {
            var origin = before is Drawing && HungFrom.TryGetValue(owned, out var earlier)
                && earlier.TryGetTarget(out var first)
                ? first
                : before;
            HungFrom.AddOrUpdate(owned, new WeakReference<AdamantiumComponent>(origin));
        }

        owned.InheritanceParent = this;
        BindingEngine.RefreshBindings(owned);
    }

    /// <summary>Whether this drawing still draws with <paramref name="owned"/> through another of its properties, so
    /// replacing one of them must keep it hung.</summary>
    protected virtual bool StillOwns(AdamantiumComponent owned) => false;

    /// <summary>Let go of one owned component, if this drawing is what it hangs on: it goes back to what it hung on
    /// before.</summary>
    protected void DetachOwned(AdamantiumComponent owned)
    {
        if (owned == null)
        {
            return;
        }

        if (owned is Transform transform)
        {
            transform.ReleaseFrom(this);
            return;
        }

        if (!ReferenceEquals(owned.InheritanceParent, this))
        {
            return;
        }

        owned.InheritanceParent = HungFrom.TryGetValue(owned, out var weak) && weak.TryGetTarget(out var back)
            && back is not FundamentalUIComponent { IsDiscarded: true } && back is not Drawing { InheritanceParent: null }
            ? back
            : null;
        HungFrom.Remove(owned);
        BindingEngine.RefreshBindings(owned);
    }

    internal void ReplaceOwned(AdamantiumComponent previous, AdamantiumComponent next)
    {
        if (previous != null && !StillOwns(previous))
        {
            DetachOwned(previous);
        }

        if (InheritanceParent != null)
        {
            AttachOwned(next);
        }
    }

    internal void ReplaceChild(Drawing previous, Drawing next)
    {
        if (previous != null && ReferenceEquals(previous.InheritanceParent, this))
        {
            previous.Detach();
        }

        if (InheritanceParent != null)
        {
            next?.Attach(this);
        }
    }
}
