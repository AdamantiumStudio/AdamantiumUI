namespace Adamantium.UI.Rendering;

/// <summary>Watches a <see cref="RenderCache"/> from outside: what each frame recorded and which recorded frames were
/// applied. A cache with no observer pays one null check per call.</summary>
internal interface IRenderCacheObserver
{
    /// <summary>A frame was recorded, on the recording thread, before the packet is handed over.</summary>
    void Recorded(RenderPacket packet);

    /// <summary>A recorded frame is being applied, on the drawing thread, before the packet goes back to the pool.</summary>
    void Applied(RenderPacket packet);
}
