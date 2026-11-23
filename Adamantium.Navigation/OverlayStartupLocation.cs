namespace Adamantium.Navigation;

/// <summary>Where an <c>OverlayWindow</c> first appears.</summary>
public enum OverlayStartupLocation
{
    /// <summary>Centered on the parent window, cascaded so multiple don't stack exactly (the default).</summary>
    CenterOwner,

    /// <summary>At the explicit <see cref="IOverlayAware.Left"/>/<see cref="IOverlayAware.Top"/>, in window coordinates.</summary>
    Manual,

    /// <summary>At the parent window's top-right corner: <see cref="IOverlayAware.Left"/> is the room from its right edge,
    /// <see cref="IOverlayAware.Top"/> from its top. Once open, Left and Top are the window's place, as for any other.</summary>
    TopRight
}
