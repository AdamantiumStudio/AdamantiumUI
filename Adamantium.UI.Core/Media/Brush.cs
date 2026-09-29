using System;
using Adamantium.Core.TypeParsing;
using Adamantium.UI.Core.Media.Animation;
using Adamantium.UI.Core.TypeParsers;

namespace Adamantium.UI.Core.Media;

[TypeParser(typeof(BrushParser))]
public abstract class Brush: AdamantiumComponent, IRenderAttachable
{
   // Set by RaiseChanged (a genuine property change), consumed by the compositor's per-frame RefreshBases. Lets a paint
   // animation re-capture its base ONLY when the brush actually changed (a theme recolor) instead of every loop frame -
   // so the render thread's dedup (see Compositor's paint tag) is not defeated by a base that is "re-captured" unchanged.
   // Loop-thread only (RaiseChanged and RefreshBases both run there); never touched by the render thread's PublishSnapshot.
   private bool _baseChanged = true;

   private bool _anchorConsidered;

   // How many render properties of each owner hold this brush, so attach/detach stay symmetric when one owner uses it
   // twice. Lazy; most brushes have one owner.
   private Dictionary<AdamantiumComponent, int> _owners;

   // The owner map is written from both the loop and render threads; a lock, since count updates and sweeps must be
   // atomic.
   private readonly object _ownersLock = new();

   // The immutable appearance the render path reads - see Snapshot.
   private volatile Brush _snapshot;

   private bool _isFrozen;

   // PAINT: a brush's own opacity changes only the color the units are baked with - never a shape, never a layout. Every
   // element painting with this brush re-bakes (via Changed -> InvalidatePaint); the flag STATES that, so an animation of
   // it can also be recognized as composited (run on the render thread) without the renderer keeping a hardcoded list of
   // "known" brush properties. The loading-skeleton pulse animates exactly this.
   public static readonly AdamantiumProperty OpacityProperty = AdamantiumProperty.Register(nameof(Opacity),
      typeof (Double), typeof (Brush), new PropertyMetadata(1.0, PropertyMetadataOptions.AffectsPaint));

   protected Brush()
   {
      // Any property change on the brush itself (Opacity here; Color on a SolidColorBrush; StartPoint/EndPoint on a
      // gradient) changes how it paints, so notify. A gradient also raises Changed for its stops (see GradientBrush).
      PropertyChanged += (_, _) => RaiseChanged();
   }

   /// <summary>Raised when the brush's appearance changes - a property here, or (for a gradient) a stop's Offset/Color.
   /// An element that draws with the brush subscribes to this and re-renders; see AdamantiumComponent's AffectsRender
   /// handling, which keeps the element hooked to whatever brush its render property currently holds. This is what lets
   /// an ANIMATED brush (e.g. a looping shimmer sweeping a gradient) repaint without the element polling.</summary>
   public event EventHandler Changed;

   public Double Opacity
   {
      get => GetValue<Double>(OpacityProperty);
      set
      {
         if (IsFrozen)
         {
            return;
         }

         SetValue(OpacityProperty, value);
      }
   }

   public bool IsFrozen => _isFrozen;

   /// <summary>Whether a theme's palette owns this brush. Only the theme may recolor it in place; an editor must put a
   /// new brush on the object instead.</summary>
   public bool IsShared { get; internal set; }

   /// <summary>The immutable snapshot of this brush's CURRENT appearance - what the bake/draw path reads. A frozen brush is
   /// its own snapshot. Null until the brush has been prepared for rendering, which every payload does in its constructor
   /// (see <see cref="ForRendering"/>), so a brush that can be drawn always has one.</summary>
   public Brush Snapshot => _isFrozen ? this : _snapshot;

   private static long _paintEpoch;

   /// <summary>How many times any brush was rewritten in place, so readers can skip scanning their brushes on frames
   /// where nothing repainted.</summary>
   public static long PaintEpoch => System.Threading.Interlocked.Read(ref _paintEpoch);

   /// <summary>How many times this brush was rewritten in place (see <see cref="RaiseChanged"/>); the render side compares
   /// it with the version it baked.</summary>
   public int PaintVersion { get; private set; }

   /// <summary>How many handlers are listening to <see cref="Changed"/>. This is what the owner counting exists to keep
   /// at one per owner, so it is what the test has to read - the hold count alone is satisfied by a broken attach that
   /// subscribes every time.</summary>
   internal int SubscriberCount
   {
      get
      {
         int owners;
         lock (_ownersLock) owners = _owners?.Count ?? 0;
         return (Changed?.GetInvocationList().Length ?? 0) + owners;
      }
   }

   /// <summary>Is this element in the owner map - the only thing this brush can tell when its color changes. An
   /// element that PAINTS with a brush and is not in it hears nothing, which is what left every inherited Foreground
   /// in the previous variant's color.</summary>
   internal bool IsOwnedBy(AdamantiumComponent component)
   {
      lock (_ownersLock) return _owners?.ContainsKey(component) == true;
   }

   protected void RaiseChanged()
   {
      // Re-PUBLISH the snapshot the render path reads (see Snapshot). Eagerly, here, on the thread that owns the brush -
      // a payload holds the LIVE brush and dereferences its current snapshot, so a stale one is a change that never
      // reaches the screen. Only for a brush that already has one: a clone being built inside CreateFrozenCore raises
      // this from its own initializer, and snapshotting THAT would recurse forever.
      if (_snapshot != null) _snapshot = CreateFrozenCore();
      PaintVersion++;
      System.Threading.Interlocked.Increment(ref _paintEpoch);
      _baseChanged = true;   // a real change to the brush's own values - the compositor re-captures its paint base on it

      // A wholesale discard happened since this brush last looked (see SweepGeneration). This is the moment it is worth
      // looking: a theme swap recolors every theme brush, so the ones that need sweeping are exactly the ones raising
      // this. One comparison on the hot path when there is nothing to do.
      if (_owners != null && _sweptGeneration != SweepGeneration) SweepOwnersOutOfTheTree();

      Changed?.Invoke(this, EventArgs.Empty);
      NotifyOwners();
   }

   // Owners are notified through the map, not Changed, whose add/remove is O(subscribers); Changed stays for a few
   // non-owner subscribers.
   private AdamantiumComponent[] _ownersSnapshot;

   private void NotifyOwners()
   {
      if (_owners == null || _owners.Count == 0) return;

      // Cached, because an animated brush raises this once a frame and an owner list must not be copied per raise.
      // Invalidated wherever the map changes; a handler that attaches or detaches during the walk therefore mutates the
      // map without disturbing the array being walked, which is what a snapshot is for.
      var owners = _ownersSnapshot;
      if (owners == null)
      {
         // Built under the lock, walked outside it: copying the keys is the one moment this must not race a writer,
         // while the walk itself is over an array nobody else can touch.
         lock (_ownersLock)
         {
            owners = new AdamantiumComponent[_owners.Count];
            _owners.Keys.CopyTo(owners, 0);
            _ownersSnapshot = owners;
         }
      }

      foreach (var owner in owners) owner.OnRenderValueChanged(this, EventArgs.Empty);
   }

   // TEMP (leak hunt): how many owner LINKS have ever been taken and given up. Their difference is how many elements the
   // live brushes are holding right now - the number that says whether a release path runs at all, which reasoning about
   // the code cannot.
   public static long LinksTaken, LinksGivenUp;

   // Weakly registered brushes that have had an owner, so a sweep can reach an old theme's idle brushes too.
   private static readonly List<WeakReference<Brush>> BrushesWithOwners = new();

   /// <summary>TEMP (leak hunt): brushes still listing a destroyed part as an owner or subscriber, and how many such
   /// entries there are.</summary>
   public static (int Brushes, int DeadOwners, int LiveBrushes) DeadOwnerCensus()
   {
      List<Brush> live;
      lock (BrushesWithOwners)
      {
         live = new List<Brush>(BrushesWithOwners.Count);
         foreach (var handle in BrushesWithOwners)
            if (handle.TryGetTarget(out var brush)) live.Add(brush);
      }

      int brushes = 0, dead = 0;
      foreach (var brush in live)
      {
         var here = 0;
         lock (brush._ownersLock)
         {
            if (brush._owners != null)
               foreach (var owner in brush._owners.Keys)
                  if (owner is FundamentalUIComponent { IsDiscarded: true }) here++;
         }

         // ...and the SUBSCRIBER LIST, which is a different thing from the owner map and can disagree with it. The map
         // came back clean while the graph showed this very event holding a destroyed part, so the map is not the
         // answer - the invocation list is.
         var handlers = brush.Changed?.GetInvocationList();
         if (handlers != null)
            foreach (var handler in handlers)
               if (handler.Target is FundamentalUIComponent { IsDiscarded: true }) here++;

         if (here > 0) { brushes++; dead += here; }
      }

      return (brushes, dead, live.Count);
   }

   /// <summary>Releases owners no longer in a tree from every brush that has owners; called once per theme swap after it
   /// settles.</summary>
   public static void SweepEveryBrush()
   {
      System.Threading.Interlocked.Increment(ref SweepGeneration);

      List<Brush> live;
      lock (BrushesWithOwners)
      {
         live = new List<Brush>(BrushesWithOwners.Count);
         for (var i = BrushesWithOwners.Count - 1; i >= 0; i--)
         {
            if (BrushesWithOwners[i].TryGetTarget(out var brush)) live.Add(brush);
            else BrushesWithOwners.RemoveAt(i);
         }
      }

      foreach (var brush in live) brush.SweepOwnersOutOfTheTree();
   }

   void IRenderAttachable.AttachTo(AdamantiumComponent owner)
   {
      var firstOwner = false;

      lock (_ownersLock)
      {
         if (_owners == null)
         {
            _owners = new Dictionary<AdamantiumComponent, int>();
            firstOwner = true;
         }

         if (_owners.TryGetValue(owner, out var held))
         {
            _owners[owner] = held + 1;   // already subscribed for this owner; another of its properties took the brush
         }
         else
         {
            _owners[owner] = 1;
            System.Threading.Interlocked.Increment(ref LinksTaken);
            _ownersSnapshot = null;   // the map is what notifies them now - see RaiseChanged
         }
      }

      // OUTSIDE the map lock, and that ordering is the point: the diagnostics take BrushesWithOwners and then read a
      // brush's map, so a path that held the map while reaching for the global list would be the other half of a
      // deadlock. Nobody holds both at once.
      if (firstOwner) lock (BrushesWithOwners) BrushesWithOwners.Add(new WeakReference<Brush>(this));

      Anchor(owner);

      // Sweep only after the attach completes: a template part being built is not in a tree yet and would be dropped
      // mid-attach.
      bool overdue;
      lock (_ownersLock) overdue = _owners.Count > _sweepAt;
      if (overdue) SweepOwnersOutOfTheTree();
   }

   void IRenderAttachable.DetachFrom(AdamantiumComponent owner)
   {
      lock (_ownersLock)
      {
         if (_owners == null || !_owners.TryGetValue(owner, out var held))
         {
            return;
         }

         if (held > 1)
         {
            _owners[owner] = held - 1;   // its other properties still draw with this brush
            return;
         }

         _owners.Remove(owner);
         System.Threading.Interlocked.Increment(ref LinksGivenUp);
         _ownersSnapshot = null;
      }
   }

   // A discarded owner never tells the brush, so the brush sweeps for owners no longer in a live tree, when the map has
   // doubled (amortized O(1)). Released owners re-take their brushes on attach.
   private int _sweepAt = 16;

   /// <summary>Bumped on wholesale discards such as a theme swap, forcing a sweep even when a brush's owner count did not
   /// grow.</summary>
   public static int SweepGeneration;

   private int _sweptGeneration = -1;

   private void SweepOwnersOutOfTheTree()
   {
      _sweptGeneration = SweepGeneration;
      List<AdamantiumComponent> gone = null;

      // Under the lock: this walks the map while the other thread may be attaching into it. The RELEASING below stays
      // outside - it calls back into the owners, and holding a brush's lock across foreign code is how a deadlock is
      // built.
      lock (_ownersLock)
      {
      foreach (var owner in _owners.Keys)
      {
         // Only elements are judged; parked ones are kept. Discarded counts as gone even if RootVisual is still set.
         if (owner is FundamentalUIComponent { IsDiscarded: true } ||
             owner is IUIComponent { IsAttachedToVisualTree: false, IsParked: false })
         {
            (gone ??= new List<AdamantiumComponent>()).Add(owner);
         }
      }

      // Collected first: releasing mutates the very map being walked.
      _sweepAt = Math.Max(16, _owners.Count * 2);
      }

      if (gone == null) return;

      foreach (var owner in gone)
      {
         // Release this owner here directly: the owner's own release walk would detach the new theme's brushes instead.
         bool removed;
         lock (_ownersLock)
         {
            removed = _owners.Remove(owner);
            if (removed) _ownersSnapshot = null;
         }
         if (removed) System.Threading.Interlocked.Increment(ref LinksGivenUp);

         // ...and the owner still gives up the rest of what it holds, which also arms its re-take on attach.
         owner.ReleaseRenderAttachments();
      }

      lock (_ownersLock) _sweepAt = Math.Max(16, _owners.Count * 2);
   }

   /// <summary>How many of <paramref name="owner"/>'s render properties currently hold this brush.</summary>
   internal int OwnerHoldCount(AdamantiumComponent owner)
   {
      lock (_ownersLock) return _owners != null && _owners.TryGetValue(owner, out var held) ? held : 0;
   }

   // Anchors the brush to its first owner so expressions on the brush can resolve; only brushes with pending expressions
   // are anchored.
   private void Anchor(AdamantiumComponent owner)
   {
      if (_anchorConsidered)
      {
         return;
      }

      _anchorConsidered = true;

      // TWO places to look: an EXPRESSION on one of the brush's properties, and a RESOURCE only a tree-scoped lookup can
      // answer. Asking about the first alone is how {ResourceReference} on a brush kept resolving to nothing.
      var hasExpressions = Data.BindingEngine.HasBindings(this);
      var hasResources = Resources.ResourceResolver.HasPending(this);
      if (!hasExpressions && !hasResources)
      {
         return;
      }

      InheritanceParent = owner;

      if (hasExpressions)
      {
         Data.BindingEngine.RefreshBindings(this);

         // The owner has no ancestors yet: refresh again on attach (for ElementName) and when its DataContext arrives.
         if (owner is IUIComponent visual)
         {
            visual.AttachedToVisualTreeEvent += (_, _) => Data.BindingEngine.RefreshBindings(this);
         }

         owner.PropertyChanged += (_, e) =>
         {
            if (e.Property == FundamentalUIComponent.DataContextProperty) Data.BindingEngine.RefreshBindings(this);
         };
      }

      if (hasResources)
      {
         Resources.ResourceResolver.Resolve(this, owner as IUIComponent);
      }
   }

   /// <summary>Returns whether the brush's own values changed since the last call, and clears the flag. Loop thread.</summary>
   public bool ConsumeBaseChange()
   {
      var changed = _baseChanged;
      _baseChanged = false;
      return changed;
   }

   // --- Frozen snapshot (render/compositor-thread safety) -------------------------------------------------------------
   // The render thread reads an immutable frozen clone of the same type, published by the writer per change. Payloads hold
   // the live brush and read Snapshot each time, so animated brushes repaint.

   /// <summary>Prepare this brush to be drawn: publish a snapshot for the render thread, and hand back the LIVE brush -
   /// which is what a payload stores, so later changes stay visible through <see cref="Snapshot"/>. Called on the thread
   /// that owns the brush (recording), never on the render thread.</summary>
   public Brush ForRendering()
   {
      if (!_isFrozen) _snapshot ??= CreateFrozenCore();
      return this;
   }

   private Brush CreateFrozenCore() => AsFrozen(CreateClone());

   /// <summary>A live, editable copy of this brush, so recoloring the copy does not affect other users of the
   /// original.</summary>
   public Brush Copy() => CreateClone();

   /// <summary>A fresh, UNFROZEN clone of this brush's current values (same runtime type). Subclasses copy their own
   /// properties; the base freezes it. Split out from freezing so the compositor can override an animated value on the
   /// clone BEFORE it is frozen (see <see cref="BuildAnimatedSnapshot"/>).</summary>
   protected abstract Brush CreateClone();

   // --- Composited paint (render-thread animation) ------------------------------------------------------------------
   /// <summary>A frozen clone with the curve's paint tracks applied, built on the render thread from a frozen base; works
   /// for any double paint property.</summary>
   public Brush BuildAnimatedSnapshot(AnimationCurve curve, double elapsed)
   {
      var clone = CreateClone();
      foreach (var track in curve.Tracks)
         clone.SetValue(track.Property, curve.Evaluate(track, elapsed));
      return AsFrozen(clone);
   }

   /// <summary>Steps per unit below which a change of <paramref name="property"/> is invisible, for paint dedup: 256 for
   /// 8-bit opacity, 4096 for geometric values.</summary>
   public virtual double PaintQuantum(AdamantiumProperty property) => property == OpacityProperty ? 256.0 : 4096.0;

   /// <summary>A frozen clone of this brush's CURRENT (live, base) values - what the compositor captures on the loop thread
   /// as the base its animated snapshots are built from. Distinct from <see cref="Snapshot"/>, which the compositor itself
   /// overwrites while it animates: the base must stay the brush's own values so a theme recolor flows through, and reading
   /// Snapshot for it would feed the animated value back in and spiral.</summary>
   public Brush CaptureBase() => AsFrozen(CreateClone());

   /// <summary>Swap in the snapshot the render path reads. The compositor calls this from the render thread; it is a single
   /// volatile reference write, so a reader always sees one whole, self-consistent brush. See <see cref="Snapshot"/>.</summary>
   public void PublishSnapshot(Brush snapshot) => _snapshot = snapshot;

   // Stamp a freshly-constructed clone immutable. Construction (ctor + object initializer) runs with the setters OPEN;
   // this closes them afterwards (each subclass setter early-returns when IsFrozen), so the clone can never change.
   protected static T AsFrozen<T>(T clone) where T : Brush
   {
      clone._isFrozen = true;
      return clone;
   }
}
