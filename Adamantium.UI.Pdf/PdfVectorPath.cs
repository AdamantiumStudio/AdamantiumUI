using Adamantium.Mathematics;
using Adamantium.Pdf.Document;
using Adamantium.Pdf.Writing;
using Adamantium.UI.Core.Graphics.Vector;

namespace Adamantium.UI.Pdf;

/// <summary>A path written into PDF content, each point placed by <paramref name="map"/>; a quadratic curve as the cubic
/// that traces it, PDF having no other.</summary>
internal sealed class PdfVectorPath(PdfContentBuilder content, PdfMatrix map) : IVectorPath
{
    // Where the path stands, before the map: a quadratic's cubic handles are reckoned from it.
    private Vector2<double> _current;
    private Vector2<double> _figureStart;

    public PdfVectorPath(PdfContentBuilder content) : this(content, PdfMatrix.Identity)
    {
    }

    public void MoveTo(Vector2<double> point)
    {
        var at = map.Transform(point.X, point.Y);
        content.MoveTo(at.X, at.Y);
        _current = point;
        _figureStart = point;
    }

    public void LineTo(Vector2<double> point)
    {
        var at = map.Transform(point.X, point.Y);
        content.LineTo(at.X, at.Y);
        _current = point;
    }

    public void QuadraticTo(Vector2<double> control, Vector2<double> end) =>
        CubicTo(_current + (control - _current) * (2.0 / 3), end + (control - end) * (2.0 / 3), end);

    public void CubicTo(Vector2<double> first, Vector2<double> second, Vector2<double> end)
    {
        var one = map.Transform(first.X, first.Y);
        var two = map.Transform(second.X, second.Y);
        var three = map.Transform(end.X, end.Y);
        content.CurveTo(one.X, one.Y, two.X, two.Y, three.X, three.Y);
        _current = end;
    }

    public void Close()
    {
        content.ClosePath();
        _current = _figureStart;
    }
}
