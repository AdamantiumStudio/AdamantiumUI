using System.Collections.Generic;
using Adamantium.Mathematics;
using Adamantium.UI.Core.Media;

namespace Adamantium.UI.Core.Graphics.Vector;

/// <summary>A gradient's color at an offset, each channel 0..1, alpha straight.</summary>
internal readonly record struct RampStop(double Offset, double Red, double Green, double Blue, double Alpha);

/// <summary>A gradient's stops as the renderer draws them, for a vector writer to lay out: sorted, as many as it draws,
/// tinted, and - where its colors run in OKLab - cut finer, so a writer mixing in sRGB runs straight between colors
/// mixed in OKLab.</summary>
internal static class GradientRamp
{
    /// <summary>How many stops each span between two stops is cut into when the colors run in OKLab.</summary>
    public const int OklabSteps = 16;

    /// <summary>How many repeats of a repeated or reflected gradient are written at most.</summary>
    public const int MaxRepeats = 64;

    private static readonly Vector2<double>[] Corners = [new(0, 0), new(1, 0), new(0, 1), new(1, 1)];

    /// <summary>The repeats of a linear gradient from <paramref name="start"/> to <paramref name="end"/> - fractions of
    /// the box it is laid over - that reach every corner of the box: written out, a writer draws it padded.</summary>
    public static (int First, int Last) LinearRepeats(Vector2<double> start, Vector2<double> end)
    {
        var axis = end - start;
        var squared = axis.X * axis.X + axis.Y * axis.Y;
        var low = double.PositiveInfinity;
        var high = double.NegativeInfinity;
        foreach (var corner in Corners)
        {
            var along = ((corner.X - start.X) * axis.X + (corner.Y - start.Y) * axis.Y) / squared;
            low = Math.Min(low, along);
            high = Math.Max(high, along);
        }

        var first = (int)Math.Floor(Math.Max(low, -MaxRepeats));
        var last = (int)Math.Ceiling(Math.Min(high, MaxRepeats));
        if (last <= first)
        {
            last = first + 1;
        }

        if (last - first > MaxRepeats)
        {
            var middle = (first + last) / 2;
            first = middle - MaxRepeats / 2;
            last = first + MaxRepeats;
        }

        return (first, last);
    }

    /// <summary>How many repeats out a radial gradient's circle reaches every corner of its box, in a space where the
    /// ellipse is a circle - heights times <paramref name="squash"/>.</summary>
    public static int RadialRepeats(Vector2<double> origin, Vector2<double> center, double startRadius,
        double endRadius, double squash)
    {
        var last = 1;
        while (last < MaxRepeats && !Covers(origin, center, startRadius, endRadius, last, squash))
        {
            last++;
        }

        return last;
    }

    /// <summary>Whether the circle <paramref name="times"/> repeats out reaches every corner of the bounds.</summary>
    private static bool Covers(Vector2<double> origin, Vector2<double> center, double startRadius, double endRadius,
        int times, double squash)
    {
        var at = origin + (center - origin) * times;
        var radius = startRadius + (endRadius - startRadius) * times;
        foreach (var corner in Corners)
        {
            var offset = new Vector2<double>(corner.X, corner.Y * squash) - at;
            if (Math.Sqrt(offset.X * offset.X + offset.Y * offset.Y) > radius)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>The stops the renderer draws - the first <see cref="GradientBrush.MaxRenderedStops"/> by offset - their
    /// colors times <paramref name="tint"/>, cut finer where they run in OKLab.</summary>
    public static List<RampStop> Of(GradientBrush brush, Color tint)
    {
        ArgumentNullException.ThrowIfNull(brush);
        var sorted = new List<GradientStop>(brush.GradientStops);
        sorted.Sort((one, other) => one.Offset.CompareTo(other.Offset));
        if (sorted.Count > GradientBrush.MaxRenderedStops)
        {
            sorted.RemoveRange(GradientBrush.MaxRenderedStops, sorted.Count - GradientBrush.MaxRenderedStops);
        }

        var stops = new List<RampStop>(sorted.Count);
        foreach (var stop in sorted)
        {
            stops.Add(new RampStop(Math.Clamp(stop.Offset, 0, 1), stop.Color.R / 255.0 * tint.R / 255.0,
                stop.Color.G / 255.0 * tint.G / 255.0, stop.Color.B / 255.0 * tint.B / 255.0, stop.Color.A / 255.0));
        }

        if (brush.ColorInterpolationMode != ColorInterpolationMode.Oklab || stops.Count < 2)
        {
            return stops;
        }

        var fine = new List<RampStop>(stops.Count * OklabSteps);
        for (var index = 0; index + 1 < stops.Count; index++)
        {
            for (var step = 0; step < OklabSteps; step++)
            {
                fine.Add(Oklab.Between(stops[index], stops[index + 1], step / (double)OklabSteps));
            }
        }

        fine.Add(stops[^1]);
        return fine;
    }

    /// <summary>The stops laid out over the repeats from <paramref name="first"/> to <paramref name="last"/>, each
    /// reflected repeat backwards, as offsets 0..1 of the whole. Each repeat holds its end colors to its own ends, so
    /// one meets the next at a hard edge, as on screen.</summary>
    public static List<RampStop> Repeated(List<RampStop> stops, GradientSpreadMethod spread, int first, int last)
    {
        if (spread == GradientSpreadMethod.Pad || last - first <= 1 && first == 0)
        {
            return stops;
        }

        if (stops[0].Offset > 0 || stops[^1].Offset < 1)
        {
            var whole = new List<RampStop>(stops.Count + 2);
            if (stops[0].Offset > 0)
            {
                whole.Add(stops[0] with { Offset = 0 });
            }

            whole.AddRange(stops);
            if (stops[^1].Offset < 1)
            {
                whole.Add(stops[^1] with { Offset = 1 });
            }

            stops = whole;
        }

        var span = (double)(last - first);
        var all = new List<RampStop>(stops.Count * (last - first));
        for (var repeat = first; repeat < last; repeat++)
        {
            var backwards = spread == GradientSpreadMethod.Reflect && (repeat & 1) != 0;
            for (var index = 0; index < stops.Count; index++)
            {
                var stop = stops[backwards ? stops.Count - 1 - index : index];
                var offset = backwards ? 1 - stop.Offset : stop.Offset;
                all.Add(stop with { Offset = (repeat - first + offset) / span });
            }
        }

        return all;
    }

    public static bool IsOpaque(List<RampStop> stops)
    {
        foreach (var stop in stops)
        {
            if (stop.Alpha < 1)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Colors mixed in OKLab (Björn Ottosson, 2020), as the renderer mixes them when asked: sRGB to linear to
    /// LMS to OKLab, mixed, and back.</summary>
    private static class Oklab
    {
        public static RampStop Between(RampStop from, RampStop to, double share)
        {
            var one = ToLab(from);
            var two = ToLab(to);
            var mixed = new Vector3<double>(
                one.X + (two.X - one.X) * share,
                one.Y + (two.Y - one.Y) * share,
                one.Z + (two.Z - one.Z) * share);
            var color = ToRgb(mixed);
            return new RampStop(from.Offset + (to.Offset - from.Offset) * share, color.X, color.Y, color.Z,
                from.Alpha + (to.Alpha - from.Alpha) * share);
        }

        private static Vector3<double> ToLab(RampStop stop)
        {
            var red = Linear(stop.Red);
            var green = Linear(stop.Green);
            var blue = Linear(stop.Blue);
            var longCone = Math.Cbrt(0.4122214708 * red + 0.5363325363 * green + 0.0514459929 * blue);
            var middleCone = Math.Cbrt(0.2119034982 * red + 0.6806995451 * green + 0.1073969566 * blue);
            var shortCone = Math.Cbrt(0.0883024619 * red + 0.2817188376 * green + 0.6299787005 * blue);
            return new Vector3<double>(
                0.2104542553 * longCone + 0.7936177850 * middleCone - 0.0040720468 * shortCone,
                1.9779984951 * longCone - 2.4285922050 * middleCone + 0.4505937099 * shortCone,
                0.0259040371 * longCone + 0.7827717662 * middleCone - 0.8086757660 * shortCone);
        }

        private static Vector3<double> ToRgb(Vector3<double> lab)
        {
            var longCone = Math.Pow(lab.X + 0.3963377774 * lab.Y + 0.2158037573 * lab.Z, 3);
            var middleCone = Math.Pow(lab.X - 0.1055613458 * lab.Y - 0.0638541728 * lab.Z, 3);
            var shortCone = Math.Pow(lab.X - 0.0894841775 * lab.Y - 1.2914855480 * lab.Z, 3);
            return new Vector3<double>(
                Encoded(4.0767416621 * longCone - 3.3077115913 * middleCone + 0.2309699292 * shortCone),
                Encoded(-1.2684380046 * longCone + 2.6097574011 * middleCone - 0.3413193965 * shortCone),
                Encoded(-0.0041960863 * longCone - 0.7034186147 * middleCone + 1.7076147010 * shortCone));
        }

        private static double Linear(double value) =>
            value <= 0.04045 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);

        private static double Encoded(double value)
        {
            value = Math.Clamp(value, 0, 1);
            return value <= 0.0031308 ? value * 12.92 : 1.055 * Math.Pow(value, 1 / 2.4) - 0.055;
        }
    }
}
