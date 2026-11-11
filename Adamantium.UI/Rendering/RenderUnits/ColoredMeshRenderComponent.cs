using System.Runtime.InteropServices;
using Adamantium.Graphics;
using Adamantium.Graphics.Core;
using Adamantium.Mathematics;
using Adamantium.UI.Core.Media;
using Adamantium.UI.FX;
using Adamantium.Vulkan.Core;

namespace Adamantium.UI.Rendering.RenderUnits;

/// <summary>Draws a <see cref="ColoredMesh"/>: its corners are read by address in the vertex stage - no vertex format of
/// their own - and each triangle's color runs between them.</summary>
public sealed class ColoredMeshRenderComponent : UIRenderComponent
{
    private const MemoryPropertyFlags MeshMemory = MemoryPropertyFlags.HostVisible | MemoryPropertyFlags.DeviceLocal;

    private readonly ReusableBuffer _buffer;
    private readonly ColoredMeshVertexRecord[] _vertices;

    public ColoredMeshRenderComponent(IGraphicsDevice device, UIBasicEffect uiBasicEffect, ColoredMesh mesh,
        GpuBufferManager bufferManager) : base(device, uiBasicEffect, null, bufferManager)
    {
        _vertices = new ColoredMeshVertexRecord[mesh.Vertices.Count];
        for (var i = 0; i < _vertices.Length; i++)
        {
            var vertex = mesh.Vertices[i];
            _vertices[i] = new ColoredMeshVertexRecord
            {
                Position = new Vector2F((float)vertex.Position.X, (float)vertex.Position.Y),
                Color = vertex.Color.ToVector4(),
            };
        }

        _buffer = ToDispose(BufferManager.CreateBuffer(BufferUsageFlags.StorageBuffer | BufferUsageFlags.ShaderDeviceAddress,
            MeshMemory, "colored mesh"));
        _buffer.Reserve(Bytes);
        _buffer.Invalidate();
    }

    private ulong Bytes => (ulong)(_vertices.Length * Marshal.SizeOf<ColoredMeshVertexRecord>());

    public override void Render()
    {
        if (_vertices.Length == 0)
        {
            return;
        }

        var buffer = _buffer.Acquire(Bytes, out var write);
        if (write)
        {
            buffer.SetData(_vertices, 0, (uint)_vertices.Length);
        }

        UIBasicEffect.Wvp.SetValue(RenderData.TransformMatrix * RenderData.ProjectionMatrix);
        UIBasicEffect.Opacity.SetValue(RenderData.Opacity);
        UIBasicEffect.MeshVertices.SetValue(buffer.GetDeviceAddress());
        UIBasicEffect.BasicColoredMeshPass.Apply();

        GraphicsDevice.VertexType = null;   // the corners are read by address
        GraphicsDevice.PolygonMode = PolygonMode.Fill;
        GraphicsDevice.PrimitiveTopology = PrimitiveTopology.TriangleList;
        GraphicsDevice.ColorBlendEquation = ColorBlendEquations.AlphaBlend;
        GraphicsDevice.DepthCompareFunction = CompareOp.Always;
        GraphicsDevice.DepthTestEnabled = true;
        GraphicsDevice.DepthWriteEnable = true;
        GraphicsDevice.Draw((uint)_vertices.Length, 1);
    }
}
