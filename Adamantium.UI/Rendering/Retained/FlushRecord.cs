using System;
using System.Collections.Generic;
using Adamantium.UI.Core.Graphics;
using Adamantium.Vulkan.Core;

namespace Adamantium.UI.Rendering.Retained;

/// <summary>One clip group's recorded flush in <see cref="InstancedFillCollector"/> - its runs of every brush family,
/// the units drawn over them and the clip - replayed on clean frames; pooled.</summary>
internal sealed class FlushRecord
{
    public readonly List<InstanceRun> Keys = new();
    public readonly List<InstanceRun> GradKeys = new();
    public readonly List<PatternRun> PatKeys = new();
    public readonly List<TextureRun> TexKeys = new();
    public readonly List<MaterialRun> MatKeys = new();
    public readonly List<IRenderUnit> Units = new();
    public Rect2D Scissor;

    /// <summary>The group's first stencil mark: run N stamps one more than it in paint order, and fringes draw only where
    /// the stencil is lower. Replays reuse it.</summary>
    public uint StencilRef;

    /// <summary>The mark run <paramref name="index"/> stamps - its place in the group's paint order, clamped to the
    /// field so a long group cannot wrap back onto an earlier run's mark.</summary>
    public uint MarkFor(int index) => Math.Min(StencilRef + (uint)index, InstancedFillCollector.CoverageMarkBits);

    public void Reset()
    {
        Keys.Clear();
        GradKeys.Clear();
        PatKeys.Clear();
        TexKeys.Clear();
        MatKeys.Clear();
        Units.Clear();
    }
}
