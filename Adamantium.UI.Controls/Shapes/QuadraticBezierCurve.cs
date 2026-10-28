using System.Linq;
using Adamantium.Mathematics;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Graphics;
using Adamantium.UI.Core.Media;

namespace Adamantium.UI.Controls.Shapes;

public class QuadraticBezierCurve : BezierCurveBase
{
    public QuadraticBezierCurve()
    {

    }

    public static readonly AdamantiumProperty ControlPointProperty =
        AdamantiumProperty.Register(nameof(ControlPoint), typeof(Vector2<double>), typeof(QuadraticBezierCurve),
            new PropertyMetadata(Vector2<double>.Zero, PropertyMetadataOptions.AffectsMeasure));
    
    public Vector2<double> ControlPoint
    {
        get => GetValue<Vector2<double>>(ControlPointProperty); 
        set => SetValue(ControlPointProperty, value);
    }
    
    protected override void OnRender(IDrawingContext context)
    {
        // Samples fully controls curvature (count 2 -> straight chord). FRESH geometry (see CubicBezierCurve).
        var dense = new List<Vector2<double>>();
        MathHelper.Curves.AppendQuadraticBezier(StartPoint, ControlPoint, EndPoint, 256, dense);
        var pts = ResampledEvenly(dense, Samples);
        var geometry = new StreamGeometry();
        geometry.Open().BeginFigure(pts[0], false, false).PolylineLineTo(pts.Skip(1), true);

        // Open, stroke-only curve: fill with Fill (null = none), not Stroke, and leave the figure open.
        context.ForControl(this).DrawGeometry(Fill, geometry, GetPen());
    }
}