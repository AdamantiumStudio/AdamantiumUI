namespace Adamantium.UI.Rendering;

/// <summary>Which of an element's bands a draw lays down; the others are written transparent, so the records stay the
/// same in number and a repaint finds them where they were.</summary>
internal enum HaloPick
{
    /// <summary>All of them.</summary>
    All,

    /// <summary>Its shadows alone - drawn apart, under a reflection that lies over them.</summary>
    Shadows,

    /// <summary>All but its shadows - its glow, drawn with what it draws.</summary>
    Glows,
}
