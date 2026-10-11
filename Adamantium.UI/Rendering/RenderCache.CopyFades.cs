using System;
using System.Collections.Generic;
using Adamantium.Graphics.Core;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Graphics;
using Adamantium.UI.Core.Media.Animation;

namespace Adamantium.UI.Rendering;

public partial class RenderCache
{
    private const int MaxCopyPhases = 64;

    private readonly Dictionary<IUIComponent, CopyFades> _copyFades = new();
    private readonly HashSet<int> _copyFadeSlots = new();
    private readonly List<IUIComponent> _copyFadesGone = new();
    private readonly List<(int Phase, int Parent)> _copyFadeKeysGone = new();
    private int _copyFadeWalk;
    private bool _copyFadeNotLive;

    private sealed class CopyFades
    {
        public readonly Dictionary<(int Phase, int Parent), PhaseSlot> Slots = new();
        public Compositor.Entry Entry;
        public double Period;
        public int Phases;
        public int Walk;
        public int NextKey;
        public bool Restaled;
    }

    private sealed class PhaseSlot
    {
        public int Slot;
        public int Phase;
        public int Key;
        public int Walk;
    }

    private int CopyFadeSlot(IGraphicsDevice device, IUIComponent from, ControlGroup host, int copy, int resolved)
    {
        if (resolved < 0)
        {
            return resolved;
        }

        for (var c = from; c != null; c = ApplySnap(c).RenderParent)
        {
            if (_transformTable.TryGetOpacitySlot(c.RenderId, out var own) && own == resolved)
            {
                return CopySlot(device, c, host, copy, own);
            }

            if (ReferenceEquals(c, host.Component))
            {
                break;
            }
        }

        return resolved;
    }

    private int CopySlot(IGraphicsDevice device, IUIComponent fading, ControlGroup host, int copy, int own)
    {
        if (!_copyFades.TryGetValue(fading, out var fades))
        {
            _copyFades[fading] = fades = new CopyFades();
        }

        var stagger = host.CloneStagger.TotalSeconds;
        if (fades.Walk != _copyFadeWalk)
        {
            fades.Walk = _copyFadeWalk;
            fades.Restaled = false;
            fades.Entry = fading is AdamantiumComponent target ? Compositor.EntryFor(target, CompositorChannel.Opacity) : null;
            fades.Period = PeriodOf(fades.Entry);
            fades.Phases = PhasesOf(fades.Period, stagger);
        }

        if (fades.Phases == 0)
        {
            return own;
        }

        var step = host.CloneSteps != null && copy < host.CloneSteps.Count ? host.CloneSteps[copy] : copy;
        var lag = step * stagger % fades.Period;
        var phase = (int)Math.Round(lag / fades.Period * fades.Phases) % fades.Phases;

        var parent = ReferenceEquals(fading, host.Component) ? null : ApplySnap(fading).RenderParent;
        var parentSlot = OpacitySlotOf(device, ApplySnap(fading).RenderParent);
        if (parent != null)
        {
            parentSlot = CopyFadeSlot(device, parent, host, copy, parentSlot);
        }

        if (!fades.Slots.TryGetValue((phase, parentSlot), out var shared))
        {
            shared = new PhaseSlot { Phase = phase, Key = fades.NextKey++ };
            shared.Slot = _transformTable.AcquireOpacitySlot(fading.RenderId, shared.Key);
            _transformTable.SetOpacityParent(device, shared.Slot, parentSlot);
            _transformTable.SetAlpha(device, shared.Slot, PhaseAlpha(fades.Entry, fades, phase));
            fades.Slots[(phase, parentSlot)] = shared;
            _copyFadeSlots.Add(shared.Slot);
        }

        shared.Walk = _copyFadeWalk;
        if (!_transformTable.IsSlotLive(shared.Slot))
        {
            _copyFadeNotLive = true;
            return own;
        }

        return shared.Slot;
    }

    private static int PhasesOf(double period, double stagger)
    {
        if (period <= 0 || stagger <= 0)
        {
            return 0;
        }

        var offset = stagger % period;
        var spacing = Math.Min(offset, period - offset);
        if (spacing < 1e-9)
        {
            return 0;
        }

        return Math.Min(MaxCopyPhases, (int)Math.Ceiling(period / spacing - 1e-9));
    }

    private static double PeriodOf(Compositor.Entry entry) =>
        entry != null && entry.Curve.IsEndless ? entry.Curve.Period : 0;

    private static float PhaseAlpha(Compositor.Entry entry, CopyFades fades, int phase) =>
        entry.AlphaAt(phase * fades.Period / fades.Phases);

    private bool IsCopyFade(int slot) => _copyFadeSlots.Contains(slot);

    private void SettleCopyFades(IGraphicsDevice device, IUIComponent fading, float alpha)
    {
        if (!_copyFades.TryGetValue(fading, out var fades) || fades.Slots.Count == 0
            || (fading is AdamantiumComponent target && PeriodOf(Compositor.EntryFor(target, CompositorChannel.Opacity)) > 0))
        {
            return;
        }

        foreach (var shared in fades.Slots.Values)
        {
            _transformTable.SetAlpha(device, shared.Slot, alpha);
        }
    }

    private void ReleaseCopyFades(IUIComponent fading)
    {
        if (!_copyFades.Remove(fading, out var fades))
        {
            return;
        }

        foreach (var shared in fades.Slots.Values)
        {
            _transformTable.ReleaseOpacitySlot(fading.RenderId, shared.Key);
            _copyFadeSlots.Remove(shared.Slot);
        }
    }

    private void ReleaseUnusedCopyFades()
    {
        _copyFadesGone.Clear();
        foreach (var (fading, fades) in _copyFades)
        {
            if (fades.Walk != _copyFadeWalk)
            {
                _copyFadesGone.Add(fading);
                continue;
            }

            _copyFadeKeysGone.Clear();
            foreach (var (key, shared) in fades.Slots)
            {
                if (shared.Walk != _copyFadeWalk)
                {
                    _copyFadeKeysGone.Add(key);
                }
            }

            foreach (var key in _copyFadeKeysGone)
            {
                fades.Slots.Remove(key, out var shared);
                _transformTable.ReleaseOpacitySlot(fading.RenderId, shared.Key);
                _copyFadeSlots.Remove(shared.Slot);
            }
        }

        foreach (var fading in _copyFadesGone)
        {
            ReleaseCopyFades(fading);
        }
    }

    private void ApplyCopyFades(IGraphicsDevice device, IUIComponent owner, Compositor.Entry entry)
    {
        if (!_copyFades.TryGetValue(owner, out var fades))
        {
            return;
        }

        if (PeriodOf(entry) != fades.Period)
        {
            if (!fades.Restaled)
            {
                fades.Restaled = true;
                StreamStaleBecause("copyFadePhases");
                Core.LoopSignal.Request();
            }

            return;
        }

        foreach (var shared in fades.Slots.Values)
        {
            _transformTable.SetAlpha(device, shared.Slot, PhaseAlpha(entry, fades, shared.Phase));
        }
    }
}
