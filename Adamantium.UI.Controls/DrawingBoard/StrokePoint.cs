using Adamantium.Mathematics;

namespace Adamantium.UI.Controls.DrawingBoard;

/// <summary>One point of a stroke: where the pen was, and how hard it was pressed, as a share of the ink's width - 1 for
/// the width itself, which a mouse, a finger and a pen at its usual pressure draw with; a pen pressing lighter draws
/// narrower and one pressing harder wider.</summary>
public readonly struct StrokePoint(Vector2F at, float pressure = 1f)
{
    /// <summary>Where the pen was, as an offset from the stroke's own <see cref="StrokeItem.Origin"/>.</summary>
    public Vector2F At { get; } = at;

    public float Pressure { get; } = pressure;
}
