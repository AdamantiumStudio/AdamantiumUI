using System;
using System.Collections.Generic;
using Adamantium.Mathematics;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Graphics;

namespace Adamantium.UI.Rendering;

// The immutable handoff from the device-free recorder to the GPU-owning applier: a delta (Clean carries nothing, Partial
// the dirty components, Full the whole paint order). The retained GPU cache stays with the applier.
internal sealed class RenderPacket
{
    /// <summary>Clean = replay retained ops; Partial = re-render only <see cref="Draws"/>; Full = rebuild from the
    /// whole paint-order sequence in <see cref="Draws"/>.</summary>
    public RenderBuildKind Kind;

    /// <summary>Full: every visited (visible, attached) component in PAINT ORDER, each with its recorded draw commands.
    /// Partial: only the geometry-dirty components. Each entry's <see cref="ComponentDraw.Commands"/> is a COPY of the
    /// component's <c>component.Render</c> output (the shared drawing context is reused per component, so the recorder
    /// must snapshot each component's commands before rendering the next).</summary>
    public readonly List<ComponentDraw> Draws = new();

    /// <summary>Motion nodes that moved this frame (the O(1)-scroll fast path rewrites their transform-table matrices).</summary>
    public readonly List<IUIComponent> MovedNodes = new();

    /// <summary>Partial only: the components whose contents changed, for the draw phase's O(dirty) slot-patch + op replay.</summary>
    public readonly List<IUIComponent> PartialDirty = new();

    /// <summary>The layout entries the recorder re-froze this frame - the DELTA of the frozen snapshot. The applier keeps its
    /// OWN snapshot dictionary and folds each packet's delta into it, so the two threads never share one: the recorder's map
    /// is authoritative and mutable on the loop thread, the applier's is a private replica built only from packets it has
    /// consumed. O(changed), not O(scene).</summary>
    public readonly List<KeyValuePair<IUIComponent, LayoutSnapshot>> SnapDelta = new();

    /// <summary>The recorder dropped its whole snapshot (a full walk) - the applier must clear its replica before folding in
    /// <see cref="SnapDelta"/>, which then carries the complete scene.</summary>
    public bool SnapReset;

    /// <summary>Partial only: whether the dirty set was a MOVE (positions changed) vs a geometry-only change.</summary>
    public bool IsTransformDirty;

    /// <summary>The components that MOVED - named, so the draw can ask whether it is patching all of them. A move only
    /// forces a rebuild because the recorded frame bakes positions; a mover whose slots this same frame re-bakes does
    /// not need one.</summary>
    public readonly List<IUIComponent> Moved = new();

    /// <summary>Something moved that could not be named (a bare Transform ticking with no owner). Then Moved is not the
    /// whole story and nothing about movement can be forgiven.</summary>
    public bool TransformUnknown;

    /// <summary>Something moved, so the applier drops its composed transform memos before drawing (they are applier-owned,
    /// so the recorder cannot clear them).</summary>
    public bool ClearMemos;

    /// <summary>Frame projection captured at record (from the root visual).</summary>
    public Matrix4x4F ProjectionMatrix;

    /// <summary>Structural only: components DETACHED from the tree - the applier frees their retained units. A Full walk needs
    /// no such list: it rebuilds the paint order from scratch and reclaims whatever it did not visit.</summary>
    public readonly List<IUIComponent> Removed = new();

    /// <summary>Structural only: components that left the PAINT ORDER but are still in the tree (hidden, or under something
    /// hidden). They stop drawing - and that is all: their units are KEPT, so showing them again costs a re-insert, not a
    /// rebuild. This is what a recycled list container is (the virtualizer collapses it and shows it again a few rows later),
    /// so freeing here would churn GPU buffers on every scroll step.</summary>
    public readonly List<IUIComponent> Undrawn = new();

    /// <summary>Structural only: components that KEEP their units but MOVED in the paint order (a recycled container re-added
    /// at a different position) - the applier re-sorts their groups to the new rank. A component that is re-recorded carries
    /// its rank on its <see cref="ComponentDraw"/> instead; this list is for the ones with nothing new to draw.</summary>
    public readonly List<KeyValuePair<IUIComponent, long>> Reranks = new();

    /// <summary>Structural only: the reranks are a renumber (fresh gaps, same relative order), not a reorder.</summary>
    public bool Renumbered;

    /// <summary>Reset for reuse (the packet is pooled per cache; a Clean frame produces an empty one).</summary>
    public void Reset(RenderBuildKind kind)
    {
        Kind = kind;
        Draws.Clear();
        MovedNodes.Clear();
        PartialDirty.Clear();
        SnapDelta.Clear();
        Removed.Clear();
        Undrawn.Clear();
        Reranks.Clear();
        Renumbered = false;
        SnapReset = false;
        IsTransformDirty = false;
        TransformUnknown = false;
        Moved.Clear();
        ClearMemos = false;
    }
}

/// <summary>One component's recorded contribution: its identity + the draw commands its <c>Render</c> produced this
/// frame (empty = "reuse cached units" when it was clean, or "clear stale units" when it was dirty - disambiguated by
/// <see cref="WasGeometryValid"/>, exactly as the fused walk did) + its PAINT RANK, which is how the applier places the
/// group without ever reading the recorder's rank map across the seam.</summary>
internal readonly struct ComponentDraw(IUIComponent component, IReadOnlyList<IDrawCommand> commands, bool wasGeometryValid,
    long order, IReadOnlyList<Matrix4x4F> clones = null)
{
    public IUIComponent Component { get; } = component;
    public IReadOnlyList<IDrawCommand> Commands { get; } = commands;
    public bool WasGeometryValid { get; } = wasGeometryValid;

    /// <summary>The component's clone set, SNAPSHOT with the rest of its contribution. Read live off the component
    /// at draw time instead, it is written by layout on the loop thread while the render thread is drawing: the frame
    /// then paints a set that no longer matches the tiles recorded beside it, which showed up as skeletons trailing the
    /// scroll by a frame or two (measured: 154 frames of 357 emitted a different count than was declared).</summary>
    public IReadOnlyList<Matrix4x4F> Clones { get; } = clones;

    /// <summary>The component's paint rank (_groups is sorted by it).</summary>
    public long Order { get; } = order;
}
