namespace Adamantium.UI.Core;

/// <summary>Where a visual stands between being built and released; only one state allows undoing its subscriptions.
/// <see cref="Detaching"/> means teardown has started but nothing may be released yet.</summary>
public enum VisualLifecycle
{
    /// <summary>In use. Either in the tree, or briefly out of it and coming straight back (a re-parent).</summary>
    Live,

    /// <summary>A teardown has begun on it, and it may still be taking part in the rebuild that replaces it. Nothing
    /// may be released here - this is the state that says "marked, but not yet finished with".</summary>
    Detaching,

    /// <summary>Deliberately out of the tree and coming back through the SAME host - a view that asked to be kept
    /// (<c>x:KeepAlive</c>). Releasing it is a bug: it is not dead, it is waiting. See <c>ParkedVisuals</c>.</summary>
    Parked,

    /// <summary>A generated item container sitting in its generator's pool, to be REUSED for another item. Like
    /// <see cref="Parked"/> it is not dead, but it comes back a different way - re-bound to a new item rather than
    /// resumed where it left off.</summary>
    Recycled,

    /// <summary>Destroyed for good. The ONLY state in which what holds this visual may let go of it, and the only one
    /// this enum treats as final.</summary>
    Discarded,
}
