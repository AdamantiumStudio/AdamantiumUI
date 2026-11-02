using Adamantium.Fonts;
using Adamantium.Graphics.Fonts;
using Adamantium.Mathematics;
using Adamantium.UI.Controls.Automation;
using Adamantium.UI.Controls.Base;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Automation;
using Adamantium.UI.Core.Graphics;
using Adamantium.UI.Core.Media;
using Adamantium.UI.Core.RoutedEvents;

namespace Adamantium.UI.Controls.Text;

/// <summary>
/// A line of text laid along a path, as SVG's <c>textPath</c>: each letter stood on the first figure of
/// <see cref="Data"/> - lines, arcs, curves - at its baseline and turned the way the figure runs there, laid by the
/// engine's <see cref="TextPath"/>. It reads left to right however an open figure runs (<see cref="KeepUpright"/>);
/// round a closed one - or one that ends where it starts - it reads the way the figure goes, across its seam. Text
/// longer than the path is clipped to the letters on it, unless <see cref="Overflow"/> says otherwise. The text is
/// shaped as any text of the element - its font, size, spacing, language and features - and drawn in its
/// <see cref="UIComponent.Foreground"/>; <see cref="Stroke"/> draws the path under it. The path is in the element's own
/// coordinates: text standing left of or above them is outside the element.
/// </summary>
public class TextOnPath : InputUIComponent
{
    public static readonly AdamantiumProperty TextProperty = AdamantiumProperty.Register(nameof(Text), typeof(string),
        typeof(TextOnPath), new PropertyMetadata(string.Empty, PropertyMetadataOptions.AffectsMeasure));

    public static readonly AdamantiumProperty DataProperty = AdamantiumProperty.Register(nameof(Data), typeof(Geometry),
        typeof(TextOnPath), new PropertyMetadata(null, PropertyMetadataOptions.AffectsMeasure, DataChangedCallback));

    public static readonly AdamantiumProperty AlignmentProperty = AdamantiumProperty.Register(nameof(Alignment),
        typeof(TextPathAlignment), typeof(TextOnPath),
        new PropertyMetadata(TextPathAlignment.Start, PropertyMetadataOptions.AffectsMeasure));

    public static readonly AdamantiumProperty StartOffsetProperty = AdamantiumProperty.Register(nameof(StartOffset),
        typeof(double), typeof(TextOnPath), new PropertyMetadata(0.0, PropertyMetadataOptions.AffectsMeasure));

    public static readonly AdamantiumProperty AcrossOffsetProperty = AdamantiumProperty.Register(nameof(AcrossOffset),
        typeof(double), typeof(TextOnPath), new PropertyMetadata(0.0, PropertyMetadataOptions.AffectsMeasure));

    public static readonly AdamantiumProperty KeepUprightProperty = AdamantiumProperty.Register(nameof(KeepUpright),
        typeof(bool), typeof(TextOnPath), new PropertyMetadata(true, PropertyMetadataOptions.AffectsMeasure));

    public static readonly AdamantiumProperty OverflowProperty = AdamantiumProperty.Register(nameof(Overflow),
        typeof(TextPathOverflow), typeof(TextOnPath),
        new PropertyMetadata(TextPathOverflow.Clip, PropertyMetadataOptions.AffectsMeasure));

    public static readonly AdamantiumProperty MaxTurnProperty = AdamantiumProperty.Register(nameof(MaxTurn),
        typeof(double), typeof(TextOnPath), new PropertyMetadata(180.0, PropertyMetadataOptions.AffectsMeasure));

    public static readonly AdamantiumProperty StrokeProperty = AdamantiumProperty.Register(nameof(Stroke),
        typeof(Brush), typeof(TextOnPath), new PropertyMetadata(null, PropertyMetadataOptions.AffectsRender));

    public static readonly AdamantiumProperty StrokeThicknessProperty = AdamantiumProperty.Register(
        nameof(StrokeThickness), typeof(double), typeof(TextOnPath),
        new PropertyMetadata(1.0, PropertyMetadataOptions.AffectsMeasure));

    public static readonly AdamantiumProperty IsTextPlacedProperty = AdamantiumProperty.RegisterReadOnly(
        nameof(IsTextPlaced), typeof(bool), typeof(TextOnPath), new PropertyMetadata(false));

    private TextLayout _layout;
    private IFont _layoutFont;
    private TextPath _path;
    private bool _pathSet;
    private string _laidText;
    private double _laidSize = double.NaN;
    private TextComposition _composition;
    private TextAttributes _shaping;
    private TextDirection _direction;
    private double _tracking = double.NaN;
    private TextKerning _kerning;

    static TextOnPath()
    {
        ForegroundProperty.OverrideMetadata(typeof(TextOnPath),
            new PropertyMetadata(null, PropertyMetadataOptions.Inherits | PropertyMetadataOptions.AffectsRender));
    }

    private static void DataChangedCallback(AdamantiumComponent a, AdamantiumPropertyChangedEventArgs e)
    {
        if (a is not TextOnPath text)
        {
            return;
        }

        text._pathSet = false;
        if (e.NewValue is Geometry geometry)
        {
            geometry.ComponentUpdated += text.OnGeometryUpdated;
            text.Follow(DataProperty, () => geometry.ComponentUpdated -= text.OnGeometryUpdated);
        }
        else
        {
            text.Follow(DataProperty, null);
        }
    }

    private void OnGeometryUpdated(object sender, ComponentUpdatedEventArgs e)
    {
        _pathSet = false;
        InvalidateMeasure();
        InvalidateRender(false);
    }

    /// <summary>The text, on one line: a line break in it reads as a space.</summary>
    public string Text
    {
        get => GetValue<string>(TextProperty);
        set => SetValue(TextProperty, value);
    }

    /// <summary>The path the text runs along: its first figure, in the element's own coordinates.</summary>
    public Geometry Data
    {
        get => GetValue<Geometry>(DataProperty);
        set => SetValue(DataProperty, value);
    }

    /// <summary>Where along the path the text stands, as it is read; <see cref="TextPathAlignment.Start"/> by
    /// default.</summary>
    public TextPathAlignment Alignment
    {
        get => GetValue<TextPathAlignment>(AlignmentProperty);
        set => SetValue(AlignmentProperty, value);
    }

    /// <summary>How far, in pixels, the text is moved on along the path from where <see cref="Alignment"/> stands it;
    /// back for a value below nothing.</summary>
    public double StartOffset
    {
        get => GetValue<double>(StartOffsetProperty);
        set => SetValue(StartOffsetProperty, value);
    }

    /// <summary>How far, in pixels, the text's baseline stands above the path as it is read; below it for a value
    /// below nothing.</summary>
    public double AcrossOffset
    {
        get => GetValue<double>(AcrossOffsetProperty);
        set => SetValue(AcrossOffsetProperty, value);
    }

    /// <summary>Whether an open figure that ends left of where it starts is read from its end, so the text does not
    /// stand on its head; true by default.</summary>
    public bool KeepUpright
    {
        get => GetValue<bool>(KeepUprightProperty);
        set => SetValue(KeepUprightProperty, value);
    }

    /// <summary>What is done with text longer than the path: clipped to the letters on it, by default; going on past
    /// its ends, straight the way they run; ended with an ellipsis; shrunk to fit; or not drawn at all.</summary>
    public TextPathOverflow Overflow
    {
        get => GetValue<TextPathOverflow>(OverflowProperty);
        set => SetValue(OverflowProperty, value);
    }

    /// <summary>With <see cref="TextPathOverflow.Hide"/>, the most, in degrees, the path may turn between two
    /// neighboring letters - every bend between them added up - for the text to be drawn; 180 by default.</summary>
    public double MaxTurn
    {
        get => GetValue<double>(MaxTurnProperty);
        set => SetValue(MaxTurnProperty, value);
    }

    /// <summary>The brush the path itself is drawn in under the text; null, the default, for none.</summary>
    public Brush Stroke
    {
        get => GetValue<Brush>(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    /// <summary>How wide the path is drawn when it has a <see cref="Stroke"/>.</summary>
    public double StrokeThickness
    {
        get => GetValue<double>(StrokeThicknessProperty);
        set => SetValue(StrokeThicknessProperty, value);
    }

    /// <summary>Whether a letter of the text stands on the path: false where none fits, or the font is not here yet.
    /// </summary>
    public bool IsTextPlaced
    {
        get => GetValue<bool>(IsTextPlacedProperty);
        private set => SetValue(IsTextPlacedProperty, value);
    }

    protected internal override void OnFontsArrived()
    {
        _layoutFont = null;
        base.OnFontsArrived();
    }

    protected override Size<double> MeasureOverride(Size<double> availableSize)
    {
        var placed = Place();
        if (IsTextPlaced != placed)
        {
            IsTextPlaced = placed;
        }

        if (Data is not { } data)
        {
            return new Size<double>(0, 0);
        }

        data.RecalculateBounds();
        var bounds = data.Bounds;
        var right = bounds.X + bounds.Width + StrokeThickness / 2;
        var bottom = bounds.Y + bounds.Height + StrokeThickness / 2;
        if (placed && _path.TryGetBounds(out _, out var max))
        {
            right = Math.Max(right, max.X);
            bottom = Math.Max(bottom, max.Y);
        }

        return new Size<double>(Math.Max(0, right), Math.Max(0, bottom));
    }

    protected override void OnRender(IDrawingContext context)
    {
        if (Data is not { } data)
        {
            return;
        }

        var session = context.ForControl(this);
        if (Stroke is { } stroke && StrokeThickness > 0)
        {
            session.DrawGeometry(null, data, new Pen(stroke, StrokeThickness));
        }

        if (!IsTextPlaced || _path == null)
        {
            return;
        }

        var layout = _path.Layout;
        var size = layout.CalculatedLayoutSize;
        var parameters = new TextRenderingParameters
        {
            HorizontalTextAlignment = HorizontalTextAlignment.Left,
            VerticalTextAlignment = VerticalTextAlignment.Top,
            TextWrapping = TextWrapping.NoWrap,
            TextTrimming = TextTrimming.None,
            TextArea = new Rectangle<int>(0, 0, (int)Math.Ceiling(size.Width), (int)Math.Ceiling(size.Height)),
        };
        session.DrawText(parameters, size, layout, Foreground, null, null, _path.Effects());
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new TextOnPathAutomationPeer(this);

    /// <summary>Whether <paramref name="localPoint"/> is on a letter drawn on the path.</summary>
    public override bool HitTestCore(Vector2<double> localPoint) => IsTextPlaced && _path.HitTest(localPoint);

    private bool Place()
    {
        if (string.IsNullOrEmpty(Text) || Data is not { } data || !Lay())
        {
            return false;
        }

        if (!_pathSet)
        {
            var figures = data.GetFigures();
            if (figures.Count > 0)
            {
                _path.SetPath(figures[0].Points, figures[0].IsClosed);
            }
            else
            {
                _path.SetPath([], false);
            }

            _pathSet = true;
        }

        return _path.Place(new TextPathOptions
        {
            Alignment = Alignment,
            StartOffset = StartOffset,
            AcrossOffset = AcrossOffset,
            KeepUpright = KeepUpright,
            MaxTurn = double.DegreesToRadians(MaxTurn),
            Overflow = Overflow,
        });
    }

    private bool Lay()
    {
        var loadsSeen = TypefaceStore.LoadedCount;
        if (!TryResolveFont(FontFamily ?? DefaultFontFamily, out var font))
        {
            WaitForFonts(loadsSeen);
        }

        if (font == null)
        {
            return false;
        }

        if (_layout == null || !ReferenceEquals(_layoutFont, font))
        {
            _layout = new TextLayout(font.Typeface, font);
            _layoutFont = font;
            _path = new TextPath(_layout);
            _pathSet = false;
            _laidText = null;
        }

        var text = Text.ReplaceLineEndings(" ");
        var composition = ResolveTextComposition(_composition);
        var shaping = TextShaping(font);
        if (_laidText == text && _laidSize.Equals(FontSize) && ReferenceEquals(_composition, composition) &&
            _direction == TextDirection && _tracking.Equals(Tracking) && _kerning == Kerning &&
            ShapesLike(_shaping, shaping))
        {
            return true;
        }

        _layout.LoadFontsInBackground = !FontAtlasStore.SynchronousFill;
        _layout.Direction = TextDirection;
        _layout.Composition = composition;
        _layout.Tracking = Tracking;
        _layout.Kerning = Kerning;
        var room = new Size<double>(double.PositiveInfinity, double.PositiveInfinity);
        _ = shaping == null
            ? _layout.ProcessText(text, FontSize, room, TextWrapping.NoWrap, TextTrimming.None,
                HorizontalTextAlignment.Left, VerticalTextAlignment.Top)
            : _layout.ProcessText(new AttributedText(text, shaping), FontSize, room, TextWrapping.NoWrap,
                TextTrimming.None, HorizontalTextAlignment.Left, VerticalTextAlignment.Top);
        _laidText = text;
        _laidSize = FontSize;
        _composition = composition;
        _shaping = shaping;
        _direction = TextDirection;
        _tracking = Tracking;
        _kerning = Kerning;
        return true;
    }
}
