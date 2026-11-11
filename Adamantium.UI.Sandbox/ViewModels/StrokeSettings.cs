using System.Globalization;
using Adamantium.Core;
using Adamantium.MVVM;
using Adamantium.Mathematics;
using Adamantium.ProceduralGeometry;
using Adamantium.UI.Core.Media;
using Adamantium.UI.Core.Media.Imaging;
using Adamantium.UI.Sandbox.Content;

namespace Adamantium.UI.Sandbox.ViewModels;

/// <summary>Shared stroke ("кайма") parameters for the Shapes-tab pen playground. ONE instance drives the stroke of
/// EVERY shape in the tab: thickness, dashes, trim, corner, cap/join (bound to <c>DropDown</c>s) and color (bound to a
/// <c>ColorPicker</c>). Sliders/dropdowns/picker mutate this one object; each shape binds its stroke off <c>Stroke.*</c>.</summary>
public sealed class StrokeSettings : AdamantiumViewModel
{
    private double _strokeWidth = 8;
    public double StrokeWidth { get => _strokeWidth; set => SetProperty(ref _strokeWidth, value); }

    // Alpha of the translucent-overlap stand. Worth dragging: at 1 the strokes take the plain single-pass path, and
    // anywhere below it they take the union path, so the two can be compared on the same shapes.
    private double _overlapAlpha = 0.5;
    public double OverlapAlpha { get => _overlapAlpha; set => SetProperty(ref _overlapAlpha, value); }

    // Curve tessellation density: number of points each Bézier/NURBS/B-spline is sampled into (evenly by arc length).
    // 0 = automatic (~3px spacing). The curves bind their Samples to this so the panel drives them all.
    private double _samples = 32;   // demo slider START only; the real default lives on CurveBase.Samples (32)
    public double Samples { get => _samples; set => SetProperty(ref _samples, value); }

    // --- NURBS playground (the demo NURBS has 6 control points) --------------------------------------------------------
    // Weight of the two TOP control points (the peak). >1 pulls the NURBS toward them; =1 = a plain B-spline. This is the
    // "R" (rational) that a B-spline lacks - drag it and the curve bulges toward the peak.
    private double _nurbsWeight = 1;
    public double NurbsWeight
    {
        get => _nurbsWeight;
        set { if (SetProperty(ref _nurbsWeight, value)) RaisePropertyChanged(nameof(NurbsWeights)); }
    }
    public System.Collections.Generic.IReadOnlyList<double> NurbsWeights => [1, 1, _nurbsWeight, _nurbsWeight, 1, 1];

    // Piecewise-polynomial degree of the NURBS (a B-spline concept too): higher = smoother, pulls further from the
    // control polygon. The plain B-spline shape stays fixed for comparison.
    private double _nurbsDegree = 3;
    public double NurbsDegree { get => _nurbsDegree; set => SetProperty(ref _nurbsDegree, value); }

    private bool _nurbsUniform;   // false = non-uniform/clamped knots (smooth, reaches endpoints); true = uniform (floats)
    public bool NurbsUniform { get => _nurbsUniform; set => SetProperty(ref _nurbsUniform, value); }

    private double _dashOffset;
    public double DashOffset { get => _dashOffset; set => SetProperty(ref _dashOffset, value); }

    private double _trimStart;
    public double TrimStart { get => _trimStart; set => SetProperty(ref _trimStart, value); }

    private double _trimEnd = 1.0;
    public double TrimEnd { get => _trimEnd; set => SetProperty(ref _trimEnd, value); }

    // Uniform corner radius for the rectangle. The slider binds the scalar Corner; the shape's CornerRadius property
    // (a CornerRadius struct, not a double) reads the derived value.
    private double _corner = 4;
    public double Corner { get => _corner; set { if (SetProperty(ref _corner, value)) RaisePropertyChanged(nameof(CornerRadius)); } }
    public CornerRadius CornerRadius => new(_corner);

    // Four independent caps: Start/End only at the stroke's real ends (none on a closed untrimmed contour), dash caps
    // everywhere else.
    private PenLineCap _dashStartCap = PenLineCap.ConvexRound;
    public PenLineCap DashStartCap { get => _dashStartCap; set => SetProperty(ref _dashStartCap, value); }

    private PenLineCap _dashEndCap = PenLineCap.ConvexRound;
    public PenLineCap DashEndCap { get => _dashEndCap; set => SetProperty(ref _dashEndCap, value); }

    private PenLineCap _startCap = PenLineCap.Flat;
    public PenLineCap StartCap { get => _startCap; set => SetProperty(ref _startCap, value); }

    private PenLineCap _endCap = PenLineCap.Flat;
    public PenLineCap EndCap { get => _endCap; set => SetProperty(ref _endCap, value); }

    // Stretch the pattern so a CLOSED contour holds a whole number of periods. Off, the leftover lands where the
    // contour closes - one odd dash at the seam, which for a rounded rect is the bottom-right corner. It is the seam,
    // not the corner: there is nothing to fix in the corner code, and this is the switch that shows it.
    private bool _dashFit;
    public bool DashFit { get => _dashFit; set => SetProperty(ref _dashFit, value); }

    // Corner join. Bound two-way to a DropDown (EnumType=PenLineJoin): Miter / Bevel / Round.
    private PenLineJoin _join = PenLineJoin.Round;
    public PenLineJoin Join { get => _join; set => SetProperty(ref _join, value); }

    // How far a miter's point may reach, in half thicknesses: below the star's 3.2 its tips are beveled.
    private double _miterLimit = 10;
    public double MiterLimit { get => _miterLimit; set => SetProperty(ref _miterLimit, value); }

    // Stroke color, driven by a ColorPicker. StrokeBrush is ONE cached brush every shape binds its Stroke to - a color
    // change mutates its Color in place (AffectsPaint re-bakes the users), NOT a new brush per read. Creating a fresh
    // SolidColorBrush (an AdamantiumComponent) on every read churned the property system and deadlocked the render thread
    // (which reads brush colors under a per-component Monitor lock) against the pump thread on a color change.
    // The gradients run from the picked color to a fixed blue, their first stop following the picker likewise.
    private static readonly Color GradientEnd = new(59, 130, 246);
    private readonly SolidColorBrush _strokeBrush = new(new Color(255, 136, 0));   // orange, matching the old default hue 32
    private readonly GradientStop _linearFrom = new(new Color(255, 136, 0), 0.0);
    private readonly GradientStop _radialFrom = new(new Color(255, 136, 0), 0.0);
    private readonly LinearGradientBrush _linearBrush;
    private readonly RadialGradientBrush _radialBrush;

    public StrokeSettings()
    {
        _linearBrush = new LinearGradientBrush(new GradientStopCollection([_linearFrom, new GradientStop(GradientEnd, 1.0)]))
        {
            StartPoint = new Vector2<double>(0, 0),
            EndPoint = new Vector2<double>(1, 0),
        };
        // Off the middle and wide: centered with the default half-size radius, a closed shape's stroke runs along the
        // radius itself and shows nothing but the last stop.
        _radialBrush = new RadialGradientBrush(new GradientStopCollection([_radialFrom, new GradientStop(GradientEnd, 1.0)]))
        {
            Center = new Vector2<double>(0.25, 0.25),
            GradientOrigin = new Vector2<double>(0.25, 0.25),
            RadiusX = 1.0,
            RadiusY = 1.0,
        };
    }

    private Color _selectedColor = new(255, 136, 0);
    public Color SelectedColor
    {
        get => _selectedColor;
        set
        {
            if (!SetProperty(ref _selectedColor, value)) return;
            _strokeBrush.Color = value;
            _linearFrom.Color = value;
            _radialFrom.Color = value;
        }
    }

    // The procedural and picture brushes a stroke can wear, one of each kind.
    private readonly PatternBrush _patternBrush = new()
    {
        Pattern = PatternType.Stripes,
        CellSize = 6,
        Color1 = new Color(255, 136, 0),
        Color2 = new Color(226, 232, 240),   // light, so it stands apart from the shapes' dark fill
    };

    public PatternType[] PatternKinds { get; } = System.Enum.GetValues<PatternType>();

    public PatternType PatternKind
    {
        get => _patternBrush.Pattern;
        set
        {
            if (_patternBrush.Pattern == value) return;
            _patternBrush.Pattern = value;
            RaisePropertyChanged();
        }
    }

    public NoiseType[] NoiseKinds { get; } = System.Enum.GetValues<NoiseType>();

    public NoiseType NoiseKind
    {
        get => _noiseBrush.NoiseType;
        set
        {
            if (_noiseBrush.NoiseType == value) return;
            _noiseBrush.NoiseType = value;
            RaisePropertyChanged();
        }
    }

    // One pass, two, or the brush's choice - bound to a DropDown (EnumType=PenOverlap).
    private PenOverlap _overlap;
    public PenOverlap Overlap { get => _overlap; set => SetProperty(ref _overlap, value); }

    private readonly NoiseBrush _noiseBrush = new()
    {
        Scale = 24,
        Octaves = 4,
        Color1 = new Color(11, 18, 32),
        Color2 = new Color(125, 211, 252),
    };

    private readonly ImageBrush _imageBrush = new()
    {
        Source = BitmapImageCache.GetOrCreate(
            System.IO.Path.Combine(System.AppContext.BaseDirectory, SandboxContentAssets.Textures.Tile_sample)),
    };

    // A picture from a layer of a texture array, as map tiles are: a second layer, so a stroke showing the first would
    // be caught - blue to violet across, a lighter band along.
    private readonly ImageBrush _layerBrush = new() { Source = AtlasLayer() };

    private static ImageLayer AtlasLayer()
    {
        const int side = 32;
        var atlas = new TextureArrayImage(side, side, 2, Adamantium.Imaging.SurfaceFormat.B8G8R8A8.UNorm);
        var first = atlas.TakeLayer();
        var second = atlas.TakeLayer();
        var pixels = new byte[side * side * 4];
        System.Array.Fill(pixels, (byte)255);
        atlas.Write(first, pixels);
        for (var y = 0; y < side; y++)
        {
            for (var x = 0; x < side; x++)
            {
                var i = (y * side + x) * 4;
                var band = y is > 12 and < 20 ? 80 : 0;
                pixels[i] = 255;                                                    // B
                pixels[i + 1] = (byte)System.Math.Min(255, 60 + band);             // G
                pixels[i + 2] = (byte)System.Math.Min(255, x * 255 / side + band); // R
                pixels[i + 3] = 255;
            }
        }

        atlas.Write(second, pixels);
        return second;
    }

    // Solid, a gradient, a pattern, noise or a picture - bound to a DropDown (EnumType=StrokeBrushKind).
    private StrokeBrushKind _brushKind;
    public StrokeBrushKind BrushKind
    {
        get => _brushKind;
        set
        {
            if (!SetProperty(ref _brushKind, value)) return;
            // The noise flows only while the strokes wear it: an animating brush holds the shared clock running.
            _noiseBrush.Animate = value == StrokeBrushKind.Noise;
            RaisePropertyChanged(nameof(StrokeBrush));
        }
    }

    public Brush StrokeBrush => _brushKind switch
    {
        StrokeBrushKind.LinearGradient => _linearBrush,
        StrokeBrushKind.RadialGradient => _radialBrush,
        StrokeBrushKind.Pattern => _patternBrush,
        StrokeBrushKind.Noise => _noiseBrush,
        StrokeBrushKind.Image => _imageBrush,
        StrokeBrushKind.AtlasLayer => _layerBrush,
        _ => _strokeBrush,
    };

    // Over each shape's bounds, or along its line - bound to a DropDown (EnumType=PenBrushMapping).
    private PenBrushMapping _brushMapping;
    public PenBrushMapping BrushMapping { get => _brushMapping; set => SetProperty(ref _brushMapping, value); }

    // Dash on-length / gap drive the pattern as a symbolic glyph string (reliable to bind vs a live collection). On == 0
    // = solid (empty symbols); otherwise a repeating "Dash" whose width/gap come from DashGlyphs.
    private double _dashOn;
    public double DashOn
    {
        get => _dashOn;
        set { if (SetProperty(ref _dashOn, value)) { RaisePropertyChanged(nameof(DashSymbols)); RaisePropertyChanged(nameof(DashGlyphs)); } }
    }

    private double _dashGap = 8;
    public double DashGap
    {
        get => _dashGap;
        set { if (SetProperty(ref _dashGap, value)) RaisePropertyChanged(nameof(DashGlyphs)); }
    }

    public string DashSymbols => _dashOn > 0 ? "Dash" : string.Empty;

    public string DashGlyphs =>
        $"Dash={_dashOn.ToString(CultureInfo.InvariantCulture)}, Gap={_dashGap.ToString(CultureInfo.InvariantCulture)}";
}
