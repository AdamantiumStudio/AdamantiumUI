using System;
using Adamantium.Mathematics;
using Adamantium.ProceduralGeometry;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Graphics;
using Adamantium.UI.Core.Media;
using Adamantium.UI.Core.Printing;

namespace Adamantium.UI.Sandbox.Printing;

/// <summary>A document of a few pages to print: on each, a header band, its number in big digits, a bar chart, a
/// gradient and a row of circles - laid out anew on whatever sheet it is given.</summary>
internal sealed class SamplePrintDocument(int pages) : IPrintDocument
{
    private static readonly Color Ink = new((byte)40, (byte)44, (byte)52, (byte)255);
    private static readonly Color Blue = new((byte)38, (byte)139, (byte)210, (byte)255);
    private static readonly Color Orange = new((byte)203, (byte)75, (byte)22, (byte)255);
    private static readonly Color Green = new((byte)133, (byte)153, (byte)0, (byte)255);

    private Rect _area = new PrintPage(PaperSize.A4) { Margins = new Thickness(72) }.ContentArea;

    public string Title => "Sample document";

    public int Paginate(PrintPage page)
    {
        _area = page.ContentArea;
        return pages;
    }

    public void DrawPage(int index, IDrawingSession session)
    {
        var left = _area.X;
        var width = _area.Width;
        session.DrawRectangle(new SolidColorBrush(Blue), new Rect(left, _area.Y, width, 56), new CornerRadius(8));
        Number(session, index + 1, new Rect(left + 16, _area.Y + 10, 36, 36), new SolidColorBrush(Colors.White));

        var top = _area.Y + 96;
        var chart = new Rect(left, top, width, Math.Min(260, _area.Height * 0.3));
        session.DrawRectangle(null, chart, new CornerRadius(0), new Pen(new SolidColorBrush(Ink), 1));
        var bars = 8;
        var gap = chart.Width / (bars * 3 + 1);
        for (var bar = 0; bar < bars; bar++)
        {
            var share = 0.25 + 0.7 * Math.Abs(Math.Sin((bar + 1) * (index + 2) * 0.7));
            var height = (chart.Height - 16) * share;
            session.DrawRectangle(new SolidColorBrush(bar % 3 == 0 ? Orange : Blue),
                new Rect(chart.X + gap + bar * gap * 3, chart.Bottom - 8 - height, gap * 2, height));
        }

        var band = new Rect(left, chart.Bottom + 32, width, 60);
        session.DrawRectangle(new LinearGradientBrush([new GradientStop(Blue, 0), new GradientStop(Green, 1)])
        {
            StartPoint = new Vector2<double>(0, 0),
            EndPoint = new Vector2<double>(1, 0),
        }, band, new CornerRadius(30));

        var circles = Math.Min(index + 3, 9);
        var diameter = Math.Min(48, width / (circles * 1.5));
        for (var circle = 0; circle < circles; circle++)
        {
            session.DrawEllipse(new Rect(left + circle * diameter * 1.5, band.Bottom + 32, diameter, diameter),
                new SolidColorBrush(circle % 2 == 0 ? Green : Orange), null, null);
        }

        // Lines standing in for text, down to the bottom margin.
        var line = band.Bottom + 32 + diameter + 32;
        var row = 0;
        while (line + 8 < _area.Bottom)
        {
            var length = width * (row % 5 == 4 ? 0.55 : 0.92 - row % 3 * 0.06);
            session.DrawRectangle(new SolidColorBrush(new Color((byte)200, (byte)204, (byte)210, (byte)255)),
                new Rect(left, line, length, 6), new CornerRadius(3));
            line += 18;
            row++;
        }
    }

    /// <summary>A number in seven-segment digits - no font needed.</summary>
    private static void Number(IDrawingSession session, int number, Rect box, Brush brush)
    {
        var text = number.ToString();
        var digitWidth = box.Height * 0.55;
        for (var at = 0; at < text.Length; at++)
        {
            Digit(session, text[at] - '0', new Rect(box.X + at * (digitWidth + 8), box.Y, digitWidth, box.Height), brush);
        }
    }

    // Segments a to g, clockwise from the top, then the middle.
    private static readonly int[] Segments = [0b1111110, 0b0110000, 0b1101101, 0b1111001, 0b0110011, 0b1011011,
        0b1011111, 0b1110000, 0b1111111, 0b1111011];

    private static void Digit(IDrawingSession session, int digit, Rect box, Brush brush)
    {
        var bits = Segments[Math.Clamp(digit, 0, 9)];
        var thick = box.Width * 0.2;
        var half = box.Height / 2;
        Rect[] pieces =
        [
            new(box.X, box.Y, box.Width, thick),
            new(box.Right - thick, box.Y, thick, half),
            new(box.Right - thick, box.Y + half, thick, half),
            new(box.X, box.Bottom - thick, box.Width, thick),
            new(box.X, box.Y + half, thick, half),
            new(box.X, box.Y, thick, half),
            new(box.X, box.Y + half - thick / 2, box.Width, thick),
        ];
        for (var segment = 0; segment < 7; segment++)
        {
            if ((bits & (1 << (6 - segment))) != 0)
            {
                session.DrawRectangle(brush, pieces[segment]);
            }
        }
    }
}
