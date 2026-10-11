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
        _stack.Clear();
        _animated.Clear();
        Compositor.CollectOwners(_animated);
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
                (float)component.Opacity, world, animated, carried);
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
                Alarm($"{ComponentText.Of(component)}: in the paint order ({group.Units} units), but not in the tree");
                continue;
            }

            CheckChain(live, component);

            if (!fact.Carried && live.TryGetComposedWorld(component, out var world) && !SamePlace(world, fact.World))
            {
                Alarm($"{ComponentText.Of(component)}: drawn at {Place(world)}, the tree has it at {Place(fact.World)}");
            }
        }

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
            _settings.Log(Report(live));
        }
    }

    private void CheckChain(RenderCache live, IUIComponent component)
    {
        for (var link = component; link != null && _checked.Add(link);)
        {
            if (!live.TryGetFrozen(link, out var frozen))
            {
                Alarm($"{ComponentText.Of(link)}: drawn, but the cache holds no layout for it");
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
