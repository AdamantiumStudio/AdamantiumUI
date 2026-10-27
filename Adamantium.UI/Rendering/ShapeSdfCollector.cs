using Adamantium.Graphics.Core;
using Adamantium.Graphics.Core.EffectsFramework;
using Adamantium.UI.FX;

namespace Adamantium.UI.Rendering;

/// <summary>An SDF batch drawn through <c>BatchEffect</c> - the SHAPES: solid rounded rects, ellipses, regular polygons,
/// and the halo band under them. Its sibling <see cref="BrushSdfCollector{TItem}"/> draws the computed and sampled fills
/// through <c>BrushEffect</c>; the two effects exist because one parameter block could not hold both families (see the
/// note at the top of BrushEffect.fx).</summary>
internal abstract class ShapeSdfCollector<TItem> : SdfBatchCollector<TItem> where TItem : struct
{
    protected BatchEffect Effect;

    protected ShapeSdfCollector(int initialCapacity) : base(initialCapacity) { }

    protected override void EnsureEffect(IGraphicsDevice device)
    {
        if (Effect != null) return;

        Effect = Own(new BatchEffect(device));
        ProjectionParam = Effect.Projection;
        ViewportSizeParam = Effect.ViewportSize;
        TargetOriginParam = Effect.TargetOrigin;
        InstancesParam = GetInstances(Effect);
        TransformsParam = Effect.Transforms;
    }

    /// <summary>The pointer of the effect this collector's instances are read through.</summary>
    protected abstract EffectParameter GetInstances(BatchEffect effect);
}
