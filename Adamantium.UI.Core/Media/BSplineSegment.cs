using Adamantium.Mathematics;
using Adamantium.UI.Core.Collections;

namespace Adamantium.UI.Core.Media;

public class BSplineSegment : PolylineSegment
{
    public BSplineSegment()
    {
        
    }

    public BSplineSegment(IEnumerable<Vector2<double>> points, bool isStroked)
    {
        Points = new PointsCollection(points);
        IsStroked = isStroked;
    }
    
    internal override Vector2<double>[] ProcessSegment(Vector2<double> currentPoint)
    {
        Vector2<double>[] controls = [currentPoint, .. Points];
        var dense = new List<Vector2<double>>();
        MathHelper.Curves.AppendQuadraticBSpline<double>(controls, 256, dense);
        return ResampledEvenly(dense);
    }

    // Each span of a quadratic B-spline is a quadratic Bezier: from the middle of one control leg to the middle of the
    // next, its control the control between.
    internal override void AppendCurves(Curves.PathCurveBuilder builder)
    {
        Vector2<double>[] controls = [builder.Current, .. Points];
        if (controls.Length < 3)
        {
            return;
        }

        builder.LineTo((controls[0] + controls[1]) * 0.5);
        for (var piece = 1; piece < controls.Length - 1; piece++)
        {
            builder.QuadraticTo(controls[piece], (controls[piece] + controls[piece + 1]) * 0.5);
        }
    }
    
    protected double CalculatePointsLength(PointsCollection points)
    {
        double cumulativeLength = 0;
        for (int i = 0; i < points.Count - 1; i++)
        {
            var vector = points[i + 1] - points[i];
            cumulativeLength += vector.Length();
        }

        return cumulativeLength / points.Count;
    }
}