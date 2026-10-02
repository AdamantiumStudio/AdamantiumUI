using System;
using System.Collections.Generic;
using Adamantium.Graphics;
using Adamantium.Graphics.Core;
using Adamantium.Graphics.Core.Extensions;
using Adamantium.Graphics.Core.Models;
using Adamantium.Graphics.Core.Vertices;
using Adamantium.Graphics.Fonts;
using Adamantium.Imaging;
using Adamantium.Mathematics;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Graphics;
using Adamantium.UI.Core.Media;
using Adamantium.UI.FX;
using Adamantium.Vulkan.Core;
using Buffer = Adamantium.Graphics.Buffer;

namespace Adamantium.UI.Rendering.RenderUnits;

public abstract class UIRenderComponent : DeferredDisposableObject
{
    // UI body geometry lives in mappable (BAR) memory and is reused across frames through the buffer manager: a
    // size/shape change rewrites the current frame's ring slot in place (UpdateGeometry) instead of allocating a fresh
    // Vulkan buffer.
    private const MemoryPropertyFlags UiMemory = MemoryPropertyFlags.HostVisible | MemoryPropertyFlags.DeviceLocal;
    private static readonly int VertexStride = System.Runtime.InteropServices.Marshal.SizeOf<UIVertex>();

    protected GpuBufferManager BufferManager { get; }
    private ReusableBuffer _vertexBuffer;
    private ReusableBuffer _indexBuffer;
    private UIVertex[] _vertices;
    private int[] _indices;
    private uint _vertexCount;
    private uint _indexCount;

    protected UIRenderComponent(IGraphicsDevice device, UIBasicEffect uiBasicEffect, Mesh mesh, GpuBufferManager bufferManager) : base(device)
    {
        GraphicsDevice = device;
        UIBasicEffect = uiBasicEffect;
        BufferManager = bufferManager;
        ColorBlendEquation = ColorBlendEquations.AlphaBlend;
        VertexType = typeof(UIVertex);
        if (mesh != null) SetMesh(mesh);
    }

    public Mesh Mesh { get; private set; }

    public Type VertexType { get; set; }

    public PrimitiveType PrimitiveType { get; set; }

    public RenderData RenderData { get; set; }

    public UIBasicEffect UIBasicEffect { get; set; }

    protected IGraphicsDevice GraphicsDevice { get; private set; }

    public ColorBlendEquationEXT ColorBlendEquation { get; set; }

    // Re-point this component at new geometry of the same kind WITHOUT recreating it (or its buffers): the manager
    // rewrites the existing ring slot in place, growing only if the new geometry needs more room. This is the resize/
    // animation fast path - the unit calls it instead of building a fresh component (and a fresh allocation) per frame.
    public void UpdateGeometry(Mesh mesh) => SetMesh(mesh);

    private void SetMesh(Mesh mesh)
    {
        Mesh = mesh;
        if (mesh != null) PrimitiveType = mesh.MeshTopology;

        _vertices = mesh?.ToUIVertices();   // already a fresh array - we own it
        _vertexCount = _vertices is { Length: > 0 } ? (uint)_vertices.Length : 0u;
        // COPY the indices. Taking mesh.Indices by reference aliased the live mesh's own array, so a later
        // re-tessellation (which rewrites the mesh IN PLACE) silently changed the index data of an already-built unit -
        // this component must hold a snapshot, exactly as it does for the vertices.
        _indices = mesh is { HasIndices: true } && _vertexCount > 0 ? (int[])mesh.Indices.Clone() : null;
        _indexCount = _indices != null ? (uint)_indices.Length : 0u;

        // Buffers are allocated lazily on the first individual Render, since batched units never draw themselves.
        _vertexBuffer?.Invalidate();
        _indexBuffer?.Invalidate();
    }

    /// <summary>The element this draw belongs to. Needed where a fill has to be PRODUCED rather than read: a vector
    /// source is baked at the size this unit draws it, and the bake needs the owner's data and a way to notify it.</summary>
    public IUIComponent Owner { get; set; }

    public void Update(Matrix4x4F transform, Matrix4x4F projectionMatrix)
    {
        RenderData.TransformMatrix = transform;
        RenderData.ProjectionMatrix = projectionMatrix;
    }

    /// <summary>Out-of-render-pass work, recorded before BeginRendering. Base is a no-op.</summary>
    public virtual void PreRender() { }

    public virtual void Render()
    {
        if (_vertexCount == 0) return;

        // First INDIVIDUAL draw: lazily create the per-unit buffer (batched units never get here, so never allocate).
        // Rent this frame's ring slot; upload the geometry only if the slot is stale (a static body settles to zero
        // work after the first N frames, an animated one writes only the current slot - never a new allocation).
        _vertexBuffer ??= ToDispose(BufferManager.CreateBuffer(BufferUsageFlags.VertexBuffer, UiMemory));
        var vertexBuffer = _vertexBuffer.Acquire((ulong)(_vertexCount * (uint)VertexStride), out var writeVertices);
        if (writeVertices) vertexBuffer.SetData(_vertices, 0, _vertexCount);

        GraphicsDevice.VertexType = VertexType;
        GraphicsDevice.PolygonMode = PolygonMode.Fill;
        GraphicsDevice.PrimitiveTopology = Mesh.MeshTopology;
        GraphicsDevice.ColorBlendEquation = ColorBlendEquation;
        GraphicsDevice.DepthCompareFunction = CompareOp.Always;
        GraphicsDevice.DepthTestEnabled = true;
        GraphicsDevice.DepthWriteEnable = true;

        if (_indexCount > 0)
        {
            _indexBuffer ??= ToDispose(BufferManager.CreateBuffer(BufferUsageFlags.IndexBuffer, UiMemory));
            var indexBuffer = _indexBuffer.Acquire((ulong)(_indexCount * sizeof(int)), out var writeIndices);
            if (writeIndices) indexBuffer.SetData(_indices, 0, _indexCount);
            // DrawIndexed binds both the vertex and index buffers itself - don't bind them again. The over-allocated
            // ring buffer may be larger than the geometry, so draw the actual index count, not the buffer's capacity.
            GraphicsDevice.DrawIndexed(vertexBuffer, indexBuffer, indexCount: _indexCount);
        }
        else
        {
            GraphicsDevice.SetVertexBuffer(vertexBuffer);
            GraphicsDevice.Draw(_vertexCount, 1);
        }
    }
}

public class StrokeRenderComponent : UIRenderComponent
{
    public StrokeRenderComponent(IGraphicsDevice graphicsDevice, UIBasicEffect uiBasicEffect, Mesh mesh, Pen pen, GpuBufferManager bufferManager) : base(graphicsDevice, uiBasicEffect, mesh, bufferManager)
    {
        PrimitiveType = PrimitiveType.TriangleList;
        Pen = pen;
    }
    
    public Pen Pen {get; set; }

    public override void Render()
    {
        // Same rule as the fill: a stroke brush this path cannot paint draws NOTHING, rather than falling through with
        // no pass applied and inheriting whatever the last draw bound.
        if (Pen?.Brush is not SolidColorBrush solidColor)
        {
            UnpaintableBrush.ReportOnce(Pen?.Brush);
            return;
        }

        var world = RenderData.TransformMatrix;
        UIBasicEffect.Wvp.SetValue(world * RenderData.ProjectionMatrix);
        UIBasicEffect.Opacity.SetValue(RenderData.Opacity);

        var fill = solidColor.Color.ToVector4();
        fill.W *= (float)solidColor.Opacity;   // fold the brush's own Opacity into the color alpha
        UIBasicEffect.FillColor.SetValue(fill);
        UIBasicEffect.BasicSolidColorPass.Apply();
        base.Render();
    }
}

public class GeometryRenderComponent : UIRenderComponent
{
    private readonly IResourceFactory _resourceFactory;

    public GeometryRenderComponent(IGraphicsDevice graphicsDevice, UIBasicEffect uiBasicEffect, Mesh mesh, Brush background, GpuBufferManager bufferManager, IResourceFactory resourceFactory = null) : base(graphicsDevice, uiBasicEffect, mesh, bufferManager)
    {
        Background = background;
        _resourceFactory = resourceFactory;
    }

    public Brush Background { get; set; }

    public override void Render()
    {
        // The brush's CURRENT appearance, not the one it had when this was recorded: Background holds the live brush and
        // republishes its snapshot on every change, so dragging a brush parameter shows up without a re-record. A brush
        // that is already frozen is its own snapshot, so this is a no-op for the paths that hand one over.
        var brush = Background?.Snapshot ?? Background;

        // A picture on ARBITRARY geometry. The rounded rect and the ellipse sample their texture in the SDF batches; a
        // tessellated shape has no SDF, so it is drawn here - one draw per shape, not instanced (which is where the
        // gradient and pattern fills started too).
        if (brush is TileBrush image)
        {
            RenderTextured(image);
            return;
        }

        // A brush this path cannot paint is DRAWN NOT AT ALL. It used to fall through to base.Render() with no pass
        // applied, so the geometry was drawn with whatever effect state the PREVIOUS draw left bound - in practice the
        // glyph pass, which smeared the font atlas across the frame and cost hours to trace. Nothing on screen is the
        // honest answer; the batches paint every brush that has a batch, and one that does not belongs to neither path.
        if (brush is not SolidColorBrush solidColor)
        {
            UnpaintableBrush.ReportOnce(Background);
            return;
        }

        var world = RenderData.TransformMatrix;
        UIBasicEffect.Wvp.SetValue(world * RenderData.ProjectionMatrix);
        UIBasicEffect.Opacity.SetValue(RenderData.Opacity);

        var fill = solidColor.Color.ToVector4();
        fill.W *= (float)solidColor.Opacity;   // fold the brush's own Opacity into the color alpha
        UIBasicEffect.FillColor.SetValue(fill);
        // Fully transparent fill -> force zero opacity. Value check (not `== Brushes.Transparent`) so a FROZEN clone of
        // Transparent - a different instance from the shared static - is still recognized (payload brushes are frozen).
        if (solidColor.Color.A == 0)
        {
            UIBasicEffect.Opacity.SetValue(0f);
        }
        UIBasicEffect.BasicSolidColorPass.Apply();

        base.Render();
    }

    // Map the picture across the shape's own LOCAL box (a tessellated mesh carries no usable uv0) with the SAME tiling
    // arithmetic the textured batch uses, so a brush looks identical whichever shape it is on. A source still decoding
    // has no texture yet: draw nothing this frame and let the re-render pick it up.
    private void RenderTextured(TileBrush image)
    {
        var world = RenderData.TransformMatrix;
        var bounds = Mesh.Bounds;
        var box = new Rect(
            bounds.Center.X - bounds.HalfExtent.X,
            bounds.Center.Y - bounds.HalfExtent.Y,
            bounds.Size.X,
            bounds.Size.Y);
        if (box.Width <= 0 || box.Height <= 0)
        {
            return;
        }

        // The box first: it is what a vector source must be baked at, since this path maps the picture across it.
        var texture = TextureBatchCollector.BrushTexture(image, _resourceFactory, box.Size, Owner);
        if (texture == null)
        {
            return;
        }

        // The mesh is in LOCAL units and the world scale is applied by the vertex shader, so a tile is sized against the
        // scale here - otherwise the picture would tile by local units and change density with the element's scale.
        var layout = ImageTiling.Layout(image, box, world.M11, world.M22, TextureBatchCollector.SourceIsSlice(image));
        var tint = image.Tint.ToVector4();
        tint.W *= (float)image.Opacity;

        UIBasicEffect.Wvp.SetValue(world * RenderData.ProjectionMatrix);
        UIBasicEffect.World.SetValue(world);
        UIBasicEffect.Opacity.SetValue(RenderData.Opacity);
        UIBasicEffect.FillBounds.SetValue(new Vector4F((float)box.X, (float)box.Y, (float)box.Width, (float)box.Height));
        UIBasicEffect.TexTile.SetValue(layout.Tile);
        UIBasicEffect.TexRotation.SetValue(layout.Rotation);
        UIBasicEffect.TexDrawn.SetValue(layout.Drawn);
        UIBasicEffect.TexUvRect.SetValue(layout.UvRect);
        UIBasicEffect.TexTint.SetValue(tint);
        UIBasicEffect.TexRepeat.SetValue(layout.Repeats ? 1f : 0f);
        UIBasicEffect.TexMirror.SetValue(layout.Mirror);
        UIBasicEffect.ShaderTexture.SetResource(texture);
        UIBasicEffect.SampleType.SetResource(SamplerStates.LinearClampToEdge);
        UIBasicEffect.BasicTexturedFillPass.Apply();

        base.Render();
    }
}

/// <summary>Says ONCE per brush type that the per-unit path was handed something it cannot paint. Once, because this sits
/// in a per-frame draw: a message per frame would be a flood, and the useful information - WHICH brush has no per-unit
/// form - is the same every time.</summary>
internal static class UnpaintableBrush
{
    private static readonly HashSet<Type> Reported = [];

    public static void ReportOnce(Brush brush)
    {
        var type = brush?.GetType();
        if (type == null) return;

        lock (Reported)
        {
            if (!Reported.Add(type)) return;
        }

        Serilog.Log.Logger.Warning(
            "{Brush} has no per-unit draw: a shape filled with it and REJECTED by the batches draws nothing. " +
            "Either give the batch a path for it, or give this brush a per-unit form.", type.Name);
    }
}

public class ImageRenderComponent : UIRenderComponent
{
    public ImageRenderComponent(IGraphicsDevice device, UIBasicEffect uiBasicEffect, Mesh mesh, ITexture texture, GpuBufferManager bufferManager) : base(device, uiBasicEffect, mesh, bufferManager)
    {
        Texture = texture;
    }

    public ImageRenderComponent(IGraphicsDevice device, UIBasicEffect uiBasicEffect, Mesh mesh, Brush background, GpuBufferManager bufferManager) : base(device, uiBasicEffect, mesh, bufferManager)
    {
        Background = background;
    }
    
    public Brush Background { get; set; }

    public ITexture Texture { get; set; }

    /// <summary>Set when <see cref="Texture"/> is an animation's frame ARRAY: which layer to sample. Advancing the
    /// animation writes this number and nothing else - no upload, no rebuild.</summary>
    public int? FrameLayer { get; set; }

    public SamplerState Sampler { get; set; }

    /// <summary>When set, this image is backed by an externally produced shared surface. Sampled via the private
    /// <see cref="UIRenderComponent.Texture"/>, refreshed each frame by the latch in <see cref="PreRender"/>.</summary>
    public SharedSurface SharedSource { get; set; }

    // The shared surface (Texture) is sampled directly. Register, for this frame's UI Submit: a wait on
    // Produce>=latest (so the producer's write+transition-to-ShaderReadOnly is complete before the fragment sample)
    // and a signal of Consume=latest (so the producer may reuse the surface). Producer/consumer run one frame in
    // lockstep (the producer CPU-throttles on Consume), so there is no read-during-write race.
    public override void PreRender()
    {
        if (SharedSource == null)
        {
            return;
        }

        var latest = SharedSource.LatchNewFrame();
        if (latest == 0)
        {
            return;
        }

        GraphicsDevice.AddWaitSemaphore(SharedSource.ProduceSemaphore, PipelineStageFlagBits2.FragmentShaderBit, latest);
        GraphicsDevice.AddSignalSemaphore(SharedSource.ConsumeSemaphore, latest);
    }

    public override void Render()
    {
        var world = RenderData.TransformMatrix;;
        UIBasicEffect.Wvp.SetValue(world * RenderData.ProjectionMatrix);
        UIBasicEffect.Opacity.SetValue(RenderData.Opacity);

        if (Background is SolidColorBrush solidColor)
        {
            var fill = solidColor.Color.ToVector4();
            fill.W *= (float)solidColor.Opacity;   // fold the brush's own Opacity into the color alpha
            UIBasicEffect.FillColor.SetValue(fill);
        }
        
        if (Texture == null)
        {
            if (Background is SolidColorBrush)
            {
                UIBasicEffect.BasicSolidColorPass.Apply();
            }
        }
        else if (FrameLayer is { } layer)
        {
            // An animation: the frames are layers of ONE texture and this draw picks one. Nothing is uploaded per frame.
            UIBasicEffect.ShaderTextureArray.SetResource(Texture);
            UIBasicEffect.TextureLayer.SetValue((float)layer);
            UIBasicEffect.SampleType.SetResource(Sampler);
            UIBasicEffect.BasicTexturedArrayPass.Apply();
        }
        else
        {
            UIBasicEffect.ShaderTexture.SetResource(Texture);
            UIBasicEffect.SampleType.SetResource(Sampler);
            UIBasicEffect.BasicTexturedPass.Apply();
        }
        
        base.Render();
    }

    // Frees a live shared-surface import this component owns (reached via the deferred queue, so no frame still samples
    // it); bitmap textures belong to their BitmapSource.
    protected override void Dispose(bool disposeManagedResources)
    {
        if (Texture is Adamantium.Graphics.SharedSurface shared) shared.Dispose();
        base.Dispose(disposeManagedResources);
    }
}

public class TextRenderComponent : ImageRenderComponent
{
    private IRenderTarget _renderTarget;

    // Render text into a supersampled target (this factor larger), then let it minify when composited onto
    // the control = SSAA. The real fix for small unhinted text: gives sub-pixel stems enough pixels.
    private const float TextSupersample = 1f;

    public TextRenderComponent(IGraphicsDevice device,
        UIBasicEffect uiBasicEffect,
        Mesh mesh,
        FontRenderer fontRenderer,
        TextLayout textLayout,
        TextRenderingParameters renderingParameters, 
        Brush background,
        Brush foreground,
        Brush stroke,
        GpuBufferManager bufferManager) : base(device, uiBasicEffect, mesh, background, bufferManager)
    {
        FontRenderer = fontRenderer;
        TextLayout = textLayout;
        RenderingParameters = renderingParameters;
        Foreground = foreground;
        Stroke = stroke;
        _rtWidth = (uint)(mesh.Bounds.Width * TextSupersample);
        _rtHeight = (uint)(mesh.Bounds.Height * TextSupersample);
        Sampler = SamplerStates.LinearFont;
    }

    private readonly uint _rtWidth, _rtHeight;

    // The block's private text target, created lazily since the batched and direct paths never use it. Supersampled by
    // TextSupersample in step with FontRenderer.RenderScale; no MSAA, MSDF glyphs anti-alias themselves.
    private IRenderTarget EnsureRenderTarget() => _renderTarget ??= ToDispose(GraphicsDevice.CreateRenderTarget(
        _rtWidth, _rtHeight, MSAALevel.None, SurfaceFormat.R8G8B8A8.UNorm, name: "TextRenderer"));
    
    public FontRenderer FontRenderer { get; }
    public TextLayout TextLayout { get; }
    public TextRenderingParameters RenderingParameters { get; }
    public Brush Foreground { get; set; }

    public Brush Stroke { get; set; }

    // The frozen glyph snapshot BOTH text paths bake from (set by TextRenderUnit after each TextLayout.Update): the batch
    // packs it into the shared SSBO, the direct/composite fallback uploads it into this component's own vertex buffer
    // (EnsureGlyphVtx). Neither reads the live, reshaped-in-place TextLayout at draw, so the whole text draw path is a pure
    // function of the frozen snapshot - render-thread safe. Null only before the first snapshot.
    private FrozenGlyphRun _glyphRun;
    public FrozenGlyphRun GlyphRun
    {
        get => _glyphRun;
        set { _glyphRun = value; _glyphVtxDirty = true; }
    }

    // Per-block vertex buffer for the DIRECT/composite draw, uploaded from the FROZEN glyph run (never the live layout).
    // Lazily allocated (only if a block ever falls to the direct draw - batched text never touches it), reused + re-uploaded
    // when the run changes. Mirrors the old TextLayout.EnsureVertexBuffer/VertexBuffer that the direct path used to read.
    private Buffer<FontItem> _glyphVtx;
    private bool _glyphVtxDirty;

    private Buffer<FontItem> EnsureGlyphVtx()
    {
        _glyphVtx ??= ToDispose(Adamantium.Graphics.Buffer.Vertex.New<FontItem>(GraphicsDevice, 4096, Adamantium.Graphics.BufferMemoryUsage.UploadFromCpuToGpu));
        if (_glyphVtxDirty)
        {
            _glyphVtx.SetData(GlyphRun.Glyphs, 0, (uint)GlyphRun.Count, 0);
            _glyphVtxDirty = false;
        }
        return _glyphVtx;
    }

    private bool _textRendered = false;

    // Color-only change: swap the brushes and force one re-rasterization, reusing the existing render
    // target and geometry (no buffer/RT rebuild).
    public void UpdateColors(Brush background, Brush foreground, Brush stroke)
    {
        Background = background;
        Foreground = foreground;
        Stroke = stroke;
        _textRendered = false;
    }

    // Text-content change at the SAME size: the shared TextLayout was re-shaped in place and its glyph buffer
    // re-uploaded, so just reuse this render target + geometry and force one re-rasterization - no new component and no
    // Vulkan render-target allocation (the live-text / recycled-list-row fast path). Same body as UpdateColors; the name
    // marks the intent at the call site.
    public void UpdateText(Brush background, Brush foreground, Brush stroke)
    {
        Background = background;
        Foreground = foreground;
        Stroke = stroke;
        _textRendered = false;
    }

    // Rasterize the glyphs into the private text target BEFORE the main render pass begins (this runs in the
    // beforeRenderPass PreRender phase). So the main pass is never interrupted per text block - it only composites the
    // pre-rastered target. SetState/RestoreState use outerPassActive:false: there is no main pass to end/resume yet
    // (BeginDraw begins it right after PreRender), we just render this one target as a standalone pass.
    public override void PreRender()
    {
        // Direct main-pass draw: glyphs are drawn straight into the main pass in Render() - no private-RT
        // rasterization pre-pass at all. See FontRenderer.UseDirectTextDraw.
        if (FontRenderer.UseDirectTextDraw) 
            return;
        
        if (_textRendered)
            return;

        if (GlyphRun == null) return;

        // Inset the text by the effect padding inside the (padded) target so edge glyphs' outline/glow have room. The
        // composite quad was grown by the same pad with its origin shifted -pad (see RenderUnit), canceling this inset.
        var pad = GlyphRun.EffectPadding;
        var location = new Vector3F(RenderingParameters.TextArea.X + pad, RenderingParameters.TextArea.Y + pad, 5);
        var foreground = ((SolidColorBrush)Foreground).Color;
        var previousColor = GraphicsDevice.ClearColor;
        // Rasterize the (logical-size) layout RenderScale x larger into the target; the composite minifies it = SSAA.
        FontRenderer.RenderScale = TextSupersample;
        FontRenderer.SetState(SamplerStates.LinearFont, location, EnsureRenderTarget(), outerPassActive: false);
        FontRenderer.DrawLayout(EnsureGlyphVtx(), (uint)GlyphRun.Count, GlyphRun.Atlas, GlyphRun.FontSize, foreground);
        FontRenderer.RestoreState(outerPassActive: false);
        GraphicsDevice.ClearColor = previousColor;
        _textRendered = true;
    }

    public override void Render()
    {
        if (FontRenderer.UseDirectTextDraw)
        {
            RenderDirect();
            return;
        }

        // The glyphs were rasterized into the private target in PreRender (before the main pass); here we only composite it.
        Texture = EnsureRenderTarget().ResolveTexture;
        Sampler = SamplerStates.LinearClampToEdge;
        // The text target holds premultiplied color (the font shaders output rgb*alpha, rendered with a premultiplied
        // blend), so composite it with a premultiplied blend too - a straight AlphaBlend would darken the edges (rim).
        ColorBlendEquation = ColorBlendEquations.Premultiplied;
        base.Render();
    }

    // Draws one block's glyphs straight into the main pass: local + TextArea, then world and projection (row vectors),
    // at RenderScale 1.
    private void RenderDirect()
    {
        if (GlyphRun == null) return;
        var textArea = RenderingParameters.TextArea;
        var mvp = Matrix4x4F.Translation(textArea.X, textArea.Y, 5)
                  * RenderData.TransformMatrix
                  * RenderData.ProjectionMatrix;
        var foreground = ((SolidColorBrush)Foreground).Color;
        FontRenderer.RenderScale = 1f;
        FontRenderer.DrawLayoutDirect(
            SamplerStates.LinearFont,
            EnsureGlyphVtx(),
            (uint)GlyphRun.Count,
            GlyphRun.Atlas,
            GlyphRun.FontSize,
            foreground,
            mvp,
            RenderData.Opacity,
            RenderData.RoundedClipBox,      // the rounded ancestor clip the batched path gets from the table by slot
            RenderData.RoundedClipRadii);
    }
}