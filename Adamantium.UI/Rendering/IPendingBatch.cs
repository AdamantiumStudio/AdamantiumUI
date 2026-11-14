using Adamantium.UI.Core;

namespace Adamantium.UI.Rendering;

/// <summary>A batch that holds draws back until it is flushed. Whatever is drawn later over what it holds has to wait for
/// that flush, or it lands under draws that came before it.</summary>
internal interface IPendingBatch
{
    /// <summary>Whether these window-logical bounds overlap what the batch is still holding.</summary>
    bool OverlapsPending(Rect bounds);
}
