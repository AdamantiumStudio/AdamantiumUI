using System;
using System.Globalization;
using System.Text;
using Adamantium.Graphics.Fonts;
using Adamantium.MVVM;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Media;

namespace Adamantium.UI.Sandbox.ViewModels;

// The "Text in a shape" topic: one text, set inside every shape of the topic and flowing round some; the shapes made
// here, each about the middle of its 280 by 280 box, as large as the scale says - the polygon with as many corners as
// asked for.
public partial class TextViewModel
{
    [Bindable] private string _shapeText =
        "Текст заполняет фигуру строка за строкой: короткие строки наверху и внизу круга, длинные посередине, " +
        "по обе стороны от дыры в кольце. Text fills the shape row by row, each row taking the stretches that lie " +
        "inside it all the way down, and goes on round the next shape the same way. Вёрстка в произвольной фигуре " +
        "делается тем же раскладчиком строк, что и колонки с обтеканием прямоугольников. The same line composer " +
        "lays out columns, flows round pictures and now fills any closed outline you give it.";

    [Bindable] private double _shapeFontSize = 13;

    [Bindable] private double _shapePadding = 6;

    [Bindable] private HorizontalTextAlignment _shapeAlignment = HorizontalTextAlignment.Center;

    [Bindable] private double _shapeScale = 1;

    [Bindable] private double _polygonCorners = 6;

    [Bindable] private Geometry _shapeCircle = CircleOf(1);

    [Bindable] private Geometry _shapeHeart = HeartOf(1);

    [Bindable] private Geometry _shapeStar = StarOf(1);

    [Bindable] private Geometry _shapePolygon = PolygonOf(6, 1);

    [Bindable] private Geometry _shapeRing = RingOf(1);

    partial void OnShapeScaleChanged(double value)
    {
        ShapeCircle = CircleOf(value);
        ShapeHeart = HeartOf(value);
        ShapeStar = StarOf(value);
        ShapePolygon = PolygonOf((int)Math.Round(PolygonCorners), value);
        ShapeRing = RingOf(value);
    }

    partial void OnPolygonCornersChanged(double value) => ShapePolygon = PolygonOf((int)Math.Round(value), ShapeScale);

    private const double Middle = 140;

    private static Geometry Parse(string data) => (Geometry)TypeCastFactory.CastFromString(data, typeof(Geometry));

    private static string N(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    private static string At(double x, double y, double scale) =>
        $"{N(Middle + (x - Middle) * scale)},{N(Middle + (y - Middle) * scale)}";

    private static string Circle(double radius) =>
        $"M {N(Middle)},{N(Middle - radius)} A {N(radius)},{N(radius)} 0 1 1 {N(Middle - 0.01)},{N(Middle - radius)} Z";

    private static Geometry CircleOf(double scale) => Parse(Circle(130 * scale));

    private static Geometry RingOf(double scale) => Parse(Circle(130 * scale) + " " + Circle(50 * scale));

    private static Geometry HeartOf(double scale) => Parse(
        $"M {At(140, 265, scale)} C {At(40, 185, scale)} {At(0, 125, scale)} {At(20, 75, scale)} " +
        $"C {At(40, 20, scale)} {At(110, 15, scale)} {At(140, 75, scale)} " +
        $"C {At(170, 15, scale)} {At(240, 20, scale)} {At(260, 75, scale)} " +
        $"C {At(280, 125, scale)} {At(240, 185, scale)} {At(140, 265, scale)} Z");

    private static Geometry StarOf(double scale) => PointsOf(10, scale, corner => corner % 2 == 0 ? 132 : 56);

    private static Geometry PolygonOf(int corners, double scale) => PointsOf(Math.Max(3, corners), scale, _ => 130);

    private static Geometry PointsOf(int count, double scale, Func<int, double> radius)
    {
        var data = new StringBuilder();
        for (var i = 0; i < count; i++)
        {
            var angle = -Math.PI / 2 + i * 2 * Math.PI / count;
            var r = radius(i);
            data.Append(i == 0 ? "M " : " L ")
                .Append(At(Middle + r * Math.Cos(angle), Middle + r * Math.Sin(angle), scale));
        }

        return Parse(data.Append(" Z").ToString());
    }
}
