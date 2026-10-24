using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using Adamantium.Mathematics;

namespace Adamantium.UI.Core.Media.Animation;

/// <summary>Animations the render thread plays itself, so they keep running while the loop thread stalls. One clock, owned
/// by the render thread; transform values are mirrored back for hit-testing, paint values are not.</summary>
/// <remarks>The render thread reads only an immutable <see cref="Basis"/>; a taken-over element becomes a motion node, so
/// moving it is one matrix write (see <see cref="TryTakeOver"/>).</remarks>
public static class Compositor
{
    /// <summary>Everything the matrix needs that the curve does NOT animate - the element's base transform values, its layout
    /// position, and the size the render-transform origin resolves against. Immutable, published by the loop thread whenever
    /// layout changes; the render thread only ever reads the reference.
    ///
    /// While the loop thread is stalled this cannot go stale, because nothing can change it: no loop, no layout.</summary>
    public sealed class Basis
    {
        public Basis(TransformValues values, Vector2 boundsLocation, Vector2 origin, Size renderSize)
        {
            Values = values;
            BoundsLocation = boundsLocation;
            Origin = origin;
            RenderSize = renderSize;
        }

        public TransformValues Values { get; }
        public Vector2 BoundsLocation { get; }
        public Vector2 Origin { get; }
        public Size RenderSize { get; }
    }

    /// <summary>One animation the render thread plays - a Transform (one element, one matrix) or a Paint brush (one shared
    /// brush, many elements). The two share a clock and a curve; how the value is APPLIED differs, so <see cref="Channel"/>
    /// selects between the transform-only and paint-only state.</summary>
    public sealed class Entry
    {
        // Transform: one element moved by one matrix write.
        private Entry(Transform target, IUIComponent owner, AnimationCurve curve, Basis basis, double resumeElapsed)
        {
            Target = target;
            Channel = CompositorChannel.Transform;
            Owner = owner;
            Curve = curve;
            _basis = basis;
            _resumeElapsed = resumeElapsed;
            Local = ComposeLocal(basis, curve, Elapsed);
        }

        // Paint: one shared brush whose animated snapshot the render thread republishes each present.
        private Entry(Brush target, AnimationCurve curve, Brush paintBase, double resumeElapsed)
        {
            Target = target;
            Channel = CompositorChannel.Paint;
            Curve = curve;
            _paintBase = paintBase;
            _resumeElapsed = resumeElapsed;
        }

        // Opacity: one element whose alpha the render thread writes into its opacity slot. No basis and no snapshot - the
        // curve's value IS the applied state, and everything under the element composes it at draw time.
        private Entry(IUIComponent owner, AnimationCurve curve, double resumeElapsed)
        {
            Target = (AdamantiumComponent)owner;
            Channel = CompositorChannel.Opacity;
            Owner = owner;
            Curve = curve;
            _resumeElapsed = resumeElapsed;
            Alpha = (float)curve.Evaluate(curve.Tracks[0], Elapsed);
        }

        // resumeElapsed carries a re-templated animation's phase across the target swap, so a spinner picks up where the old
        // one was instead of snapping to the start (the theme-swap stutter). 0 for a fresh start.
        internal static Entry ForTransform(Transform t, IUIComponent owner, AnimationCurve curve, Basis basis, double resumeElapsed = 0) => new(t, owner, curve, basis, resumeElapsed);
        internal static Entry ForPaint(Brush b, AnimationCurve curve, Brush paintBase, double resumeElapsed = 0) => new(b, curve, paintBase, resumeElapsed);
        internal static Entry ForOpacity(IUIComponent owner, AnimationCurve curve, double resumeElapsed = 0) => new(owner, curve, resumeElapsed);

        /// <summary>The element's alpha at the last <see cref="Recompose"/> - what the render thread writes to its slot.</summary>
        public float Alpha { get; private set; }

        /// <summary>The element's alpha <paramref name="lag"/> seconds behind right now - what a copy of it drawn that much
        /// later shows (see IUIComponent.RenderCloneStagger). An endless curve is read as if it had always been playing;
        /// a finite one holds its start until the lag has passed.</summary>
        public float AlphaAt(double lag)
        {
            var active = Elapsed - lag - Curve.Delay;
            var period = Curve.Period;
            if (active < 0 && Curve.IsEndless && period > 0)
            {
                active = active % period + period;
            }

            return (float)Curve.Evaluate(Curve.Tracks[0], active + Curve.Delay);
        }

        public AdamantiumComponent Target { get; }
        public CompositorChannel Channel { get; }
        public AnimationCurve Curve { get; }

        // Transform-only.
        public IUIComponent Owner { get; }

        /// <summary>True when the compositor itself flipped Owner into a motion node for THIS animation (it was a plain
        /// world-baked element). Release then flips it back - a node that was ALREADY a motion node (FlipTile, a scroll
        /// viewport) has this false and is left promoted. Carried across a re-animation of the same target.</summary>
        internal bool Promoted { get; set; }

        internal long TakenInFrame { get; set; }

        internal long RetiredInFrame { get; set; }

        private volatile Basis _basis;
        public Matrix4x4F Local { get; private set; }

        // Paint-only: the brush's own (un-animated) values, refreshed on the loop thread. The render thread builds each
        // present's snapshot from it and never reads the live brush.
        private volatile Brush _paintBase;

        // Paint-only: a quantized fingerprint of the last published values, so a present whose baked bytes would be identical
        // rebuilds and re-bakes NOTHING. The render thread presents far faster than an animation changes a pixel (a 1.2s
        // opacity pulse crosses only tens of alpha bytes), so without this a shared brush would rebuild its snapshot and
        // re-bake all its slots ~1000x/second to no visible effect. The quantum is per-property (see Brush.PaintQuantum).
        private long _lastPaintTag = long.MinValue;

        /// <summary>Did the LAST <see cref="Recompose"/> actually change the paint (so the render cache must re-bake the
        /// brush's slots)? Meaningful only for a Paint entry.</summary>
        public bool PaintChanged { get; private set; }

        private readonly double _resumeElapsed;
        private long _startTimestamp;

        /// <summary>Seconds since this animation began - read straight off the monotonic clock, not accumulated. Both threads
        /// therefore get the same answer for the same instant, no matter who asks, how often, or in what order: there is
        /// nothing to keep in step because there is no counter. (Accumulating a delta would need every reader to agree on who
        /// advances it - and would drift the moment two did.) Until <see cref="Start"/>, it holds where it starts from.</summary>
        public double Elapsed
        {
            get
            {
                var start = Volatile.Read(ref _startTimestamp);
                return start == 0 ? _resumeElapsed : (Stopwatch.GetTimestamp() - start) / (double)Stopwatch.Frequency;
            }
        }

        internal void Start()
        {
            if (Volatile.Read(ref _startTimestamp) == 0)
            {
                Volatile.Write(ref _startTimestamp, Stopwatch.GetTimestamp() - (long)(_resumeElapsed * Stopwatch.Frequency));
            }
        }

        // When the render thread last applied this transform; if stale, the loop forces a re-record. A torn read is
        // corrected next frame.
        private long _lastAppliedTs;
        internal void MarkApplied() => _lastAppliedTs = Stopwatch.GetTimestamp();
        public bool AppliedRecently => _lastAppliedTs != 0 && Stopwatch.GetTimestamp() - _lastAppliedTs < Stopwatch.Frequency / 20;   // ~50 ms

        public Basis CurrentBasis
        {
            get => _basis;
            internal set => _basis = value;
        }

        internal Brush PaintBase
        {
            get => _paintBase;
            set { _paintBase = value; _lastPaintTag = long.MinValue; }   // a new base -> force one republish so a recolor shows
        }

        /// <summary>Recompute this animation's applied state for RIGHT NOW. Idempotent - calling it twice in a frame is
        /// harmless. Opacity: the alpha. Paint: build and publish the brush snapshot (the render cache then re-bakes the
        /// slots that read it). A transform's matrix is folded from all its animations by <see cref="Tick"/>.</summary>
        internal void Recompose()
        {
            if (Channel == CompositorChannel.Opacity)
            {
                Alpha = (float)Curve.Evaluate(Curve.Tracks[0], Elapsed);
                return;
            }

            // Paint: only rebuild + republish when the quantized values actually moved (see _lastPaintTag).
            var brush = (Brush)Target;
            var elapsed = Elapsed;
            var tag = PaintTag(brush, Curve, elapsed);
            PaintChanged = tag != _lastPaintTag;
            if (!PaintChanged) return;
            _lastPaintTag = tag;
            brush.PublishSnapshot(_paintBase.BuildAnimatedSnapshot(Curve, elapsed));
        }

        // A fingerprint of the curve's values at this instant, each quantized to ITS property's visible resolution (see
        // Brush.PaintQuantum) - two instants with the same fingerprint bake byte-identical, so the second needs no work.
        // Per-property because opacity's exact quantum (8-bit) would visibly step a geometric one, and the fine geometric
        // quantum would re-bake a shared opacity brush far more than its 8-bit output can show. Order-dependent combine.
        private static long PaintTag(Brush brush, AnimationCurve curve, double elapsed)
        {
            long tag = 17;
            foreach (var track in curve.Tracks)
                tag = tag * 31 + (long)(curve.Evaluate(track, elapsed) * brush.PaintQuantum(track.Property));
            return tag;
        }

        // The same composition UIComponent.LocalTransform does - the render transform applied around the origin, then the
        // layout offset - but built from captured data instead of live properties.
        private static Matrix4x4F ComposeLocal(Basis basis, AnimationCurve curve, double elapsed)
        {
            var values = basis.Values;
            Apply(ref values, curve, elapsed);
            return ToLocal(basis, values);
        }

        internal void Fold(ref TransformValues values) => Apply(ref values, Curve, Elapsed);

        internal void Place(Basis basis, TransformValues values) => Local = ToLocal(basis, values);

        private static void Apply(ref TransformValues values, AnimationCurve curve, double elapsed)
        {
            foreach (var track in curve.Tracks)
            {
                values.Set(track.Property, curve.Evaluate(track, elapsed));
            }
        }

        private static Matrix4x4F ToLocal(Basis basis, TransformValues values)
        {
            var matrix = (Matrix4x4F)values.ToMatrix();

            var origin = basis.Origin;
            if (origin.X != 0 || origin.Y != 0)
            {
                var ox = (float)(origin.X * basis.RenderSize.Width);
                var oy = (float)(origin.Y * basis.RenderSize.Height);
                matrix = Matrix4x4F.Translation(-ox, -oy, 0) * matrix * Matrix4x4F.Translation(ox, oy, 0);
            }

            if (values.ShiftX != 0 || values.ShiftY != 0)
            {
                matrix *= Matrix4x4F.Translation((float)(values.ShiftX * basis.RenderSize.Width),
                    (float)(values.ShiftY * basis.RenderSize.Height), 0);
            }

            return matrix * Matrix4x4F.Translation((float)basis.BoundsLocation.X, (float)basis.BoundsLocation.Y, 0);
        }
    }

    // Guarded by itself: the loop thread adds/removes, the render thread iterates. Both are rare-to-modest operations (an
    // animation starting is not a per-frame event), so a plain lock is right - a lock-free structure here would buy nothing
    // and cost the ability to reason about it.
    private static readonly object Gate = new();
    private static readonly List<Entry> Entries = new();
    private static readonly List<Entry> Retiring = new();
    private static int _opacityEntries;
    private static long _frame;
    private static long _recorded;
    private static long _recordedFrame = -1;

    // A record this many loop frames older than the newest is never drawn again, so what it alone still played can go.
    private const long StaleRecordFrames = 600;

    /// <summary>Whether the render thread plays an animation in <paramref name="root"/>'s tree, or of a brush, which any
    /// tree may paint with.</summary>
    public static bool IsPlayingIn(IRootVisualComponent root)
    {
        lock (Gate)
        {
            return PlaysIn(Entries, root) || PlaysIn(Retiring, root);
        }
    }

    private static bool PlaysIn(List<Entry> entries, IRootVisualComponent root)
    {
        foreach (var entry in entries)
        {
            if (entry.Owner == null || ReferenceEquals(entry.Owner.RootVisual, root))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Whether an animation waits for a record to be drawn: one taken over since the last record does not play
    /// before it, one released plays on until it.</summary>
    public static bool NeedsRecord
    {
        get
        {
            var recorded = Interlocked.Read(ref _recordedFrame);
            lock (Gate)
            {
                if (Retiring.Count > 0)
                {
                    return true;
                }

                foreach (var entry in Entries)
                {
                    if (entry.TakenInFrame > recorded)
                    {
                        return true;
                    }
                }

                return false;
            }
        }
    }

    /// <summary>The loop frame being built. A render packet carries the number of the frame that recorded it.</summary>
    public static long Frame => Interlocked.Read(ref _frame);

    /// <summary>How many loop frames have been recorded and handed to the render thread; 0 under a host that records none.</summary>
    public static long Recorded => Interlocked.Read(ref _recorded);

    /// <summary>Ends the loop frame. Loop thread, once the frame is recorded.</summary>
    public static void EndFrame()
    {
        var frame = Interlocked.Increment(ref _frame);
        lock (Gate)
        {
            for (var i = Retiring.Count - 1; i >= 0; i--)
            {
                if (Retiring[i].RetiredInFrame < frame - StaleRecordFrames) Retiring.RemoveAt(i);
            }
        }
    }

    /// <summary>The loop frame has been recorded and handed to the render thread. Loop thread.</summary>
    public static void FrameRecorded()
    {
        Interlocked.Exchange(ref _recordedFrame, Frame);
        Interlocked.Increment(ref _recorded);
    }

    /// <summary>Whether a frame recorded after <paramref name="recorded"/> was read has been handed to the render thread -
    /// what has to happen before the loop thread starts work that holds it, so what it put up first is on screen. Under a
    /// host that records nothing, two updates after <paramref name="ticks"/> stand for it.</summary>
    public static bool RecordedSince(long recorded, long ticks) =>
        Recorded > 0 ? Recorded > recorded : AnimationManager.Ticks >= ticks + 2;

    /// <summary>Hand an animation to the render thread, if it can play it. Loop thread, when the animation starts. Returns
    /// null when it cannot - the caller then keeps the animation on the loop thread, as before.</summary>
    public static Entry TryTakeOver(AdamantiumComponent target, AnimationCurve curve, double resumeElapsed = 0)
    {
        var channel = AnimationChannels.Of(target, curve);
        Entry entry;
        switch (channel)
        {
            case CompositorChannel.Transform:
            {
                if (target is not Transform transform || transform.Owner is not { } owner || transform.IsLayoutTransform
                    || owner.LayoutTransform != null)
                {
                    return null;
                }

                // A world-baked element cannot be moved by a matrix write. Promote it - and mark it structural, because its
                // subtree's instances must be re-baked in the node's space before the slot means anything. Remember WE did
                // it (Promoted) so Release un-promotes it; a node that was already a motion node is left as it was.
                var promoted = !owner.IsRenderMotionNode;
                if (promoted)
                {
                    owner.IsRenderMotionNode = true;
                    RenderDirty.MarkStructural(owner);
                }
                entry = Entry.ForTransform(transform, owner, curve, CaptureBasis(transform, owner), resumeElapsed);
                entry.Promoted = promoted;
                break;
            }
            case CompositorChannel.Paint:
            {
                // A shared brush whose color/opacity/geometry animates. No element to promote and no matrix: the render
                // thread republishes the brush's snapshot each present, and the slots painting with it are re-baked from it.
                // Any double paint property works - BuildAnimatedSnapshot applies it generically, and AffectsPaint (which the
                // channel already required) guarantees a re-bake suffices.
                if (target is not Brush brush || brush.IsBakedIn) return null;
                entry = Entry.ForPaint(brush, curve, brush.CaptureBase(), resumeElapsed);
                break;
            }
            case CompositorChannel.Opacity:
            {
                // The element's own alpha. Nothing to promote and nothing to capture: the value goes into the element's
                // opacity slot, and every instance beneath it already carries that slot's index - which is what makes
                // this the twin of Transform, one write for a whole subtree.
                if (target is not IUIComponent owner) return null;
                entry = Entry.ForOpacity(owner, curve, resumeElapsed);
                break;
            }
            default:
                return null;
        }

        lock (Gate)
        {
            // Re-animating the same target replaces it; carry a running compositor-promotion across the swap so it isn't
            // lost (the node stays a motion node, and the LAST Release un-promotes it).
            for (var i = Entries.Count - 1; i >= 0; i--)
            {
                var e = Entries[i];
                if (!ReferenceEquals(e.Target, target)
                    || (entry.Channel == CompositorChannel.Transform && !SharesAProperty(e.Curve, curve)))
                {
                    continue;
                }

                if (e.Promoted)
                {
                    entry.Promoted = true;
                }

                if (e.Channel == CompositorChannel.Opacity)
                {
                    _opacityEntries--;
                }

                Entries.RemoveAt(i);
                e.RetiredInFrame = Frame;
                Retiring.Add(e);
            }

            entry.TakenInFrame = Frame;
            Entries.Add(entry);
            if (entry.Channel == CompositorChannel.Opacity)
            {
                _opacityEntries++;
            }
        }
        return entry;
    }

    /// <summary>Stop compositing the one animation <paramref name="entry"/> plays (it was canceled or finished), after its
    /// last value was written to the property. Loop thread.</summary>
    public static void Release(Entry entry)
    {
        lock (Gate)
        {
            if (!Entries.Remove(entry))
            {
                return;
            }

            entry.RetiredInFrame = Frame;
            Retiring.Add(entry);
            if (entry.Channel == CompositorChannel.Opacity)
            {
                _opacityEntries--;
            }

            if (entry.Owner is not { } owner)
            {
                return;
            }

            if (entry.Channel == CompositorChannel.Opacity)
            {
                owner.InvalidatePaint();
                return;
            }

            if (entry.Channel != CompositorChannel.Transform)
            {
                return;
            }

            if (Entries.Find(e => ReferenceEquals(e.Target, entry.Target)) is { } sibling)
            {
                sibling.Promoted |= entry.Promoted;
                return;
            }

            if (entry.Promoted)
            {
                owner.IsRenderMotionNode = false;
                RenderDirty.MarkStructural(owner);
            }
            else
            {
                RenderDirty.MarkNodeTransform(owner);
            }
        }
    }

    private static bool SharesAProperty(AnimationCurve a, AnimationCurve b)
    {
        foreach (var track in b.Tracks)
        {
            if (Animates(a, track.Property))
            {
                return true;
            }
        }

        return false;
    }

    private static bool Animates(AnimationCurve curve, AdamantiumProperty property)
    {
        foreach (var track in curve.Tracks)
        {
            if (track.Property == property)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Starts the clock of every animation taken over since the last call. Loop thread, once a frame is recorded:
    /// the frame that builds what an animation moves then does not spend its duration before any of it is shown.</summary>
    public static void StartPending()
    {
        lock (Gate)
        {
            foreach (var entry in Entries)
            {
                entry.Start();
            }
        }
    }

    public static void Reset()
    {
        lock (Gate)
        {
            Entries.Clear();
            Retiring.Clear();
            _opacityEntries = 0;
        }
    }

    /// <summary>Whether <paramref name="property"/> is <paramref name="target"/>'s own opacity and the render thread is
    /// drawing it right now, so the loop thread's mirrored write of it needs no re-paint. Loop thread.</summary>
    public static bool DrawsOpacity(AdamantiumComponent target, AdamantiumProperty property)
    {
        if (System.Threading.Volatile.Read(ref _opacityEntries) == 0
            || AnimationChannels.Of(target, property) != CompositorChannel.Opacity)
        {
            return false;
        }

        lock (Gate)
        {
            foreach (var entry in Entries)
            {
                if (entry.Channel == CompositorChannel.Opacity && ReferenceEquals(entry.Target, target))
                {
                    return entry.AppliedRecently;
                }
            }
        }

        return false;
    }

    internal static bool Mirrored(Transform transform)
    {
        var drawn = false;
        lock (Gate)
        {
            foreach (var entry in Entries)
            {
                if (ReferenceEquals(entry.Target, transform))
                {
                    entry.CurrentBasis = CaptureBasis(transform, entry.Owner);
                    drawn |= entry.AppliedRecently;
                }
            }
        }

        return drawn;
    }

    /// <summary>Re-capture every entry's base from the live tree. Loop thread, once per frame: layout may have moved or
    /// resized a transformed element, or a theme swap may have recolored an animating brush - the render thread reads only
    /// these captured bases, so this is where such changes reach it. Reading the live tree here is the loop thread's right.</summary>
    public static void RefreshBases()
    {
        lock (Gate)
        {
            foreach (var entry in Entries)
            {
                if (entry.Channel == CompositorChannel.Transform)
                {
                    entry.CurrentBasis = CaptureBasis((Transform)entry.Target, entry.Owner);
                }
                else if (entry.Channel == CompositorChannel.Paint && ((Brush)entry.Target).ConsumeBaseChange())
                {
                    // Only when the brush itself changed (a theme recolor) - not every frame, which would reset the dedup.
                    entry.PaintBase = ((Brush)entry.Target).CaptureBase();
                }
            }
        }
    }

    /// <summary>Recompose every composited animation for the instant it is called, into <paramref name="into"/> (the caller
    /// keeps and reuses that list, so a present allocates nothing). Called by the RENDER thread, once per present.
    /// Every animation of one transform lands in its one matrix. An animation plays only for a caller drawing a record made
    /// between its start and its stop; <paramref name="drawnFrame"/> is that record's frame. False = nothing is composited,
    /// and <paramref name="into"/> is empty.</summary>
    public static bool Tick(List<Entry> into, long drawnFrame)
    {
        into.Clear();
        lock (Gate)
        {
            for (var i = Retiring.Count - 1; i >= 0; i--)
            {
                if (Retiring[i].RetiredInFrame <= drawnFrame) Retiring.RemoveAt(i);
            }

            foreach (var entry in Entries)
            {
                if (entry.TakenInFrame <= drawnFrame) into.Add(entry);
            }

            foreach (var retiring in Retiring)
            {
                if (retiring.TakenInFrame <= drawnFrame) into.Add(retiring);
            }
        }

        if (into.Count == 0) return false;
        Recompose(into);
        return true;
    }

    /// <summary><see cref="Tick(List{Entry}, long)"/> for a caller drawing the newest record.</summary>
    public static bool Tick(List<Entry> into) => Tick(into, Frame);

    private static void Recompose(List<Entry> into)
    {
        var folded = _folded ??= new Dictionary<AdamantiumComponent, (Basis Basis, TransformValues Values)>();
        foreach (var entry in into)
        {
            if (entry.Channel != CompositorChannel.Transform)
            {
                entry.Recompose();
                continue;
            }

            if (!folded.TryGetValue(entry.Target, out var sofar))
            {
                var basis = entry.CurrentBasis;
                sofar = (basis, basis.Values);
            }

            entry.Fold(ref sofar.Values);
            folded[entry.Target] = sofar;
        }

        foreach (var entry in into)
        {
            if (entry.Channel == CompositorChannel.Transform)
            {
                var (basis, values) = folded[entry.Target];
                entry.Place(basis, values);
            }
        }

        folded.Clear();
    }

    [ThreadStatic]
    private static Dictionary<AdamantiumComponent, (Basis Basis, TransformValues Values)> _folded;

    /// <summary>Whether the render thread is playing this target; if so, the loop thread's mirror writes must not
    /// invalidate its render.</summary>
    public static bool Owns(AdamantiumComponent target)
    {
        lock (Gate)
        {
            foreach (var entry in Entries)
                if (ReferenceEquals(entry.Target, target))
                    return true;
            return false;
        }
    }

    /// <summary>Adds every element whose transform or opacity the render thread is playing to <paramref name="into"/>:
    /// what it draws with runs ahead of the element's own values by design.</summary>
    public static void CollectOwners(ISet<IUIComponent> into)
    {
        lock (Gate)
        {
            foreach (var entry in Entries)
            {
                if (entry.Owner != null)
                {
                    into.Add(entry.Owner);
                }
            }
        }
    }

    /// <summary>The entry playing <paramref name="target"/>, or null if the render thread isn't playing it.</summary>
    public static Entry EntryFor(AdamantiumComponent target)
    {
        lock (Gate)
        {
            foreach (var entry in Entries)
                if (ReferenceEquals(entry.Target, target))
                    return entry;
            return null;
        }
    }

    /// <summary>The entry playing <paramref name="channel"/> on <paramref name="target"/>, or null.</summary>
    public static Entry EntryFor(AdamantiumComponent target, CompositorChannel channel)
    {
        lock (Gate)
        {
            foreach (var entry in Entries)
            {
                if (entry.Channel == channel && ReferenceEquals(entry.Target, target))
                {
                    return entry;
                }
            }

            return null;
        }
    }

    private static Basis CaptureBasis(Transform transform, IUIComponent owner) =>
        new(transform.Values, owner.Bounds.Location, owner.RenderTransformOrigin, owner.RenderSize);
}
