using Adamantium.Imaging;
using Adamantium.Mathematics;
using Adamantium.ProceduralGeometry;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Graphics;
using Adamantium.UI.Core.Media;
using Adamantium.UI.Core.Media.Imaging;
using Adamantium.UI.Core.Printing;
using Adamantium.Vulkan.Core;

namespace Adamantium.UITests.Printing;

/// <summary>One page of what the vector writers write: fills, pens of every kind, gradients, a picture, groups.</summary>
internal sealed class XpsProbeDocument : IPrintDocument
{
    private static readonly Color Red = new((byte)220, (byte)50, (byte)40, (byte)255);
    private static readonly Color Blue = new((byte)40, (byte)110, (byte)220, (byte)255);
    private static readonly Color Green = new((byte)60, (byte)170, (byte)80, (byte)255);

    public string Title => "XPS probe";

    public int Paginate(PrintPage page) => 1;

    public void DrawPage(int index, IDrawingSession session)
    {
        session.DrawRectangle(new SolidColorBrush(Blue), new Rect(40, 40, 200, 100), new CornerRadius(16),
            new Pen(new SolidColorBrush(Red), 6));
        session.DrawEllipse(new Rect(280, 40, 160, 100), new SolidColorBrush(Green), 0, 360, default,
            new Pen(new SolidColorBrush(Colors.Black), 2));

        // Pens: dashes XPS lays itself, a trim, concave and unlike ends laid as runs, a translucent dashed square.
        session.DrawLine(new Vector2<double>(40, 180), new Vector2<double>(440, 180),
            new Pen(new SolidColorBrush(Colors.Black), 4, dashStrokeArray: [12, 6],
                dashStartCap: PenLineCap.ConvexRound, dashEndCap: PenLineCap.ConvexRound));
        session.DrawLine(new Vector2<double>(40, 220), new Vector2<double>(440, 220),
            new Pen(new SolidColorBrush(Red), 10, trimStart: 0.2, trimEnd: 0.8));
        session.DrawLine(new Vector2<double>(40, 260), new Vector2<double>(440, 260),
            new Pen(new SolidColorBrush(Blue), 20, startLineCap: PenLineCap.ConcaveRound,
                endLineCap: PenLineCap.ConvexTriangle));
        session.DrawRectangle(null, new Rect(460, 160, 100, 100),
            new Pen(new SolidColorBrush(new Color((byte)0, (byte)0, (byte)0, (byte)128)), 8, dashStrokeArray: [20, 10],
                fitDashesToContour: true));

        // Gradients: linear repeated, radial with an inner circle, with alpha.
        session.DrawRectangle(new LinearGradientBrush([new GradientStop(Red, 0), new GradientStop(Blue, 1)])
        {
            StartPoint = new Vector2<double>(0, 0), EndPoint = new Vector2<double>(0.25, 0),
            SpreadMethod = GradientSpreadMethod.Reflect,
        }, new Rect(40, 300, 240, 80));
        session.DrawEllipse(new Rect(300, 300, 140, 140), new RadialGradientBrush(
            [new GradientStop(Colors.White, 0), new GradientStop(new Color((byte)40, (byte)110, (byte)220, (byte)64), 1)])
        {
            GradientOriginRadius = 0.3,
        }, 0, 360, default);

        // A picture, cut to rounded corners.
        session.DrawImage(Picture(8, 8), null, new Rect(460, 300, 120, 120), new CornerRadius(20));

        // A faded group cut to a circle.
        session.PushGroup(new DrawingGroupOptions
        {
            Opacity = 0.5,
            Clip = new EllipseGeometry(new Rect(40, 460, 200, 200)),
        });
        session.DrawRectangle(new SolidColorBrush(Red), new Rect(40, 460, 200, 100));
        session.DrawRectangle(new SolidColorBrush(Green), new Rect(40, 560, 200, 100));
        session.PopGroup();
    }

    /// <summary>A checkerboard of red and blue, <paramref name="width"/> by <paramref name="height"/> pixels.</summary>
    private static BitmapSource Picture(int width, int height)
    {
        var pixels = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var color = (x + y) % 2 == 0 ? Red : Blue;
                var at = (y * width + x) * 4;
                pixels[at] = color.R;
                pixels[at + 1] = color.G;
                pixels[at + 2] = color.B;
                pixels[at + 3] = 255;
            }
        }

        return new BitmapSource((uint)width, (uint)height, 1, 1, (SurfaceFormat)Format.R8G8B8A8_UNORM, pixels);
    }
}
