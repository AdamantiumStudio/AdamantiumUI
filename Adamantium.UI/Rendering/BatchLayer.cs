namespace Adamantium.UI.Rendering;

/// <summary>Where a batch is flushed among the others, bottom first. What a batch holds is drawn over everything a batch
/// lower in this order holds, so a draw that belongs over something still pending in a higher batch has to wait for a
/// flush.</summary>
internal enum BatchLayer
{
    /// <summary>The outer aura or shadow, under every fill.</summary>
    HaloUnder,

    /// <summary>The living outer aura, beside its still twin.</summary>
    HaloLivingUnder,

    Rect,
    Ellipse,
    Polygon,
    GradientRect,
    GradientEllipse,
    Pattern,
    Fractal,
    Texture,

    /// <summary>The canvas ground, after every other SDF fill.</summary>
    CanvasGrid,

    Ink,
    Arrow,
    Stroke,

    /// <summary>Tessellated fills, with their fringes and strokes.</summary>
    InstancedFill,

    /// <summary>Backdrop materials, last of the fills: they copy what is behind them out of the frame.</summary>
    Material,

    /// <summary>The inner band, over every fill and under text.</summary>
    HaloOver,

    /// <summary>The living inner band, beside its still twin.</summary>
    HaloLivingOver,

    Text
}
