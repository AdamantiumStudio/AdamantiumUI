using Adamantium.UI.Core.RoutedEvents;
using Adamantium.Mathematics;
using Adamantium.UI.Core.Media.Imaging;

namespace Adamantium.UI.Core.Media;

/// <summary>A nine-slice frame (CSS <c>border-image</c>): <see cref="Slice"/> cuts the picture into fixed corners,
/// stretching or repeating edges, and a center; drawn as one batch.</summary>
public sealed class NineSliceBrush : Brush
{
    public NineSliceBrush() { }

    public NineSliceBrush(ImageSource source) => Source = source;

    // PAINT, all of them: a nine-slice fills the shape it is given, so changing any of this re-colours the same pixels.
    public static readonly AdamantiumProperty SourceProperty = AdamantiumProperty.Register(nameof(Source),
        typeof(ImageSource), typeof(NineSliceBrush), new PropertyMetadata(null, PropertyMetadataOptions.AffectsPaint, OnSourceChanged));

    private static void OnSourceChanged(AdamantiumComponent sender, AdamantiumPropertyChangedEventArgs e)
    {
        if (sender is NineSliceBrush brush)
        {
            TexturedBrushSource.RepaintWhenLoaded(e.NewValue as ImageSource, brush.RaiseChanged);
        }
    }

    /// <summary>Where the source is cut, as FRACTIONS of its size (0..1), not pixels: left, top, right, bottom. Fractions
    /// so one brush serves sources of several resolutions - the same skin at 1x and 2x cuts in the same place, and a
    /// pixel count would be wrong for one of them.</summary>
    public static readonly AdamantiumProperty SliceProperty = AdamantiumProperty.Register(nameof(Slice),
        typeof(Thickness), typeof(NineSliceBrush),
        new PropertyMetadata(new Thickness(0.25), PropertyMetadataOptions.AffectsPaint));

    /// <summary>How wide the corners are DRAWN, in logical px. Unset (0) draws them at the size the slice fractions give
    /// against the source's own pixel size - the 1:1 case. Setting it scales the frame without touching the source, which
    /// is what a skin needs when the same picture dresses a 24px button and a 96px panel.</summary>
    public static readonly AdamantiumProperty BorderProperty = AdamantiumProperty.Register(nameof(Border),
        typeof(Thickness), typeof(NineSliceBrush),
        new PropertyMetadata(new Thickness(0), PropertyMetadataOptions.AffectsPaint));

    public static readonly AdamantiumProperty EdgeModeProperty = AdamantiumProperty.Register(nameof(EdgeMode),
        typeof(NineSliceEdgeMode), typeof(NineSliceBrush),
        new PropertyMetadata(NineSliceEdgeMode.Stretch, PropertyMetadataOptions.AffectsPaint));

    /// <summary>Whether the center tiles instead of stretching; off by default, like CSS <c>border-image</c>, and separate
    /// from <see cref="EdgeMode"/>.</summary>
    public static readonly AdamantiumProperty TileCenterProperty = AdamantiumProperty.Register(nameof(TileCenter),
        typeof(bool), typeof(NineSliceBrush), new PropertyMetadata(false, PropertyMetadataOptions.AffectsPaint));

    /// <summary>Whether the middle piece is drawn at all. False leaves the inside untouched - the frame is then a border
    /// over whatever the element already paints there, which is the usual want for a skin that dresses live content.</summary>
    public static readonly AdamantiumProperty DrawCenterProperty = AdamantiumProperty.Register(nameof(DrawCenter),
        typeof(bool), typeof(NineSliceBrush), new PropertyMetadata(true, PropertyMetadataOptions.AffectsPaint));

    /// <summary>Multiplied into every sampled pixel; white draws the source as it is.</summary>
    public static readonly AdamantiumProperty TintProperty = AdamantiumProperty.Register(nameof(Tint),
        typeof(Color), typeof(NineSliceBrush), new PropertyMetadata(Colors.White, PropertyMetadataOptions.AffectsPaint));

    public ImageSource Source
    {
        get => GetValue<ImageSource>(SourceProperty);
        set
        {
            if (IsFrozen) return;
            SetValue(SourceProperty, value);
        }
    }

    public Thickness Slice
    {
        get => GetValue<Thickness>(SliceProperty);
        set
        {
            if (IsFrozen) return;
            SetValue(SliceProperty, value);
        }
    }

    public Thickness Border
    {
        get => GetValue<Thickness>(BorderProperty);
        set
        {
            if (IsFrozen) return;
            SetValue(BorderProperty, value);
        }
    }

    public NineSliceEdgeMode EdgeMode
    {
        get => GetValue<NineSliceEdgeMode>(EdgeModeProperty);
        set
        {
            if (IsFrozen) return;
            SetValue(EdgeModeProperty, value);
        }
    }

    public bool TileCenter
    {
        get => GetValue<bool>(TileCenterProperty);
        set
        {
            if (IsFrozen) return;
            SetValue(TileCenterProperty, value);
        }
    }

    public bool DrawCenter
    {
        get => GetValue<bool>(DrawCenterProperty);
        set
        {
            if (IsFrozen) return;
            SetValue(DrawCenterProperty, value);
        }
    }

    public Color Tint
    {
        get => GetValue<Color>(TintProperty);
        set
        {
            if (IsFrozen) return;
            SetValue(TintProperty, value);
        }
    }

    protected override Brush CreateClone() =>
        new NineSliceBrush
        {
            Source = Source,
            Slice = Slice,
            Border = Border,
            EdgeMode = EdgeMode,
            DrawCenter = DrawCenter,
            TileCenter = TileCenter,
            Tint = Tint,
            Opacity = Opacity
        };
}
