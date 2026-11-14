using Adamantium.Graphics.Core;
using Adamantium.Mathematics;
using Adamantium.UI.Core;
using Adamantium.Vulkan.Core;

namespace Adamantium.UI.Rendering;

/// <summary>A batch that holds draws back until it is flushed. Whatever is drawn later over what it holds has to wait for
/// that flush, or it lands under draws that came before it.</summary>
internal interface IPendingBatch
{
    /// <summary>Where the batch is flushed among the others.</summary>
    BatchLayer Layer { get; }

    /// <summary>Which <see cref="RenderOp.Batch"/> a recorded op that draws this batch names.</summary>
    byte BatchId { get; }

    /// <summary>Whether these window-logical bounds overlap what the batch is still holding.</summary>
    bool OverlapsPending(Rect bounds);

    /// <summary>Draws what the batch holds and returns the recorded segment, or -1 when it held nothing. Leaves the
    /// device on <paramref name="fullScissor"/>.</summary>
    int Flush(IGraphicsDevice device, Rect2D fullScissor, Matrix4x4F projection);
}
