namespace Adamantium.UI.Core.Graphics;

public interface IRenderCachePolicy
{
    bool RequiresBufferRebuild(IRenderCachePolicy newState);

    /// <summary>What the draw shows, by identity, when its buffers are worth keeping across a re-record: a draw of the
    /// same content goes back to the unit that already holds it, wherever it now stands among the element's draws. Null
    /// when the draw is matched by its place alone.</summary>
    object Content => null;
}