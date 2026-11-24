using System.Globalization;
using Adamantium.UI.Core;

namespace Adamantium.UI.WebView.Accessibility;

/// <summary>The flat part of the 4x4 matrix Chromium draws a node with - turn, scale and move in the page's plane.</summary>
internal sealed class WebViewAccessibilityTransform
{
    private readonly double xx, xy, moveX, yx, yy, moveY;

    private WebViewAccessibilityTransform(double xx, double xy, double moveX, double yx, double yy, double moveY)
    {
        this.xx = xx;
        this.xy = xy;
        this.moveX = moveX;
        this.yx = yx;
        this.yy = yy;
        this.moveY = moveY;
    }

    /// <summary>Reads Chromium's text of the matrix, row by row - <c>[ a b 0 x\n c d 0 y\n 0 0 1 0\n 0 0 0 1 ]</c>;
    /// null for one it cannot read.</summary>
    public static WebViewAccessibilityTransform Parse(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        var numbers = new List<double>(16);
        foreach (var part in text.Split([' ', '\n', '\t', '[', ']'], StringSplitOptions.RemoveEmptyEntries))
        {
            if (double.TryParse(part, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
            {
                numbers.Add(number);
            }
        }

        return numbers.Count < 16
            ? null
            : new WebViewAccessibilityTransform(numbers[0], numbers[1], numbers[3], numbers[4], numbers[5], numbers[7]);
    }

    /// <summary>Where <paramref name="rect"/> goes: the box around its four corners moved.</summary>
    public Rect Apply(Rect rect)
    {
        Span<double> xs = [rect.X, rect.Right, rect.X, rect.Right];
        Span<double> ys = [rect.Y, rect.Y, rect.Bottom, rect.Bottom];
        double left = double.MaxValue, top = double.MaxValue, right = double.MinValue, bottom = double.MinValue;
        for (var i = 0; i < 4; i++)
        {
            var x = xx * xs[i] + xy * ys[i] + moveX;
            var y = yx * xs[i] + yy * ys[i] + moveY;
            left = Math.Min(left, x);
            top = Math.Min(top, y);
            right = Math.Max(right, x);
            bottom = Math.Max(bottom, y);
        }

        return new Rect(left, top, right - left, bottom - top);
    }
}
