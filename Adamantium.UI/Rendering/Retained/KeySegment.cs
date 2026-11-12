using Adamantium.Graphics.Core;
using Adamantium.UI.Core.Media;
using Adamantium.Vulkan.Core;
using Buffer = Adamantium.Graphics.Buffer;

namespace Adamantium.UI.Rendering.Retained;

/// <summary>What <see cref="InstancedFillCollector"/> keeps for one shared geometry: its mesh or curves on the GPU and
/// the instances of every brush family collected for it this frame. The mesh buffers are exact-size and written once
/// (another mesh is another key); the instance buffers are rewritten each frame, one per frame in flight, and grown only
/// when a frame begins.</summary>
internal sealed class KeySegment
{
    public Buffer VtxBuffer, IdxBuffer;
    public uint VertexCount, IndexCount;
    public bool Indexed;
    public PrimitiveType Topology;
    public bool MeshUploaded;

    /// <summary>The shared analytic-AA ring of the mesh: every instance draws it with the body's instance buffer.</summary>
    public Buffer RingBuffer;
    public uint RingVertexCount;

    /// <summary>A curve fill's packed curves (see <see cref="CurveFill"/>), in place of a mesh: one quad per instance.
    /// </summary>
    public Buffer CurveBuffer;

    /// <summary>When, in stopwatch ticks, the key was last asked for: a key is kept by being used.</summary>
    public long LastUsed;

    /// <summary>The device memory its mesh, ring and curve buffers hold - what the budget counts.</summary>
    public long MeshBytes;

    /// <summary>Solid instances: appended this frame across its flushes, and how many are drawn already.</summary>
    public GeometryInstance[] Items = new GeometryInstance[64];
    public int Count;
    public int Flushed;

    /// <summary>One instance buffer per frame in flight; <see cref="Gpu"/> is the copy this frame writes and draws.
    /// </summary>
    public Buffer[] GpuRing, GradGpuRing, PatGpuRing;
    public Buffer Gpu;
    public int GpuCapacity;

    /// <summary>Grown this frame: a fresh buffer holds no earlier bytes, so skipping a clean write is unsafe.</summary>
    public bool Recreated;

    /// <summary>Listed among the collector's pending keys.</summary>
    public bool InPending;

    /// <summary>Gradient instances of the same geometry.</summary>
    public GradientGeometryInstance[] GradItems = new GradientGeometryInstance[16];
    public int GradCount;
    public int GradFlushed;
    public Buffer GradGpu;
    public int GradGpuCapacity;
    public bool GradRecreated;

    /// <summary>Pattern and noise instances of the same geometry.</summary>
    public PatternGeometryInstance[] PatItems = new PatternGeometryInstance[16];
    public int PatCount;
    public int PatFlushed;
    public Buffer PatGpu;
    public int PatGpuCapacity;
    public bool PatRecreated;

    /// <summary>Textured instances of the same geometry. The texture is not part of the key: one is bound per draw, so a
    /// texture change ends a run, not the segment.</summary>
    public TextureGeometryInstance[] TexItems = new TextureGeometryInstance[16];
    public int TexCount;
    public int TexFlushed;
    public Buffer[] TexGpuRing;
    public Buffer TexGpu;
    public int TexGpuCapacity;
    public bool TexRecreated;

    /// <summary>What the textured instances appended since the last run all sample, and how.</summary>
    public ITexture PendingTexture;
    public SamplerState PendingSampler;

    /// <summary>Backdrop material instances of the same geometry, in the pattern record both feed.</summary>
    public PatternGeometryInstance[] MatItems = new PatternGeometryInstance[16];
    public int MatCount;
    public int MatFlushed;
    public Buffer[] MatGpuRing;
    public Buffer MatGpu;
    public int MatGpuCapacity;
    public bool MatRecreated;

    /// <summary>What the material instances appended since the last run agree about: the image bound to them, the pass
    /// that reads it, the frame region captured, a picture of the author's own and what it is pinned to.</summary>
    public bool MatPending;
    public bool MatWallpaper;
    public MaterialTreatment MatTreatment;
    public Rect2D MatRegion;
    public ITexture MatSource;
    public MaterialAnchor MatAnchor;
}
