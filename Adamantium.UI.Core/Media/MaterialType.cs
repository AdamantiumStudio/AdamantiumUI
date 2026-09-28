namespace Adamantium.UI.Core.Media;

/// <summary>What a <see cref="MaterialBrush"/> is made of: a source (a capture, the wallpaper, or none for surfaces like
/// <see cref="Velvet"/>) plus a <see cref="MaterialTreatment"/>.</summary>
public enum MaterialType
{
    /// <summary>Frosted glass over what is DIRECTLY behind the element in this frame: blur, tint, grain. Follows the
    /// content beneath it - scroll something under an acrylic panel and the panel shows it moving.</summary>
    Acrylic,

    /// <summary>A tinted, heavily blurred desktop wallpaper, blurred once and cheap enough for a whole window; falls back
    /// to a tint. Frozen while the window is dragged (see RenderCache.WindowOnDesktop).</summary>
    Mica,

    /// <summary>A LENS, not frosting: the capture is sampled with an offset that grows towards the shape's edge, so what
    /// is behind bends the way it does through a thick drop of glass, with a chromatic fringe and a bright rim.
    /// Refraction at zero degrades gracefully into plain acrylic - the two are ends of one range.</summary>
    LiquidGlass,

    /// <summary>A napped surface lit with a sheen BRDF (<c>KHR_materials_sheen</c>), brightest at grazing angles, with a
    /// noise-gradient relief; no capture.</summary>
    Velvet,

    /// <summary>Brushed metal: an anisotropic GGX lobe reflecting a procedural studio gradient. Metals differ by
    /// <see cref="MaterialBrush.MetalColor"/>, roughness and anisotropy.</summary>
    Metal,

    /// <summary>Wood figure: a plane cut through annual rings, colored between <see cref="MaterialBrush.EarlyWoodColor"/>
    /// and <see cref="MaterialBrush.LateWoodColor"/>.</summary>
    Wood
}
