using Adamantium.Mathematics;

namespace Adamantium.UI.Core.Media;

/// <summary>An arrow (shaft with optional heads) drawn by its own shader pass, no geometry. Ends and thickness are in the
/// drawing element's coordinates; a canvas passes screen values to keep them small.</summary>
public sealed class CanvasArrowBrush : Brush
{
    /// <summary>Where the arrow starts, in the drawing element's own coordinates - the end <see cref="StartHead"/> sits
    /// on.</summary>
    public static readonly AdamantiumProperty FromProperty = AdamantiumProperty.Register(nameof(From),
        typeof(Vector2), typeof(CanvasArrowBrush),
        new PropertyMetadata(Vector2.Zero, PropertyMetadataOptions.AffectsPaint));

    public static readonly AdamantiumProperty ToProperty = AdamantiumProperty.Register(nameof(To),
        typeof(Vector2), typeof(CanvasArrowBrush),
        new PropertyMetadata(Vector2.Zero, PropertyMetadataOptions.AffectsPaint));

    /// <summary>How thick the shaft is, in the same units as the ends.</summary>
    public static readonly AdamantiumProperty ThicknessProperty = AdamantiumProperty.Register(nameof(Thickness),
        typeof(double), typeof(CanvasArrowBrush), new PropertyMetadata(2.0, PropertyMetadataOptions.AffectsPaint));

    /// <summary>What sits on each end: 0 nothing, 1 barbs, 2 a filled triangle. A NUMBER and not the enum the canvas
    /// uses, because this is the parameter block and the graphics side has no business knowing a control's vocabulary.
    /// </summary>
    public static readonly AdamantiumProperty StartHeadProperty = AdamantiumProperty.Register(nameof(StartHead),
        typeof(int), typeof(CanvasArrowBrush), new PropertyMetadata(0, PropertyMetadataOptions.AffectsPaint));

    public static readonly AdamantiumProperty EndHeadProperty = AdamantiumProperty.Register(nameof(EndHead),
        typeof(int), typeof(CanvasArrowBrush), new PropertyMetadata(0, PropertyMetadataOptions.AffectsPaint));

    /// <summary>How far back from the tip a head reaches, in the same units as the ends.</summary>
    public static readonly AdamantiumProperty HeadLengthProperty = AdamantiumProperty.Register(nameof(HeadLength),
        typeof(double), typeof(CanvasArrowBrush), new PropertyMetadata(0.0, PropertyMetadataOptions.AffectsPaint));

    /// <summary>How far to each side of the shaft a head reaches - HALF its width, so it matches the thickness beside
    /// it.</summary>
    public static readonly AdamantiumProperty HeadWidthProperty = AdamantiumProperty.Register(nameof(HeadWidth),
        typeof(double), typeof(CanvasArrowBrush), new PropertyMetadata(0.0, PropertyMetadataOptions.AffectsPaint));

    public static readonly AdamantiumProperty ColorProperty = AdamantiumProperty.Register(nameof(Color),
        typeof(Color), typeof(CanvasArrowBrush), new PropertyMetadata(Colors.White, PropertyMetadataOptions.AffectsPaint));

    /// <summary>Bumped by whoever moves the arrow to say the numbers changed.
    /// <para>Needed for the same reason ink needs one: the ends are rewritten every time the camera moves, and a
    /// Vector2 written back with the same value leaves the paint with nothing to notice. One number said out loud costs
    /// less than making every frame's values differ.</para></summary>
    public static readonly AdamantiumProperty RevisionProperty = AdamantiumProperty.Register(nameof(Revision),
        typeof(int), typeof(CanvasArrowBrush), new PropertyMetadata(0, PropertyMetadataOptions.AffectsPaint));

    public Vector2 From
    {
        get => GetValue<Vector2>(FromProperty);
        set => SetValue(FromProperty, value);
    }

    public Vector2 To
    {
        get => GetValue<Vector2>(ToProperty);
        set => SetValue(ToProperty, value);
    }

    public double Thickness
    {
        get => GetValue<double>(ThicknessProperty);
        set => SetValue(ThicknessProperty, value);
    }

    public int StartHead
    {
        get => GetValue<int>(StartHeadProperty);
        set => SetValue(StartHeadProperty, value);
    }

    public int EndHead
    {
        get => GetValue<int>(EndHeadProperty);
        set => SetValue(EndHeadProperty, value);
    }

    public double HeadLength
    {
        get => GetValue<double>(HeadLengthProperty);
        set => SetValue(HeadLengthProperty, value);
    }

    public double HeadWidth
    {
        get => GetValue<double>(HeadWidthProperty);
        set => SetValue(HeadWidthProperty, value);
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

    protected override Brush CreateClone() => new CanvasArrowBrush
    {
        From = From,
        To = To,
        Thickness = Thickness,
        StartHead = StartHead,
        EndHead = EndHead,
        HeadLength = HeadLength,
        HeadWidth = HeadWidth,
        Color = Color,
        Revision = Revision,
        Opacity = Opacity
    };
}
