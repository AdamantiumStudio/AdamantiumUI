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

    public void MoveTo(Vector2 point) => _figure = _context.BeginFigure(point, true, false);

    public void LineTo(Vector2 point) => _figure.LineTo(point);

    public void CubicTo(Vector2 first, Vector2 second, Vector2 end) => _figure.CubicBezierTo(first, second, end);

    public void QuadraticTo(Vector2 control, Vector2 end) => _figure.QuadraticBezierTo(control, end);

    public void ArcTo(double radiusX, double radiusY, double rotation, bool largeArc, bool sweep, Vector2 end) =>
        _figure.ArcTo(end, new Size(radiusX, radiusY), rotation, largeArc,
            sweep ? SweepDirection.Clockwise : SweepDirection.CounterClockwise, true);

    public void Close() => _figure.CloseFigure();
}
