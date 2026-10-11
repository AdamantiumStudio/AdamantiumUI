using System.Collections.Generic;
using Adamantium.Graphics.Core;
using Adamantium.Mathematics;
using Adamantium.UI.Core.Graphics;
using Adamantium.UI.Rendering.Retained;

namespace Adamantium.UI.Rendering;

public partial class RenderCache
{
    private readonly Dictionary<IRenderUnit, CopyLedger> _copiesByUnit = new();
    private readonly HashSet<IRenderUnit> _culledThisCopy = new();
    private readonly HashSet<IRenderUnit> _brushIndexedCopies = new();

    private struct CopySlots
    {
        public bool Holds;
        public bool Culled;
        public int Fade;
        public int? Rect;
        public (SdfSlotKind Kind, int Slot)? Sdf;
        public (int First, int Count, Graphics.Fonts.FontAtlas Atlas)? TextRun;
        public (int First, int Count)? TexRun;
        public int? FractalKind;
        public (BatchArena Arena, int Slot)? Fill;
        public HaloRuns? Halo;
    }

    private sealed class CopyLedger(ControlGroup root, IReadOnlyList<Matrix4x4F> clones)
    {
        public readonly ControlGroup Root = root;
        public readonly IReadOnlyList<Matrix4x4F> Clones = clones;
        public readonly List<CopySlots> Copies = new();
    }

    private void ForgetCopies()
    {
        _copiesByUnit.Clear();
        _culledThisCopy.Clear();
        _brushIndexedCopies.Clear();
    }

    private void NoteCopySlots(int start, int end, IReadOnlyList<Matrix4x4F> clones, bool last)
    {
        var root = _groups[start];
        for (var i = start; i < end; i++)
        {
            foreach (var u in _groups[i].Units)
            {
                if (!_copiesByUnit.TryGetValue(u, out var ledger))
                {
                    _copiesByUnit[u] = ledger = new CopyLedger(root, clones);
                }

                var copy = CaptureSlots(u);
                copy.Culled = _culledThisCopy.Contains(u);
                copy.Fade = u.FadeSlot;
                ledger.Copies.Add(copy);
                if (!last)
                {
                    ForgetSlots(u);
                    continue;
                }

                for (var k = ledger.Copies.Count - 1; k >= 0; k--)
                {
                    if (!ledger.Copies[k].Holds)
                    {
                        continue;
                    }

                    InstallSlots(u, ledger.Copies[k]);
                    break;
                }
            }
        }

        _culledThisCopy.Clear();
    }

    private CopySlots CaptureSlots(IRenderUnit u)
    {
        var copy = new CopySlots
        {
            Rect = _rectSlotByUnit.TryGetValue(u, out var rect) ? rect : null,
            Sdf = _sdfSlotByUnit.TryGetValue(u, out var sdf) ? sdf : null,
            TextRun = _textRunByUnit.TryGetValue(u, out var text) ? text : null,
            TexRun = _texRunByUnit.TryGetValue(u, out var tex) ? tex : null,
            FractalKind = _fractalKindByUnit.TryGetValue(u, out var fractal) ? fractal : null,
            Fill = _fillSlotByUnit.TryGetValue(u, out var fill) ? fill : null,
            Halo = _haloRunsByUnit.TryGetValue(u, out var halo) ? halo : null
        };
        copy.Holds = copy.Rect.HasValue || copy.Sdf.HasValue || copy.TextRun.HasValue || copy.Fill.HasValue;
        return copy;
    }

    private void InstallSlots(IRenderUnit u, CopySlots copy)
    {
        ForgetSlots(u);
        Put(_rectSlotByUnit, u, copy.Rect);
        Put(_sdfSlotByUnit, u, copy.Sdf);
        Put(_textRunByUnit, u, copy.TextRun);
        Put(_texRunByUnit, u, copy.TexRun);
        Put(_fractalKindByUnit, u, copy.FractalKind);
        Put(_fillSlotByUnit, u, copy.Fill);
        Put(_haloRunsByUnit, u, copy.Halo);
    }

    private static void Put<T>(Dictionary<IRenderUnit, T> map, IRenderUnit u, T? value) where T : struct
    {
        if (value is { } held)
        {
            map[u] = held;
        }
    }

    private void ForgetSlots(IRenderUnit u)
    {
        _rectSlotByUnit.Remove(u);
        _sdfSlotByUnit.Remove(u);
        _textRunByUnit.Remove(u);
        _texRunByUnit.Remove(u);
        _fractalKindByUnit.Remove(u);
        _fillSlotByUnit.Remove(u);
        _haloRunsByUnit.Remove(u);
    }

    private bool Cloned(IRenderUnit u) =>
        u.Component != null && _groupById.TryGetValue(u.Component.RenderId, out var group) && _clonedWhenRecorded.Contains(group);

    private bool IndexesCopy(IRenderUnit u) => !_cloneMatrix.HasValue || _brushIndexedCopies.Add(u);

    private bool CopiesPatchable(IRenderUnit u, CopyLedger ledger)
    {
        if (!_groupById.TryGetValue(ledger.Root.Component.RenderId, out var root) || !ReferenceEquals(root, ledger.Root)
            || !root.InOrder || !ReferenceEquals(root.Clones, ledger.Clones) || ledger.Copies.Count != ledger.Clones.Count)
        {
            return false;
        }

        var current = CaptureSlots(u);
        try
        {
            foreach (var copy in ledger.Copies)
            {
                if (!copy.Holds)
                {
                    if (copy.Culled)
                    {
                        continue;
                    }

                    return false;
                }

                InstallSlots(u, copy);
                if (!SlotsPatchable(u))
                {
                    return false;
                }
            }

            return true;
        }
        finally
        {
            InstallSlots(u, current);
        }
    }

    private bool PatchUnit(IGraphicsDevice device, IRenderUnit u)
    {
        if (!_copiesByUnit.TryGetValue(u, out var ledger))
        {
            var bakeWorld = ResolveBake(device, u.Component, World(u.Component), out var slot);
            return PatchSlot(device, u, bakeWorld, slot);
        }

        var current = CaptureSlots(u);
        var fade = OpacitySlotOf(device, u.Component);
        try
        {
            for (var k = 0; k < ledger.Copies.Count; k++)
            {
                if (!ledger.Copies[k].Holds)
                {
                    continue;
                }

                InstallSlots(u, ledger.Copies[k]);
                u.SetFadeSlot(IsCopyFade(ledger.Copies[k].Fade) ? ledger.Copies[k].Fade : fade);
                _cloneMatrix = ledger.Clones[k];
                var bakeWorld = ResolveBake(device, u.Component, World(u.Component), out var slot);
                if (!PatchSlot(device, u, bakeWorld, slot))
                {
                    return false;
                }
            }

            return true;
        }
        finally
        {
            _cloneMatrix = null;
            u.SetFadeSlot(fade);
            InstallSlots(u, current);
        }
    }
}
