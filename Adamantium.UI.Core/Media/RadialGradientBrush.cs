using Adamantium.Mathematics;

namespace Adamantium.UI.Core.Media;

/// <summary>A gradient radiating outward from <see cref="GradientOrigin"/> (offset 0) to the ellipse boundary defined by
/// <see cref="Center"/> + <see cref="RadiusX"/>/<see cref="RadiusY"/> (offset 1). All values are RELATIVE to the filled
/// bounds (0..1), so the default (center 0.5,0.5 / radius 0.5) inscribes the bounds.</summary>
public sealed class RadialGradientBrush : GradientBrush
{
    public RadialGradientBrush() { }

    public RadialGradientBrush(GradientStopCollection stops) : base(stops) { }

    // PAINT, every one of them: a gradient's geometry is RELATIVE to the filled bounds, so moving the center or the radius
    // re-colors the same pixels - it never changes the element's shape or its layout (see Brush.Opacity).
    public static readonly AdamantiumProperty CenterProperty = AdamantiumProperty.Register(nameof(Center),
        typeof(Vector2<double>), typeof(RadialGradientBrush), new PropertyMetadata(new Vector2<double>(0.5f, 0.5f), PropertyMetadataOptions.AffectsPaint));

    public static readonly AdamantiumProperty GradientOriginProperty = AdamantiumProperty.Register(nameof(GradientOrigin),
        typeof(Vector2<double>), typeof(RadialGradientBrush), new PropertyMetadata(new Vector2<double>(0.5f, 0.5f), PropertyMetadataOptions.AffectsPaint));

    public static readonly AdamantiumProperty RadiusXProperty = AdamantiumProperty.Register(nameof(RadiusX),
        typeof(double), typeof(RadialGradientBrush), new PropertyMetadata(0.5, PropertyMetadataOptions.AffectsPaint));

    public static readonly AdamantiumProperty RadiusYProperty = AdamantiumProperty.Register(nameof(RadiusY),
        typeof(double), typeof(RadialGradientBrush), new PropertyMetadata(0.5, PropertyMetadataOptions.AffectsPaint));

    /// <summary>The ellipse center (relative). The stops lay out from <see cref="GradientOrigin"/> to this ellipse's edge.</summary>
    public Vector2<double> Center
    {
        get => GetValue<Vector2<double>>(CenterProperty);
        set { if (IsFrozen) return; SetValue(CenterProperty, value); }
    }

    /// <summary>Where offset 0 sits (relative). Defaults to the center; move it for an off-center "spotlight".</summary>
    public Vector2<double> GradientOrigin
    {
        get => GetValue<Vector2<double>>(GradientOriginProperty);
        set { if (IsFrozen) return; SetValue(GradientOriginProperty, value); }
    }

    public double RadiusX
    {
        get => GetValue<double>(RadiusXProperty);
        set { if (IsFrozen) return; SetValue(RadiusXProperty, value); }
    }

    public double RadiusY
    {
        get => GetValue<double>(RadiusYProperty);
        set { if (IsFrozen) return; SetValue(RadiusYProperty, value); }
    }

    public static readonly AdamantiumProperty GradientOriginRadiusProperty = AdamantiumProperty.Register(
        nameof(GradientOriginRadius), typeof(double), typeof(RadialGradientBrush),
        new PropertyMetadata(0.0, PropertyMetadataOptions.AffectsPaint));

    /// <summary>The radius of the circle offset 0 lies on, around <see cref="GradientOrigin"/>, as a fraction of
    /// <see cref="RadiusX"/> and <see cref="RadiusY"/>; 0, the default, makes the origin a point. Above 0 the gradient
    /// runs between two circles, centers and radii moving in step, a later circle over an earlier one - Canvas 2D's
    /// <c>createRadialGradient</c>, SVG 2's <c>fr</c>, a PDF radial shading. Where no circle passes nothing is painted.
    /// </summary>
    public double GradientOriginRadius
    {
        get => GetValue<double>(GradientOriginRadiusProperty);
        set { if (IsFrozen) return; SetValue(GradientOriginRadiusProperty, value); }
    }

    protected override Brush CreateClone() =>
        new RadialGradientBrush(CopyStops())
        {
            Center = Center,
            GradientOrigin = GradientOrigin,
            RadiusX = RadiusX,
            RadiusY = RadiusY,
            GradientOriginRadius = GradientOriginRadius,
            SpreadMethod = SpreadMethod,
            ColorInterpolationMode = ColorInterpolationMode,
            Opacity = Opacity
        };
}
