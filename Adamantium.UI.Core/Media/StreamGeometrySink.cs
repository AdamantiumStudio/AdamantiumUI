using Adamantium.Mathematics;
using Adamantium.Mathematics.Svg;

namespace Adamantium.UI.Core.Media;

internal sealed class StreamGeometrySink : ISvgPathSink
{
    private readonly StreamGeometryContext _context;
    private IFigureSegments _figure;

    public StreamGeometrySink(StreamGeometryContext context)
    {
        _context = context;
    }

    public void MoveTo(Vector2<double> point) => _figure = _context.BeginFigure(point, true, false);

    public void LineTo(Vector2<double> point) => _figure.LineTo(point);

    public void CubicTo(Vector2<double> first, Vector2<double> second, Vector2<double> end) => _figure.CubicBezierTo(first, second, end);

    public void QuadraticTo(Vector2<double> control, Vector2<double> end) => _figure.QuadraticBezierTo(control, end);

    public void ArcTo(double radiusX, double radiusY, double rotation, bool largeArc, bool sweep, Vector2<double> end) =>
        _figure.ArcTo(end, new Size<double>(radiusX, radiusY), rotation, largeArc,
            sweep ? SweepDirection.Clockwise : SweepDirection.CounterClockwise, true);

    public void Close() => _figure.CloseFigure();
}
