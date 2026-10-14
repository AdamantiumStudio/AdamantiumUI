using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using Adamantium.Core.DependencyInjection;
using Adamantium.Mathematics;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Media;
using Adamantium.UI.Core.Media.Imaging;
using Adamantium.UI.Core.Rendering;

namespace Adamantium.UI.Rendering;

// Supplies a VisualBrush with an off-screen picture of its live source. Asked on the render thread, so it only queues
// (IVisualRenderer.RequestSnapshot); nothing is drawn until the picture lands.
internal static class VisualBrushRaster
{
    // What is known about each SOURCE - which elements are sources at all - kept WITH the source, weakly: a source thrown
    // away is not kept alive by having once been painted from. Two strong sets of sources stood here ("being taken",
    // "being woken"), and an entry left in one held a discarded source and the whole page above it.
    private static readonly ConditionalWeakTable<IUIComponent, Source> _sources = new();

    // The last two pictures per source, owned here since all brushes of a source share them; the deferred queue is what
    // makes freeing safe, the extra generation is a margin.
    private static readonly ConditionalWeakTable<IUIComponent, BitmapSource> _current = new();
    private static readonly ConditionalWeakTable<IUIComponent, BitmapSource> _previous = new();

    private static bool _listening;

    // Every brush painting from a source, with the element that draws it (the one to tell about a new picture). Filled on
    // the render thread and read on the loop thread, hence the lock. Taking: its picture is being taken right now - one
    // at a time, so a source that changes every frame does not queue an off-screen render per frame. Waking: a wake is
    // already posted, cleared as it runs.
    private sealed class Source
    {
        public readonly object Gate = new();
        public readonly List<(VisualBrush Brush, IUIComponent Owner)> Brushes = [];
        public int Taking;
        public int Waking;

        public (VisualBrush Brush, IUIComponent Owner)[] Snapshot()
        {
            lock (Gate)
            {
                return Brushes.ToArray();
            }
        }
    }

    private static IVisualRenderer _renderer;

    private static IVisualRenderer Renderer => _renderer ??= UIApplication.Current?.Container.Resolve<IVisualRenderer>();

    /// <summary>RENDER thread: make sure this brush has a picture, and a fresh one if its source has changed. Cheap to
    /// call every frame - a brush whose picture is current does nothing.</summary>
    public static void Ensure(VisualBrush brush, IUIComponent owner)
    {
        var origin = brush.Origin;
        var visual = origin.Visual;
        if (visual == null)
        {
            return;
        }

        // Registered even when the picture is current: a shape revealed later paints from a source that has long since
        // stopped announcing changes, and a brush nobody watches is never told about the next picture.
        var source = Watch(visual, origin, owner);
        if (!origin.NeedsBake)
        {
            return;
        }

        var dispatcher = UIAppContext.Current?.Dispatcher;
        if (dispatcher == null)
        {
            return;
        }

        // ONE picture per SOURCE, not per brush. Four shapes painting from one element is four fills, but it is still
        // one element - drawing it off-screen four times costs four render targets for four identical pictures. And
        // one in flight at a time: a source that changes every frame (a slider being dragged) would otherwise queue a
        // fresh off-screen render per frame, which is what lost the device.
        if (Interlocked.CompareExchange(ref source.Taking, 1, 0) != 0)
        {
            return;
        }

        // Cleared only now that the request is really going out. Clearing it on the way to a DROPPED request threw the
        // change away: the source kept moving while a bake ran, and the fill stayed at whatever that bake had caught -
        // drag the source's slider back and the picture stood at the far end.
        origin.NeedsBake = false;
        dispatcher.Post(() => Take(visual));
    }

    // Its picture is no longer being taken, whatever came of it.
    private static void Taken(IUIComponent visual)
    {
        if (_sources.TryGetValue(visual, out var source))
        {
            Volatile.Write(ref source.Taking, 0);
        }
    }

    // UI thread: tell everything painting from this source that the brush changed, so it re-records and asks again.
    private static void Wake(IUIComponent visual)
    {
        if (!_sources.TryGetValue(visual, out var source))
        {
            return;
        }

        Volatile.Write(ref source.Waking, 0);
        foreach (var entry in source.Snapshot())
        {
            if (entry.Brush.NeedsBake)
            {
                entry.Brush.Refresh();
            }
        }
    }

    // LOOP thread. A visual that is ON SCREEN is read where it is - RequestSnapshot walks it read-only and never
    // reparents it, which is the whole difficulty of painting with something that is also being displayed. One that is
    // detached has no bounds to be read at, so it is laid out at its own desired size instead.
    private static void Take(IUIComponent visual)
    {
        var renderer = Renderer;
        if (renderer == null)
        {
            Taken(visual);
            return;
        }

        if (visual.IsAttachedToVisualTree)
        {
            renderer.RequestSnapshot(visual, image => Deliver(visual, image));
            return;
        }

        var size = DetachedSize(visual);
        if (size.Width < 1 || size.Height < 1)
        {
            Taken(visual);
            return;
        }

        renderer.RequestRender(visual, size, 1.0, Colors.Transparent, image => Deliver(visual, image));
    }

    // Every brush painting from this source gets the one picture. Each owner is told to RE-RECORD, not to re-bake its
    // paint: a texture is asked for while the unit is ROUTED into a batch, and a paint invalidation never asks again -
    // so the very first picture would sit unused and the fill stay empty for ever.
    private static void Deliver(IUIComponent visual, ImageSource image)
    {
        Taken(visual);
        if (!_sources.TryGetValue(visual, out var source))
        {
            return;
        }

        if (image is not BitmapSource baked)
        {
            return;
        }

        if (_previous.TryGetValue(visual, out var stale))
        {
            _previous.Remove(visual);
            stale?.Dispose();
        }

        if (_current.TryGetValue(visual, out var previous))
        {
            _current.Remove(visual);
            if (previous != null)
            {
                _previous.Add(visual, previous);
            }
        }

        _current.Add(visual, baked);

        foreach (var entry in source.Snapshot())
        {
            entry.Brush.Deliver(baked);
            entry.Owner?.InvalidateRender(false);
        }
    }

    private static Size DetachedSize(IUIComponent visual)
    {
        if (visual is not IMeasurableComponent measurable)
        {
            return default;
        }

        measurable.Measure(new Size(4096, 4096));
        var desired = measurable.DesiredSize;
        return new Size(System.Math.Max(1, desired.Width), System.Math.Max(1, desired.Height));
    }

    private static Source Watch(IUIComponent visual, VisualBrush brush, IUIComponent owner)
    {
        var source = _sources.GetValue(visual, static _ => new Source());

        lock (source.Gate)
        {
            // An element that drew with this source and has been thrown away since is not told about pictures any more -
            // and left here it would live as long as the source does.
            source.Brushes.RemoveAll(static entry => entry.Owner is FundamentalUIComponent { IsDiscarded: true });

            if (!source.Brushes.Exists(entry => ReferenceEquals(entry.Brush, brush) && ReferenceEquals(entry.Owner, owner)))
            {
                source.Brushes.Add((brush, owner));
            }
        }

        if (_listening)
        {
            return source;
        }

        _listening = true;
        VisualTreeNotifications.ContentInvalidated += OnSourceChanged;
        VisualTreeNotifications.PaintInvalidated += OnSourceChanged;
        VisualTreeNotifications.PaintMirrored += OnSourceChanged;
        return source;
    }

    // The element that changed is rarely the SOURCE itself - it is usually something inside it - so the walk goes up
    // from the change looking for a watched source. Bounded by the tree's depth, and only listened to once some visual
    // brush has asked for a picture.
    private static void OnSourceChanged(IUIComponent component)
    {
        for (var node = component; node != null; node = node.VisualParent)
        {
            if (!_sources.TryGetValue(node, out var source))
            {
                continue;
            }

            foreach (var entry in source.Snapshot())
            {
                entry.Brush.NeedsBake = true;
            }

            // Fills are re-routed only by the brush's Changed; posted once per source, since raising it inside the render
            // walk would loop. Marked only once there is somewhere to post: a mark with no wake to clear it is for good.
            var dispatcher = UIAppContext.Current?.Dispatcher;
            if (dispatcher != null && Interlocked.CompareExchange(ref source.Waking, 1, 0) == 0)
            {
                var awake = node;
                dispatcher.Post(() => Wake(awake));
            }
        }
    }
}
