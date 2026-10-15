using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using Adamantium.Mathematics;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Media.Animation;

namespace Adamantium.UI.Rendering.Verification;

/// <summary>Checks one window's render cache against its tree. On each record it notes what the tree says about every
/// component in it; after the window draws that record, it compares what the cache drew with - the frozen layouts and
/// their parent chains, the composed world transforms, the groups in the paint order - and raises an alarm naming the
/// component, the field and both values when they part.</summary>
internal sealed class FrameVerifier : IRenderCacheObserver
{
    private const int Idle = 0;
    private const int Armed = 1;
    private const int Matched = 2;
    private const int Spoiled = 3;

    private const int AlarmsPerFrame = 40;
    private const float Tolerance = 0.01f;

    private readonly FrameVerification _settings;
    private readonly IRootVisualComponent _root;
    private readonly Dictionary<IUIComponent, TreeFact> _facts = new();
    private readonly Stack<(IUIComponent Node, bool Hidden)> _stack = new();
    private readonly HashSet<IUIComponent> _checked = [];
    private readonly List<string> _alarms = [];
    private readonly HashSet<IUIComponent> _animated = [];
    private readonly HashSet<IUIComponent> _animatedBefore = [];
    private readonly HashSet<IUIComponent> _cloned = [];
    private readonly Dictionary<Guid, IUIComponent> _byRenderId = new();
    private readonly List<(int First, int Count, IUIComponent Component)> _runs = [];
    private readonly HashSet<Core.Graphics.IRenderUnit> _textWithRuns = [];
    private readonly Dictionary<IUIComponent, int> _lastFillOp = new();

    private int _state;
    private int _alarmCount;
    private long _frame;
    private int _recorded;
    private RenderPacket _armedPacket;
    private RecordSummary _summary;
    private string _lastAlarms = string.Empty;

    public FrameVerifier(FrameVerification settings, IRootVisualComponent root)
    {
        _settings = settings;
        _root = root;
    }

    /// <summary>The loop records faster than the window draws, so the facts follow the newest record until the window
    /// applies it, and are kept from then on for that frame's check.</summary>
    public void Recorded(RenderPacket packet)
    {
        _frame++;
        var state = Volatile.Read(ref _state);
        if (state >= Matched || ++_recorded % Math.Max(1, _settings.Every) != 0)
        {
            return;
        }

        if (state == Armed && Interlocked.Exchange(ref _armedPacket, null) == null)
        {
            return;
        }

        try
        {
            _summary = new RecordSummary(_frame, packet);
            NoteTheTree();
            Volatile.Write(ref _armedPacket, packet);
            Volatile.Write(ref _state, Armed);
        }
        catch (Exception e)
        {
            Volatile.Write(ref _state, Idle);
            _settings.CountSkipped();
            _settings.Log($"frame {_frame}: the tree could not be noted - {e}");
        }
    }

    /// <summary>Packets are pooled, so the noted packet is the first application of that object after it was recorded;
    /// anything applied after it means the frame drew a later record than the facts describe.</summary>
    public void Applied(RenderPacket packet)
    {
        var state = Volatile.Read(ref _state);
        if (state == Matched)
        {
            Volatile.Write(ref _state, Spoiled);
        }
        else if (state == Armed && Interlocked.CompareExchange(ref _armedPacket, null, packet) == packet)
        {
            Volatile.Write(ref _state, Matched);
        }
    }

    /// <summary>Checks what the window just drew against the facts noted at its record, on the drawing thread.</summary>
    public void FrameDrawn(RenderCache live)
    {
        var state = Volatile.Read(ref _state);
        if (state < Matched)
        {
            return;
        }

        try
        {
            if (state == Spoiled)
            {
                _settings.CountSkipped();
            }
            else if (live.LastFrameWithheld)
            {
                _settings.CountSkipped();
            }
            else
            {
                Check(live);
            }
        }
        catch (Exception e)
        {
            _settings.CountSkipped();
            _settings.Log($"frame {_summary.Frame}: the check failed - {e}");
        }
        finally
        {
            Volatile.Write(ref _state, Idle);
        }
    }

    private void NoteTheTree()
    {
        _facts.Clear();
        _byRenderId.Clear();
        _stack.Clear();

        // What the render thread played up to the last record is still its own in this one: the release that hands the
        // value back marks the node for the NEXT record, so the record in which an animation ends draws its last played
        // value under the tree's final one. Held for one record only - a value still stale after that is caught.
        _animated.Clear();
        _animated.UnionWith(_animatedBefore);
        _animatedBefore.Clear();
        Compositor.CollectOwners(_animatedBefore);
        _animated.UnionWith(_animatedBefore);
        _stack.Push((_root, false));

        while (_stack.Count > 0)
        {
            var (component, hiddenByAncestor) = _stack.Pop();
            if (component.Visibility == Visibility.Collapsed || _facts.ContainsKey(component))
            {
                continue;
            }

            var hidden = hiddenByAncestor || component.Visibility != Visibility.Visible;
            var local = component.LocalTransform;
            var parent = component.RenderParent;
            var animated = _animated.Contains(component);
            Matrix4x4F world;
            var carried = animated;
            if (parent != null && _facts.TryGetValue(parent, out var above))
            {
                world = local * above.World;
                carried |= above.Carried;
            }
            else
            {
                world = component.WorldTransform;
            }

            _facts[component] = new TreeFact(local, component.RenderSize, component.ClipToBounds, parent,
                (float)component.Opacity, world, animated, carried, component.RenderClones?.Count ?? 0, hidden,
                (float)component.SelfOpacity,
                component is Controls.Text.TextBlock { Foreground: Core.Media.SolidColorBrush foreground }
                    ? foreground.Color.ToVector4()
                    : null,
                !component.IsGeometryValid);
            _byRenderId[component.RenderId] = component;
            RenderCache.PushChildrenInPaintOrder(_stack, component.VisualChildren, hidden);
        }
    }

    private void Check(RenderCache live)
    {
        _alarms.Clear();
        _alarmCount = 0;
        _checked.Clear();

        foreach (var group in live.DescribeGroups())
        {
            var component = group.Component;
            if (!_facts.TryGetValue(component, out var fact))
            {
                Alarm($"{ComponentText.Of(component)}: in the paint order ({group.Units} units, {group.Clones} copies), but not in the tree" +
                      $" - {((component as Core.FundamentalUIComponent)?.Lifecycle.ToString() ?? "?")}, " +
                      $"{(component.IsAttachedToVisualTree ? "attached" : "detached")}, " +
                      $"{(component.Visibility == Visibility.Collapsed ? "collapsed" : "visible")}, parent {ComponentText.Of(component.VisualParent)}");
                continue;
            }

            CheckChain(live, component);

            if (!fact.Carried && live.TryGetComposedWorld(component, out var world) && !SamePlace(world, fact.World))
            {
                Alarm($"{ComponentText.Of(component)}: drawn at {Place(world)}, the tree has it at {Place(fact.World)}");
            }

            _cloned.Add(component);
            if (!fact.Hidden && group.Clones != fact.Clones)
            {
                Alarm($"{ComponentText.Of(component)}: drawn as {group.Clones} copies, the tree asks for {fact.Clones}");
            }
        }

        foreach (var (component, fact) in _facts)
        {
            if (fact.Clones > 0 && !fact.Hidden && !_cloned.Contains(component))
            {
                Alarm($"{ComponentText.Of(component)}: the tree asks for {fact.Clones} copies, the cache has no group for it");
            }
        }

        _cloned.Clear();

        CheckShaderInputs(live);

        _settings.CountVerified();
        if (_alarms.Count == 0)
        {
            _lastAlarms = string.Empty;
            return;
        }

        _settings.CountMismatched();
        var alarms = string.Join(Environment.NewLine, _alarms);
        if (alarms == _lastAlarms)
        {
            return;
        }

        _lastAlarms = alarms;
        if (_settings.TakeReport())
        {
            var report = Report(live);
            _settings.Log(report);

            // An error, not only a line in the file: a driven run fails the step after which the frame was drawn.
            Serilog.Log.Error("Frame verifier: {Report}", report);
        }
    }

    private void CheckChain(RenderCache live, IUIComponent component)
    {
        for (var link = component; link != null && _checked.Add(link);)
        {
            if (!live.TryGetFrozen(link, out var frozen))
            {
                Alarm($"{ComponentText.Of(link)}: drawn, but the cache holds no layout for it - on the chain of {ComponentText.Of(component)}, " +
                      $"{((link as Core.FundamentalUIComponent)?.Lifecycle.ToString() ?? "?")}, " +
                      $"{(link.IsAttachedToVisualTree ? "attached" : "detached")}, in the tree {_facts.ContainsKey(link)}, parent {ComponentText.Of(link.VisualParent)}");
                return;
            }

            var readLive = live.IsReadLive(link) ? " (read live: no packet carried it)" : string.Empty;
            if (!_facts.TryGetValue(link, out var fact))
            {
                Alarm($"{ComponentText.Of(link)}: the cache composes a place through it, but it is not in the tree{readLive}");
                return;
            }

            if (!ReferenceEquals(frozen.RenderParent, fact.Parent))
            {
                Alarm($"{ComponentText.Of(link)}: parent in the cache {ComponentText.Of(frozen.RenderParent)}, " +
                      $"in the tree {ComponentText.Of(fact.Parent)}{readLive}");
            }

            if (!fact.Animated && !SamePlace(frozen.LocalTransform, fact.Local))
            {
                Alarm($"{ComponentText.Of(link)}: offset in the cache {Place(frozen.LocalTransform)}, " +
                      $"in the tree {Place(fact.Local)}{readLive}");
            }

            if (Math.Abs(frozen.RenderSize.Width - fact.Size.Width) > Tolerance ||
                Math.Abs(frozen.RenderSize.Height - fact.Size.Height) > Tolerance)
            {
                Alarm($"{ComponentText.Of(link)}: size in the cache {Dims(frozen.RenderSize)}, in the tree {Dims(fact.Size)}{readLive}");
            }

            if (frozen.ClipToBounds != fact.Clips)
            {
                Alarm($"{ComponentText.Of(link)}: clips in the cache {frozen.ClipToBounds}, in the tree {fact.Clips}{readLive}");
            }

            if (!fact.Animated && Math.Abs(frozen.Opacity - fact.Opacity) > 1e-3f)
            {
                Alarm($"{ComponentText.Of(link)}: opacity in the cache {frozen.Opacity:0.###}, in the tree {fact.Opacity:0.###}{readLive}");
            }

            link = frozen.RenderParent;
        }
    }

    // What the shaders read, against the tree: the slots a unit's records point at (matrices, opacities and clips share
    // one table, so an index kept past its release reads a stranger's record), and the glyph batch's runs and alpha.
    private void CheckShaderInputs(RenderCache live)
    {
        var slots = live.DescribeSlots();
        foreach (var unit in live.DescribeUnits())
        {
            if (unit.FadeSlot >= 0 && unit.Component is { } component && _facts.TryGetValue(component, out var fact) && !fact.Hidden)
            {
                CheckSlot(slots, unit.FadeSlot, TransformTable.SlotUse.Opacity, component, "takes its opacity from");
            }
        }

        var extent = live.GlyphExtent;
        _runs.Clear();
        foreach (var (unit, first, count) in live.DescribeTextRuns())
        {
            _textWithRuns.Add(unit);
            if (unit.Component is not { } component || !_facts.TryGetValue(component, out var fact) || fact.Hidden || fact.Pending)
            {
                continue;
            }

            if (first < 0 || count <= 0 || first + count > extent)
            {
                Alarm($"{ComponentText.Of(component)}: its glyphs [{first}, {first + count}) lie outside the {extent} the batch draws");
                continue;
            }

            _runs.Add((first, count, component));
            if (!live.GlyphsIssued(first, count))
            {
                Alarm($"{ComponentText.Of(component)}: its glyphs [{first}, {first + count}) are in the batch, but no segment draws them");
            }

            if (unit is RenderUnits.TextRenderUnit { TextComponent: { GlyphRun: { } glyphs } text } textUnit && glyphs.Count == count)
            {
                var before = _alarmCount;
                CheckGlyphs(live, slots, component, fact, live.GlyphAt(first), text, glyphs.Glyphs[0].HasOwnColor);
                if (_alarmCount > before)
                {
                    Alarm($"  ...{ComponentText.Of(component)} \"{(component as Controls.Text.TextBlock)?.Text}\" owns glyphs " +
                          $"[{first}, {first + count}) - {live.DescribeTextGroup(unit)}");
                }
                CheckGlyphPlaces(live, component, fact, textUnit, first, count);
            }
            else
            {
                Alarm($"{ComponentText.Of(component)}: {count} glyph instances in the batch, its text has " +
                      $"{(unit as RenderUnits.TextRenderUnit)?.TextComponent?.GlyphRun?.Count ?? 0}");
            }
        }

        // Text that should be on screen and is batched by kind, but has no run at all: it never reaches the shader.
        foreach (var unit in live.DescribeUnits())
        {
            if (unit is RenderUnits.TextRenderUnit { TextComponent: { GlyphRun.Count: > 0 } text } && !_textWithRuns.Contains(unit)
                && unit.Component is { } component && _facts.TryGetValue(component, out var fact) && !fact.Hidden && !fact.Pending
                && !live.WasCulled(unit) && live.BatchesText(text))
            {
                Alarm($"{ComponentText.Of(component)}: has {text.GlyphRun.Count} glyphs to draw, but none of them is in the glyph batch" +
                      $" - {live.DescribeTextGroup(unit)}");
            }
        }

        _textWithRuns.Clear();
        CheckTextOverItsAncestors(live);
        _runs.Sort((a, b) => a.First.CompareTo(b.First));
        for (var i = 1; i < _runs.Count; i++)
        {
            var (before, after) = (_runs[i - 1], _runs[i]);
            if (before.First + before.Count > after.First)
            {
                Alarm($"{ComponentText.Of(before.Component)} and {ComponentText.Of(after.Component)} share glyph instances " +
                      $"[{after.First}, {Math.Min(before.First + before.Count, after.First + after.Count)})");
            }
        }
    }

    private void CheckGlyphs(RenderCache live, Dictionary<int, (TransformTable.SlotUse Use, Guid Owner)> slots,
        IUIComponent component, TreeFact fact, GlyphItem glyph, RenderUnits.TextRenderComponent text, bool ownColor)
    {
        var matrix = (int)glyph.Params.X;
        if (matrix != 0)
        {
            CheckSlot(slots, matrix, TransformTable.SlotUse.Matrix, component, "takes its place from");
        }

        var fade = (int)glyph.Params.W;
        if (fade >= 0)
        {
            CheckSlot(slots, fade, TransformTable.SlotUse.Opacity, component, "takes its opacity from");
        }

        var clip = (int)glyph.Clip.X;
        if (clip >= 0)
        {
            CheckSlot(slots, clip, TransformTable.SlotUse.Clip, component, "is cut by");
            CheckClipBox(live, slots, component, clip);
        }

        if (ownColor || text.Foreground is not Core.Media.SolidColorBrush foreground || TreeAlpha(live, component, fact) is not { } tree)
        {
            return;
        }

        // The color the TREE gives the text, not the one the cache holds for it: a stale brush in the cache agrees with
        // itself. A selected tab's caption is recolored by a trigger, which is exactly where the two can part.
        var color = fact.TextColor ?? foreground.Color.ToVector4();
        if (fact.TextColor is { } treeColor && (Math.Abs(glyph.Color.X - treeColor.X) > 0.02f
                                                || Math.Abs(glyph.Color.Y - treeColor.Y) > 0.02f
                                                || Math.Abs(glyph.Color.Z - treeColor.Z) > 0.02f))
        {
            Alarm($"{ComponentText.Of(component)}: its glyphs are drawn in {Rgb(glyph.Color)}, the tree's foreground is {Rgb(treeColor)}" +
                  $" (the cache's brush: {Rgb(foreground.Color.ToVector4())})");
        }

        var drawn = glyph.Color.W * (fade >= 0 ? live.SlotAlpha(fade) : 1f);
        var expected = color.W * tree;
        if (Math.Abs(drawn - expected) > 0.02f)
        {
            Alarm($"{ComponentText.Of(component)}: its glyphs are drawn at alpha {drawn:0.###} (color {glyph.Color.W:0.###}" +
                  $"{(fade >= 0 ? $" x slot {fade} {live.SlotAlpha(fade):0.###}" : string.Empty)}), the tree has {expected:0.###}");
        }
    }

    // Where the shader puts each glyph (its slot's matrix over its local rect) against where the tree puts it, and which
    // atlas cell it samples against the one its text holds now: a glyph baked against an old place or an old cell is
    // drawn somewhere else, or as nothing.
    private void CheckGlyphPlaces(RenderCache live, IUIComponent component, TreeFact fact, RenderUnits.TextRenderUnit unit,
        int first, int count)
    {
        var text = unit.TextComponent;
        var glyphs = text.GlyphRun.Glyphs;
        var area = text.RenderingParameters.TextArea;
        var tree = unit.Place(fact.World);
        for (var i = 0; i < count; i++)
        {
            var glyph = live.GlyphAt(first + i);
            if (glyph.Source != glyphs[i].Source || (int)glyph.Params.Y != (int)glyphs[i].Layer)
            {
                Alarm($"{ComponentText.Of(component)}: glyph {i} samples atlas cell {Cell(glyph.Source, glyph.Params.Y)}, " +
                      $"its text holds {Cell(glyphs[i].Source, glyphs[i].Layer)}");
                return;
            }

            if (fact.Carried)
            {
                continue;
            }

            var drawn = Vector2F.TransformCoordinate(new Vector2F(glyph.LocalRect.X, glyph.LocalRect.Y),
                live.SlotMatrix((int)glyph.Params.X));
            var arrange = glyphs[i].ArrangeRect;
            var expected = Vector2F.TransformCoordinate(new Vector2F((float)(arrange.X + area.X), (float)(arrange.Y + area.Y)), tree);
            if (Math.Abs(drawn.X - expected.X) > 1f || Math.Abs(drawn.Y - expected.Y) > 1f)
            {
                Alarm($"{ComponentText.Of(component)}: glyph {i} is drawn at ({drawn.X:0.#}, {drawn.Y:0.#}), the tree has it at " +
                      $"({expected.X:0.#}, {expected.Y:0.#}) - slot {(int)glyph.Params.X}");
                return;
            }
        }
    }

    // A text's glyphs are drawn after everything its ancestors draw - a background issued later covers it, however right
    // every datum of both is.
    private void CheckTextOverItsAncestors(RenderCache live)
    {
        if (_runs.Count == 0)
        {
            return;
        }

        var order = live.DescribeDrawOrder();

        // The last op drawing each element's fills - the only thing of an ancestor that can cover a text.
        _lastFillOp.Clear();
        foreach (var (unit, op) in order)
        {
            if (unit is RenderUnits.TextRenderUnit || unit.Component is not { } owner) continue;
            if (!_lastFillOp.TryGetValue(owner, out var was) || op > was) _lastFillOp[owner] = op;
        }

        foreach (var (unit, _, _) in live.DescribeTextRuns())
        {
            if (unit.Component is not { } component || !order.TryGetValue(unit, out var textOp)) continue;

            var depth = 0;
            for (var link = _facts.TryGetValue(component, out var f) ? f.Parent : null; link != null && depth++ < 16;
                 link = _facts.TryGetValue(link, out var above) ? above.Parent : null)
            {
                if (_lastFillOp.TryGetValue(link, out var ancestorOp) && ancestorOp > textOp)
                {
                    Alarm($"{ComponentText.Of(component)}: its glyphs are drawn by op {textOp}, before {ComponentText.Of(link)} " +
                          $"behind it (op {ancestorOp}), which covers them - text {live.DescribeRanks(component, textOp)}; " +
                          $"behind {live.DescribeRanks(link, ancestorOp)}; chain {ChainRanks(live, component, link)}");
                    break;
                }
            }
        }
    }

    // Each link from a text up to the ancestor that covers it, with the rank the draw holds for it - where the order breaks.
    private string ChainRanks(RenderCache live, IUIComponent from, IUIComponent to)
    {
        var text = new StringBuilder();
        for (var link = from; link != null; link = _facts.TryGetValue(link, out var fact) ? fact.Parent : null)
        {
            text.Append(text.Length > 0 ? " < " : string.Empty).Append(link.GetType().Name)
                .Append(' ').Append(live.RankOfGroup(link)?.ToString() ?? "-");
            if (ReferenceEquals(link, to)) break;
        }

        return text.ToString();
    }

    // Where the clip a glyph is cut by lies, against where its owner is in the tree: a clip left at a place its owner has
    // moved from cuts away everything it should show.
    private void CheckClipBox(RenderCache live, Dictionary<int, (TransformTable.SlotUse Use, Guid Owner)> slots, IUIComponent component,
        int clip)
    {
        if (!slots.TryGetValue(clip, out var held) || held.Use != TransformTable.SlotUse.Clip
            || !_byRenderId.TryGetValue(held.Owner, out var owner) || !_facts.TryGetValue(owner, out var fact) || fact.Carried)
        {
            return;
        }

        var drawn = live.SlotClipBox(clip);
        var expected = live.ClipBoxOf(fact.Size, fact.World);
        if (Math.Abs(drawn.X - expected.X) > 1.5f || Math.Abs(drawn.Y - expected.Y) > 1.5f
            || Math.Abs(drawn.Z - expected.Z) > 1.5f || Math.Abs(drawn.W - expected.W) > 1.5f)
        {
            Alarm($"{ComponentText.Of(component)}: is cut by slot {clip}, the clip of {ComponentText.Of(owner)} at " +
                  $"({drawn.X:0}, {drawn.Y:0}) {drawn.Z:0}x{drawn.W:0} px - the tree has it at ({expected.X:0}, {expected.Y:0}) " +
                  $"{expected.Z:0}x{expected.W:0} px");
        }
    }

    private static string Rgb(Vector4F c) => $"({c.X:0.##}, {c.Y:0.##}, {c.Z:0.##}, a {c.W:0.##})";

    private static string Cell(Vector4F source, float layer) =>
        $"({source.X:0.###}, {source.Y:0.###}, {source.Z:0.###}x{source.W:0.###}) layer {layer:0}";

    // The tree's alpha for an element's own draws: its SelfOpacity and the opacities above it, up to a layer, which
    // applies its own when it is composited. Null while any of them is animated.
    private float? TreeAlpha(RenderCache live, IUIComponent component, TreeFact fact)
    {
        var alpha = fact.SelfOpacity;
        for (var link = component; link != null && _facts.TryGetValue(link, out var above); link = above.Parent)
        {
            if (above.Animated)
            {
                return null;
            }

            if (live.DrawsIntoLayer(link))
            {
                break;
            }

            alpha *= above.Opacity;
        }

        return alpha;
    }

    private void CheckSlot(Dictionary<int, (TransformTable.SlotUse Use, Guid Owner)> slots, int slot, TransformTable.SlotUse expected,
        IUIComponent component, string verb)
    {
        if (!slots.TryGetValue(slot, out var held))
        {
            Alarm($"{ComponentText.Of(component)}: {verb} slot {slot}, which nobody holds");
            return;
        }

        if (held.Use != expected)
        {
            Alarm($"{ComponentText.Of(component)}: {verb} slot {slot}, held as {held.Use} by {OwnerText(held.Owner)}");
            return;
        }

        if (held.Owner != Guid.Empty && !IsSelfOrAncestor(component, held.Owner))
        {
            Alarm($"{ComponentText.Of(component)}: {verb} slot {slot}, the {expected} of {OwnerText(held.Owner)} - " +
                  "neither it nor an ancestor");
        }
    }

    private bool IsSelfOrAncestor(IUIComponent component, Guid owner)
    {
        for (var link = component; link != null; link = _facts.TryGetValue(link, out var fact) ? fact.Parent : null)
        {
            if (link.RenderId == owner)
            {
                return true;
            }
        }

        return false;
    }

    private string OwnerText(Guid owner) =>
        owner == Guid.Empty ? "nobody"
        : _byRenderId.TryGetValue(owner, out var component) ? ComponentText.Of(component)
        : $"an element not in the tree ({owner.ToString()[..8]})";

    private void Alarm(string line)
    {
        _alarmCount++;
        if (_alarms.Count < AlarmsPerFrame)
        {
            _alarms.Add(line);
        }
    }

    private string Report(RenderCache live)
    {
        var text = new StringBuilder();
        text.AppendLine($"frame {_summary.Frame}: {_alarmCount} alarm(s) - record {_summary.Kind}, drawn by {live.LastDrawPath}," +
                        $" dirty {_summary.DirtyCount}, moved {_summary.MovedCount}, motion nodes {_summary.MotionNodeCount}");
        foreach (var line in _alarms)
        {
            text.AppendLine($"  {line}");
        }

        foreach (var node in _summary.MotionNodes)
        {
            text.AppendLine($"  motion node: {node}");
        }

        return text.ToString().TrimEnd();
    }

    private static bool SamePlace(Matrix4x4F a, Matrix4x4F b) =>
        Math.Abs(a.M41 - b.M41) <= Tolerance && Math.Abs(a.M42 - b.M42) <= Tolerance &&
        Math.Abs(a.M11 - b.M11) <= 1e-4f && Math.Abs(a.M22 - b.M22) <= 1e-4f &&
        Math.Abs(a.M12 - b.M12) <= 1e-4f && Math.Abs(a.M21 - b.M21) <= 1e-4f;

    private static string Place(Matrix4x4F m) =>
        m.M11 == 1 && m.M22 == 1 && m.M12 == 0 && m.M21 == 0
            ? $"({m.M41:0.##}, {m.M42:0.##})"
            : $"({m.M41:0.##}, {m.M42:0.##}) [{m.M11:0.###} {m.M12:0.###}; {m.M21:0.###} {m.M22:0.###}]";

    private static string Dims(Size size) => $"{size.Width:0.##}x{size.Height:0.##}";
}
