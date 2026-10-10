namespace Adamantium.UI.Rendering;

/// <summary>Which way a <see cref="RenderCache"/> drew its last frame.</summary>
internal enum DrawPath
{
    /// <summary>Walked the retained groups and re-recorded the op stream.</summary>
    Walk,

    /// <summary>Replayed the recorded op stream unchanged (a clean frame).</summary>
    Replay,

    /// <summary>Rewrote the changed slots in place and replayed.</summary>
    Patch,

    /// <summary>Spliced changed groups into the recorded batches and replayed.</summary>
    Splice
}
