using Adamantium.Mathematics;

namespace Adamantium.UI.Core.Media;

public abstract class BezierSegmentBase : PathSegment
{
    public static readonly AdamantiumProperty PointProperty =
        AdamantiumProperty.Register(nameof(Point), typeof(Vector2<double>), typeof(BezierSegmentBase),
            new PropertyMetadata(Vector2<double>.Zero, PropertyMetadataOptions.AffectsMeasure));

    public Vector2<double> Point
    {
        get => GetValue<Vector2<double>>(PointProperty);
        set => SetValue(PointProperty, value);
    }
    
    protected double CalculatePointsLength(Vector2<double>[] points)
    {
        double cumulativeLength = 0;
        for (int i = 0; i < points.Length - 1; i++)
        {
            var vector = points[i + 1] - points[i];
            cumulativeLength += vector.Length();
        }

        return cumulativeLength;
    }
}