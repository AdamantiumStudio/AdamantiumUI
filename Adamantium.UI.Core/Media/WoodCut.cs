namespace Adamantium.UI.Core.Media;

/// <summary>How the board was sawn from the log, which sets where the ring core lies relative to its face and so the
/// figure it shows.</summary>
public enum WoodCut
{
    /// <summary>PLAIN SAWN, the cheap cut and the common one: the plane runs beside the core without meeting it, so it
    /// slices the cylinders lengthways and the rings open into the nested arches - the "cathedral" - that most people
    /// picture when they picture wood.</summary>
    Flat,

    /// <summary>QUARTER SAWN: the plane passes THROUGH the core, cutting every ring square on, so they land as narrow
    /// evenly spaced lines running the length of the board. Wasteful of the log and prized for it - it is the striped
    /// oak of furniture and instrument tops.</summary>
    Quarter,

    /// <summary>END GRAIN: the plane is across the trunk, so the cylinders show as what they are - concentric rings
    /// about the core. A butcher's block, or the end of a beam.</summary>
    End,

    /// <summary>BURL: a growth of dormant buds where the grain has no direction left at all and the rings knot around
    /// each other. The one figure that is not a clean cut through an orderly log.</summary>
    Burl
}
