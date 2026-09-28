using Adamantium.Mathematics;

namespace Adamantium.UI.Core.Media;

/// <summary>One stroke's ink, drawn as capsules by its own pass. <see cref="Points"/> is a borrowed array in the drawing
/// element's coordinates; changes are signaled through <see cref="Count"/> and <see cref="Revision"/>.</summary>
public sealed class InkBrush : Brush
{
    /// <summary>The points, in the drawing element's own coordinates. Only the first <see cref="Count"/> are used.
    /// </summary>
    public static readonly AdamantiumProperty PointsProperty = AdamantiumProperty.Register(nameof(Points),
        typeof(Vector2F[]), typeof(InkBrush), new PropertyMetadata(null, PropertyMetadataOptions.AffectsPaint));

    /// <summary>How many of <see cref="Points"/> are the stroke. A stroke being drawn grows this and nothing else.</summary>
    public static readonly AdamantiumProperty CountProperty = AdamantiumProperty.Register(nameof(Count),
        typeof(int), typeof(InkBrush), new PropertyMetadata(0, PropertyMetadataOptions.AffectsPaint));

    /// <summary>How wide the ink is, in the same units as <see cref="Points"/>.</summary>
    public static readonly AdamantiumProperty ThicknessProperty = AdamantiumProperty.Register(nameof(Thickness),
        typeof(double), typeof(InkBrush), new PropertyMetadata(2.0, PropertyMetadataOptions.AffectsPaint));

    public static readonly AdamantiumProperty ColorProperty = AdamantiumProperty.Register(nameof(Color),
        typeof(Color), typeof(InkBrush), new PropertyMetadata(Colors.White, PropertyMetadataOptions.AffectsPaint));

    /// <summary>Bumped after rewriting <see cref="Points"/> in place, since the unchanged reference is invisible to the
    /// property system.</summary>
    public static readonly AdamantiumProperty RevisionProperty = AdamantiumProperty.Register(nameof(Revision),
        typeof(int), typeof(InkBrush), new PropertyMetadata(0, PropertyMetadataOptions.AffectsPaint));

    public Vector2F[] Points
    {
        get => GetValue<Vector2F[]>(PointsProperty);
        set => SetValue(PointsProperty, value);
    }

    public int Count
    {
        get => GetValue<int>(CountProperty);
        set => SetValue(CountProperty, value);
    }

    public double Thickness
    {
        get => GetValue<double>(ThicknessProperty);
        set => SetValue(ThicknessProperty, value);
    }

    public Color Color
    {
        get => GetValue<Color>(ColorProperty);
        set => SetValue(ColorProperty, value);
    }

    public int Revision
    {
        get => GetValue<int>(RevisionProperty);
        set => SetValue(RevisionProperty, value);
    }

    protected override Brush CreateClone() => new InkBrush
    {
        Points = Points,
        Count = Count,
        Revision = Revision,
        Thickness = Thickness,
        Color = Color,
        Opacity = Opacity
    };
}
