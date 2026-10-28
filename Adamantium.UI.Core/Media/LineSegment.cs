using Adamantium.Mathematics;

namespace Adamantium.UI.Core.Media;

public class LineSegment : PathSegment
{
    public LineSegment()
    {
        
    }

    public LineSegment(Vector2<double> point, bool isStroked)
    {
        Point = point;
        IsStroked = isStroked;
    }
    
    public static readonly AdamantiumProperty PointProperty =
        AdamantiumProperty.Register(nameof(Point), typeof(Vector2<double>), typeof(LineSegment),
            new PropertyMetadata(Vector2<double>.Zero, PropertyMetadataOptions.AffectsMeasure));
    
    public Vector2<double> Point
    {
        get => GetValue<Vector2<double>>(PointProperty);
        set => SetValue(PointProperty, value);
    }
    
    internal override Vector2<double>[] ProcessSegment(Vector2<double> currentPoint)
    {
        return new[] { Point };
    }
}