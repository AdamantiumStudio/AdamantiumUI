using Adamantium.Mathematics;

namespace Adamantium.UI.Core.Media;

public class QuadraticBezierSegment : BezierSegmentBase
{
    public QuadraticBezierSegment()
    {
        
    }

    public QuadraticBezierSegment(Vector2<double> controlPoint, Vector2<double> point, bool isStroked)
    {
        ControlPoint = controlPoint;
        Point = point;
        IsStroked = isStroked;
    }
    
    public static readonly AdamantiumProperty ControlPointProperty =
        AdamantiumProperty.Register(nameof(ControlPoint), typeof(Vector2<double>), typeof(QuadraticBezierSegment),
            new PropertyMetadata(Vector2<double>.Zero, PropertyMetadataOptions.AffectsMeasure));

    public Vector2<double> ControlPoint
    {
        get => GetValue<Vector2<double>>(ControlPointProperty);
        set => SetValue(ControlPointProperty, value);
    }
    
    internal override Vector2<double>[] ProcessSegment(Vector2<double> currentPoint)
    {
        var dense = new List<Vector2<double>>();
        MathHelper.Curves.AppendQuadraticBezier(currentPoint, ControlPoint, Point, 256, dense);
        return ResampledEvenly(dense);
    }
}