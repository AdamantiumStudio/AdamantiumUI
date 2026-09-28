using System.Collections.Generic;
using System.Runtime.CompilerServices;
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
    // Which sources are worth watching. The invalidation events below fire for EVERY element in the application, so
    // the first thing the handler does is look at this count - with no visual brush on screen it is one read.
    private static readonly HashSet<IUIComponent> _sources = new();

    // Every brush painting from a given source, with the element that draws it (the one to tell about a new picture).
    // Weakly keyed: a brush no longer in use must not be kept alive, nor keep its source alive, by the fact that it
    // once asked for a picture.
    private static readonly ConditionalWeakTable<IUIComponent, List<(VisualBrush Brush, IUIComponent Owner)>> _brushes = new();

    // Sources whose picture is being taken right now - one at a time each, so a source that changes every frame does
    // not queue an off-screen render per frame.
    private static readonly HashSet<IUIComponent> _pending = new();

    // The last two pictures per source, owned here since all brushes of a source share them; the deferred queue is what
    // makes freeing safe, the extra generation is a margin.
    private static readonly ConditionalWeakTable<IUIComponent, BitmapSource> _current = new();
    private static readonly ConditionalWeakTable<IUIComponent, BitmapSource> _previous = new();

    // Sources with a wake already posted. Cleared as the wake runs, so the next change posts a fresh one.
    private static readonly HashSet<IUIComponent> _waking = new();

    private static bool _listening;

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
        Watch(visual, origin, owner);
        if (!origin.NeedsBake)
        {
            return;
        }

        // ONE picture per SOURCE, not per brush. Four shapes painting from one element is four fills, but it is still
        // one element - drawing it off-screen four times costs four render targets for four identical pictures. And
        // one in flight at a time: a source that changes every frame (a slider being dragged) would otherwise queue a
        // fresh off-screen render per frame, which is what lost the device.
        if (_pending.Contains(visual))
        {
            return;
        }

        var dispatcher = UIAppContext.Current?.Dispatcher;
        if (dispatcher == null)
        {
            return;
        }

        // Cleared only now that the request is really going out. Clearing it on the way to a DROPPED request threw the
        // change away: the source kept moving while a bake ran, and the fill stayed at whatever that bake had caught -
        // drag the source's slider back and the picture stood at the far end.
        origin.NeedsBake = false;
        _pending.Add(visual);
        dispatcher.Post(() => Take(visual));
    }

    // UI thread: tell everything painting from this source that the brush changed, so it re-records and asks again.
    private static void Wake(IUIComponent visual)
    {
        _waking.Remove(visual);
        if (!_brushes.TryGetValue(visual, out var list))
        {
            return;
        }

        foreach (var entry in list)
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
            _pending.Remove(visual);
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
            _pending.Remove(visual);
            return;
        }

        renderer.RequestRender(visual, size, 1.0, Colors.Transparent, image => Deliver(visual, image));
    }

    // Every brush painting from this source gets the one picture. Each owner is told to RE-RECORD, not to re-bake its
    // paint: a texture is asked for while the unit is ROUTED into a batch, and a paint invalidation never asks again -
    // so the very first picture would sit unused and the fill stay empty for ever.
    private static void Deliver(IUIComponent visual, ImageSource image)
    {
        _pending.Remove(visual);
        if (!_brushes.TryGetValue(visual, out var list))
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

        foreach (var entry in list)
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

    private static void Watch(IUIComponent visual, VisualBrush brush, IUIComponent owner)
    {
        var list = _brushes.GetValue(visual, static _ => []);
        if (!list.Exists(entry => ReferenceEquals(entry.Brush, brush) && ReferenceEquals(entry.Owner, owner)))
        {
            list.Add((brush, owner));
        }

        _sources.Add(visual);

        if (_listening)
        {
            return;
        }

        _listening = true;
        VisualTreeNotifications.ContentInvalidated += OnSourceChanged;
        VisualTreeNotifications.PaintInvalidated += OnSourceChanged;
    }

    // The element that changed is rarely the SOURCE itself - it is usually something inside it - so the walk goes up
    // from the change looking for a watched source. Bounded by the tree's depth, and only entered while some visual
    // brush exists at all.
    private static void OnSourceChanged(IUIComponent component)
    {
        if (_sources.Count == 0)
        {
            return;
        }

        for (var node = component; node != null; node = node.VisualParent)
        {
            if (!_sources.Contains(node) || !_brushes.TryGetValue(node, out var list))
            {
                continue;
            }

            foreach (var entry in list)
            {
                entry.Brush.NeedsBake = true;
            }

            // Fills are re-routed only by the brush's Changed; posted once per source, since raising it inside the render
            // walk would loop.
            if (_waking.Add(node))
            {
                UIAppContext.Current?.Dispatcher.Post(() => Wake(node));
            }
        }
    }
}
