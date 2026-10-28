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