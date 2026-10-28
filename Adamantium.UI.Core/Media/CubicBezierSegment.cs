using Adamantium.Mathematics;

namespace Adamantium.UI.Core.Media;

public class CubicBezierSegment : BezierSegmentBase
{
    public CubicBezierSegment()
    {
        
    }

    public CubicBezierSegment(Vector2<double> controlPoint1, Vector2<double> controlPoint2, Vector2<double> point, bool isStroked)
    {
        ControlPoint1 = controlPoint1;
        ControlPoint2 = controlPoint2;
        Point = point;
        IsStroked = isStroked;
    }
    
    public static readonly AdamantiumProperty ControlPoint1Property =
        AdamantiumProperty.Register(nameof(ControlPoint1), typeof(Vector2<double>), typeof(CubicBezierSegment),
            new PropertyMetadata(Vector2<double>.Zero, PropertyMetadataOptions.AffectsMeasure));
    
    public static readonly AdamantiumProperty ControlPoint2Property =
        AdamantiumProperty.Register(nameof(ControlPoint2), typeof(Vector2<double>), typeof(CubicBezierSegment),
            new PropertyMetadata(Vector2<double>.Zero, PropertyMetadataOptions.AffectsMeasure));

    public Vector2<double> ControlPoint1
    {
        get => GetValue<Vector2<double>>(ControlPoint1Property); 
        set => SetValue(ControlPoint1Property, value);
    }
    
    public Vector2<double> ControlPoint2
    {
        get => GetValue<Vector2<double>>(ControlPoint2Property); 
        set => SetValue(ControlPoint2Property, value);
    }
    
    internal override Vector2<double>[] ProcessSegment(Vector2<double> currentPoint)
    {
        var dense = new List<Vector2<double>>();
        MathHelper.Curves.AppendCubicBezier(currentPoint, ControlPoint1, ControlPoint2, Point, 256, dense);
        return ResampledEvenly(dense);
    }
}