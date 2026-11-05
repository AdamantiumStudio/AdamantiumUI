using System;
using Adamantium.Graphics;
using Adamantium.Graphics.Core;

namespace Adamantium.UI.Rendering;

/// <summary>
/// Rents <see cref="ReusableBuffer"/>s (frames-in-flight rings with high-water capacity) to render units, so changing
/// geometry rewrites in place instead of allocating. One per renderer; renters own and dispose their buffers.
/// </summary>
public sealed class GpuBufferManager
{
    private readonly IGraphicsDevice _device;
    private readonly uint _ringSize;

    public GpuBufferManager(IGraphicsDevice device)
    {
        _device = device;
        // One buffer per in-flight frame so the slot written this frame is never the one a previous frame still reads.
        _ringSize = Math.Max(1u, _device.MaxFramesInFlight);
    }

    internal IGraphicsDevice Device => _device;
    internal uint RingSize => _ringSize;
    internal uint CurrentFrame => _device.CurrentFrame;

    /// <summary>A buffer for <paramref name="usage"/>; <paramref name="name"/> is what a count of device memory calls
    /// its buffers.</summary>
    public ReusableBuffer CreateBuffer(BufferUsageFlags usage, MemoryPropertyFlags memory, string name = null)
        => new(this, usage, memory, name);
}
