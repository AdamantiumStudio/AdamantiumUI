namespace Adamantium.UI.Core.Media;

/// <summary>What a material's shader does with its source, the second axis of <see cref="MaterialType"/>; acrylic and
/// mica share the frosted treatment.</summary>
public enum MaterialTreatment
{
    /// <summary>Blur, tint, grain over whatever picture the material brought - acrylic and mica.</summary>
    Frosted,

    /// <summary>The same picture bent like a lens, with a chromatic fringe and a bright rim - liquid glass.</summary>
    Glass,

    /// <summary>No picture at all: a lit surface, whose relief comes from a noise field and whose brightness comes from
    /// a grazing-angle sheen - velvet and the fabrics beside it.</summary>
    Sheen,

    /// <summary>The same lit surface with a metal's answer: a GGX lobe stretched along the grinding, reflecting a
    /// procedural studio environment rather than anything captured.</summary>
    Metal,

    /// <summary>A lit surface whose appearance is mostly FIGURE: annual rings drawn as color, with the light doing
    /// no more than varnishing them. The odd one of the branch - the other two are lighting models over a plain
    /// color, this one is a pattern that happens to be lit.</summary>
    Wood
}
