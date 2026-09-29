using Adamantium.Graphics;
using Adamantium.Graphics.Core;
using Adamantium.Graphics.Core.EffectsFramework;
using Adamantium.Mathematics;
using Adamantium.UI.FX;
using Adamantium.Vulkan.Core;

namespace Adamantium.UI.Rendering;

// Base for SDF-instanced shape batches: same-clip fills in one draw whose pixel shader rebuilds the shape from its
// distance field. Subclasses supply the bake and the pass.
internal abstract class SdfBatchCollector<TItem> : BatchCollector<TItem> where TItem : struct
{
    private bool _effectUnavailable;
    private int _verdictsWhenGaveUp = -1;
    private Effect _ownedEffect;

    // WHICH effect feeds this collector is the subclass's business: the shapes draw through BatchEffect and the brushes
    // through BrushEffect (see BrushEffect.fx - two effects because one parameter block could not hold both). What the
    // draw needs is the four parameters BOTH declare, held as fields rather than looked up by name: a dictionary hit per
    // parameter per draw is exactly the cost the batch exists to remove.
    protected EffectParameter ProjectionParam, ViewportSizeParam, InstancesAddressParam, TransformsAddressParam;

    /// <summary>Create the effect if it is not there yet and point the four parameters above at it. Called at the start
    /// of every frame, so it must be cheap once the effect exists.</summary>
    protected abstract void EnsureEffect(IGraphicsDevice device);

    /// <summary>Hands the effect <see cref="EnsureEffect"/> built to this collector, which frees it with its buffers.</summary>
    protected T Own<T>(T effect) where T : Effect
    {
        _ownedEffect = effect;
        return effect;
    }

    protected override void OnDisposeGpuResources()
    {
        _ownedEffect?.Dispose();
        _ownedEffect = null;
    }

    /// <summary>Device address of the owning cache's <see cref="TransformTable"/> - the SDF vertex shaders fetch each
    /// instance's world matrix from it by the instance's slot index. Set by RenderCache every frame BEFORE any draw
    /// (the shader always reads it; slot 0 is identity, so legacy world-baked instances render unchanged).</summary>
    public ulong TransformsAddress { get; set; }

    /// <summary>Device address of the alpha table, indexed by the same slot as the matrix. 0 = none bound.</summary>


    protected SdfBatchCollector(int initialCapacity) : base(initialCapacity) { }

    // The effect is built on first draw, not at BeginFrame, so idle collectors create no shader objects. A failure is
    // remembered until the verdicts change, not retried per draw.
    protected bool EnsureEffectForDraw(IGraphicsDevice device)
    {
        if (_effectUnavailable)
        {
            if (_verdictsWhenGaveUp == ShaderCompileStats.Generation) return false;
            _effectUnavailable = false;
        }

        try
        {
            EnsureEffect(device);
            return true;
        }
        catch (System.InvalidOperationException e)
        {
            _effectUnavailable = true;
            _verdictsWhenGaveUp = ShaderCompileStats.Generation;
            Serilog.Log.Logger.Error(e, $"{GetType().Name} draws nothing until its effect can be built");
            return false;
        }
    }

    // The SDF draw pass for this shape (per-instance TItem read from the buffer's device address by SV_InstanceID).
    protected abstract IEffectPass DrawPass { get; }

    // Straight-alpha AlphaBlend (matches solid fills); depth like the other main-pass units (Always, test+write). The quad
    // comes from SV_VertexID and the per-instance TItem from the buffer's device address (no vertex buffer). The address
    // is offset by firstInstance and drawn at base 0 so items[0..count-1] read THIS segment regardless of SV_InstanceID's
    // base (whether it includes firstInstance is translation-defined).
    protected override void DrawSegment(IGraphicsDevice device, Buffer<TItem> buffer, uint count, uint firstInstance, Matrix4x4F projection)
    {
        var dev = (GraphicsDevice)device;
        if (!EnsureEffectForDraw(device)) return;

        // Set what this draw DEPENDS on, don't inherit it. The color mask is device state like any other, and a pass
        // that borrows it (the strokes' union coverage masks color off for its depth pass) would otherwise leave these
        // instances writing nothing at all.
        dev.ColorComponentFlags = ColorComponentFlagBits.RBit | ColorComponentFlagBits.GBit |
                                  ColorComponentFlagBits.BBit | ColorComponentFlagBits.ABit;
        device.ColorBlendEnabled = true;
        device.ColorBlendEquation = ColorBlendEquations.AlphaBlend;
        device.PrimitiveRestartEnable = true;
        device.DepthTestEnabled = true;
        device.DepthWriteEnable = true;
        device.DepthCompareFunction = CompareOp.Always;
        // Written every draw: off-screen bakes share the effect and set their own projection in between.
        ProjectionParam.SetValue(projection);

        // The SDF shapes measure themselves in DEVICE pixels (SlotPixelScale), which needs the render target's pixel
        // size; without it the shader falls back to the raw slot space, where an anisotropically scaled slot smears the
        // shape's edge along its long axis.
        var vp = ((GraphicsDevice)device).CurrentViewports;
        if (vp is { Length: > 0 }) ViewportSizeParam.SetValue(new Vector2F(vp[0].Width, vp[0].Height));
        device.PrimitiveTopology = PrimitiveTopology.TriangleStrip;

        device.VertexType = null;
        InstancesAddressParam.SetValue(buffer.GetDeviceAddress() + firstInstance * (ulong)Stride);
        TransformsAddressParam.SetValue(TransformsAddress);

        var applyBytes0 = System.GC.GetAllocatedBytesForCurrentThread();
        DrawPass.Apply();
        var afterApply = System.GC.GetAllocatedBytesForCurrentThread();
        device.Draw(4, count, 0, 0);
        Adamantium.UI.Core.Diagnostics.RuntimeStats.PassApplyBytes += afterApply - applyBytes0;
        Adamantium.UI.Core.Diagnostics.RuntimeStats.DeviceDrawBytes += System.GC.GetAllocatedBytesForCurrentThread() - afterApply;
        Adamantium.UI.Core.Diagnostics.RuntimeStats.PassApplyCount++;
    }
}
