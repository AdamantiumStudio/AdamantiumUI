using System.Runtime.InteropServices;
using Adamantium.Graphics;
using Adamantium.Graphics.Core;
using Adamantium.Mathematics;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Media;
using Adamantium.UI.Core.Media.Curves;
using Adamantium.UI.FX;
using Adamantium.Vulkan.Core;

namespace Adamantium.UI.Rendering.RenderUnits;

/// <summary>Draws a <see cref="ColoredMesh"/>: its corners are read by address in the vertex stage - no vertex format of
/// their own - and each triangle's color runs between them; a mesh with an outline is cut to it by the curve coverage.
/// </summary>
public sealed class ColoredMeshRenderComponent : UIRenderComponent
{
    private const MemoryPropertyFlags MeshMemory = MemoryPropertyFlags.HostVisible | MemoryPropertyFlags.DeviceLocal;

    private readonly Rect _bounds;
    private readonly ReusableBuffer _buffer;
    private readonly ColoredMeshVertexRecord[] _vertices;
    private readonly uint[] _outlineWords;
    private readonly ReusableBuffer _outlineBuffer;
    private readonly bool _outlineEnclosesNothing;

    /// <summary>A mesh cut to <paramref name="outline"/>, or left with its triangles' edges when it is null; an outline
    /// that encloses nothing leaves nothing drawn.</summary>
    internal ColoredMeshRenderComponent(IGraphicsDevice device, UIBasicEffect uiBasicEffect, ColoredMesh mesh,
        PathCurves outline, GpuBufferManager bufferManager) : this(device, uiBasicEffect, mesh, bufferManager)
    {
        if (outline == null)
        {
            return;
        }

        var packed = Retained.CurveFill.From(outline);
        if (packed == null)
        {
            _outlineEnclosesNothing = true;
            return;
        }

        _outlineWords = packed.Words;
        _outlineBuffer = ToDispose(BufferManager.CreateBuffer(
            BufferUsageFlags.StorageBuffer | BufferUsageFlags.ShaderDeviceAddress, MeshMemory, "colored mesh outline"));
        _outlineBuffer.Reserve(CountOutlineBytes());
        _outlineBuffer.Invalidate();
    }

    public ColoredMeshRenderComponent(IGraphicsDevice device, UIBasicEffect uiBasicEffect, ColoredMesh mesh,
        GpuBufferManager bufferManager) : base(device, uiBasicEffect, null, bufferManager)
    {
        _bounds = mesh.Bounds;
        _vertices = new ColoredMeshVertexRecord[mesh.Vertices.Count];
        for (var index = 0; index < _vertices.Length; index++)
        {
            var vertex = mesh.Vertices[index];
            _vertices[index] = new ColoredMeshVertexRecord
            {
                Position = new Vector2F((float)vertex.Position.X, (float)vertex.Position.Y),
                Color = vertex.Color.ToVector4(),
            };
        }

        _buffer = ToDispose(BufferManager.CreateBuffer(BufferUsageFlags.StorageBuffer | BufferUsageFlags.ShaderDeviceAddress,
            MeshMemory, "colored mesh"));
        _buffer.Reserve(CountBytes());
        _buffer.Invalidate();
    }

    private ulong CountBytes() => (ulong)(_vertices.Length * Marshal.SizeOf<ColoredMeshVertexRecord>());

    private ulong CountOutlineBytes() => (ulong)(_outlineWords.Length * sizeof(uint));

    public override void Render()
    {
        if (_vertices.Length == 0 || _outlineEnclosesNothing)
        {
            return;
        }

        var buffer = _buffer.Acquire(CountBytes(), out var write);
        if (write)
        {
            buffer.SetData(_vertices, 0, (uint)_vertices.Length);
        }

        UIBasicEffect.Wvp.SetValue(RenderData.TransformMatrix * RenderData.ProjectionMatrix);
        UIBasicEffect.Opacity.SetValue(RenderData.Opacity);
        UIBasicEffect.MeshVertices.SetValue(buffer.GetDeviceAddress());
        UIBasicEffect.MeshTriangleCount.SetValue((uint)(_vertices.Length / 3));
        UIBasicEffect.MeshOutlined.SetValue(_outlineBuffer != null ? 1u : 0u);
        if (_outlineBuffer != null)
        {
            var outline = _outlineBuffer.Acquire(CountOutlineBytes(), out var writeOutline);
            if (writeOutline)
            {
                outline.SetData(_outlineWords, 0, (uint)_outlineWords.Length);
            }

            UIBasicEffect.CurveData.SetValue(outline.GetDeviceAddress());
            UIBasicEffect.CurveTiled.SetValue(Retained.InstancedFillCollector.CurveTilesEnabled ? 1u : 0u);
        }

        UIBasicEffect.BasicColoredMeshPass.Apply();

        GraphicsDevice.VertexType = null;   // the corners are read by address
        GraphicsDevice.PolygonMode = PolygonMode.Fill;
        GraphicsDevice.PrimitiveTopology = PrimitiveTopology.TriangleList;
        GraphicsDevice.ColorBlendEquation = ColorBlendEquations.AlphaBlend;
        GraphicsDevice.DepthTestEnabled = true;
        if (_outlineBuffer != null && GraphicsDevice.HasDepthAttachment &&
            TryDeviceBounds((float)_bounds.X, (float)_bounds.Y, (float)_bounds.Right, (float)_bounds.Bottom,
                out var scratch))
        {
            DrawTopmostOnce(scratch);
            return;
        }

        GraphicsDevice.DepthCompareFunction = CompareOp.Always;
        GraphicsDevice.DepthWriteEnable = true;
        GraphicsDevice.Draw((uint)_vertices.Length, 1);
    }

    private void DrawTopmostOnce(Rect2D scratch)
    {
        DepthUnion.Settle(GraphicsDevice, scratch);
        GraphicsDevice.Draw((uint)_vertices.Length, 1);
        DepthUnion.Lay(GraphicsDevice);
        GraphicsDevice.Draw((uint)_vertices.Length, 1);
        DepthUnion.Finish(GraphicsDevice);
    }
}
