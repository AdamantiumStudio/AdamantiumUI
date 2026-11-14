using Adamantium.Mathematics;

namespace Adamantium.UI.Controls.Canvases;

/// <summary>What the plane is drawn through: how many units of the drawing one world unit is, and where a world point
/// lands. A canvas is one; <see cref="CanvasPicture"/> draws through a plane of its own, one to one.</summary>
internal interface ICanvasCamera
{
    /// <summary>How many units of the drawing one world unit is.</summary>
    double Scale { get; }

    /// <summary>Whether what is drawn lands on pixels, so a text is put on a whole one to read sharp.</summary>
    bool SnapsToPixels { get; }

    /// <summary>Where a world point lands in the drawing.</summary>
    Vector2<double> WorldToScreen(Vector2<double> world);
}
