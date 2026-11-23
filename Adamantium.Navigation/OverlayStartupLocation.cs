namespace Adamantium.Navigation;

/// <summary>Where an <c>OverlayWindow</c> first appears.</summary>
public enum OverlayStartupLocation
{
    /// <summary>Centered on the parent window, cascaded so multiple don't stack exactly (the default).</summary>
    CenterOwner,

    /// <summary>At the explicit <see cref="IOverlayAware.Left"/>/<see cref="IOverlayAware.Top"/>, in window coordinates.</summary>
    Manual,

    /// <summary>At the parent window's top-left corner: <see cref="IOverlayAware.Left"/> is the room from its left edge,
    /// <see cref="IOverlayAware.Top"/> from its top - the place <see cref="Manual"/> gives, named for its corner.</summary>
    TopLeft,

    /// <summary>At the parent window's top-right corner: <see cref="IOverlayAware.Left"/> is the room from its right edge,
    /// <see cref="IOverlayAware.Top"/> from its top. It keeps to that corner as the parent resizes, and a new Left or Top
    /// moves it from there, until it is dragged, resized or collapsed: from then on Left and Top are its top-left.</summary>
    TopRight,

    /// <summary>At the parent window's bottom-left corner: <see cref="IOverlayAware.Left"/> is the room from its left edge,
    /// <see cref="IOverlayAware.Top"/> from its bottom - kept to that corner as <see cref="TopRight"/> is to its own.</summary>
    BottomLeft,

    /// <summary>At the parent window's bottom-right corner: <see cref="IOverlayAware.Left"/> is the room from its right
    /// edge, <see cref="IOverlayAware.Top"/> from its bottom - kept to that corner as <see cref="TopRight"/> is to its own.
    /// </summary>
    BottomRight
}
