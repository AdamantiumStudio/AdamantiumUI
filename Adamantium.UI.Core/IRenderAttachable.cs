namespace Adamantium.UI.Core;

/// <summary>A value that needs to know which elements draw with it; notified when an <c>AffectsRender</c> property takes
/// or releases it.</summary>
public interface IRenderAttachable
{
    /// <summary>An element just took this value for a render property.</summary>
    void AttachTo(AdamantiumComponent owner);

    /// <summary>An element just gave it up.</summary>
    void DetachFrom(AdamantiumComponent owner);
}
