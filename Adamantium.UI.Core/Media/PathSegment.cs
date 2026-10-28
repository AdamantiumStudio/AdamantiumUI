using System.Runtime.InteropServices;
using Adamantium.Mathematics;

namespace Adamantium.UI.Core.Media;

public abstract class PathSegment : AdamantiumComponent
{
    public static readonly AdamantiumProperty IsStrokedProperty =
        AdamantiumProperty.Register(nameof(IsStroked), typeof(bool), typeof(PathSegment),
            new PropertyMetadata(true, PropertyMetadataOptions.AffectsRender));

    public bool IsStroked
    {
        get => GetValue<bool>(IsStrokedProperty);
        set => SetValue(IsStrokedProperty, value);
    }
    
    internal abstract Vector2<double>[] ProcessSegment(Vector2<double> currentPoint);

    /// <summary>A curve sampled densely along its parameter, resampled evenly along its length - a point about every 3
    /// pixels, 8 to 256 of them - so the stroke neither bunches where the curve is slow nor goes angular where it is fast;
    /// its two ends for a curve of no length, nothing for none.</summary>
    internal static Vector2<double>[] ResampledEvenly(List<Vector2<double>> dense)
    {
        if (dense.Count == 0)
        {
            return [];
        }

        var points = CollectionsMarshal.AsSpan(dense);
        var length = MathHelper.Polyline.Length<double>(points);
        if (!(length > 0))
        {
            return [points[0], points[^1]];
        }

        var resampled = new Vector2<double>[Math.Clamp((int)(length / 3.0), 8, 256)];
        MathHelper.Polyline.Resample<double>(points, resampled);
        return resampled;
    }
}