using Adamantium.Graphics.Fonts;
using Adamantium.Mathematics;
using Adamantium.UI.Core.Media;

namespace Adamantium.UI.Controls.Text;

/// <summary>A geometry as the <see cref="TextShape"/> text is set inside or flows round: its figures closed, kept
/// while the geometry and the padding stay as they were.</summary>
internal sealed class GeometryTextShape
{
    private Geometry _geometry;
    private int _version = -1;
    private double _padding = double.NaN;
    private TextShape _shape;

    /// <summary>The shape of <paramref name="geometry"/> padded by <paramref name="padding"/>; null for no geometry or
    /// one without a figure.</summary>
    public TextShape Of(Geometry geometry, double padding)
    {
        if (geometry == null)
        {
            _geometry = null;
            _shape = null;
            return null;
        }

        if (ReferenceEquals(geometry, _geometry) && geometry.Version == _version && padding.Equals(_padding))
        {
            return _shape;
        }

        var contours = new List<Vector2<double>[]>();
        foreach (var (points, _) in geometry.GetFigures())
        {
            if (points.Length >= 3)
            {
                contours.Add(points);
            }
        }

        _geometry = geometry;
        _version = geometry.Version;
        _padding = padding;
        _shape = contours.Count > 0 ? new TextShape(contours, padding) : null;
        return _shape;
    }
}
