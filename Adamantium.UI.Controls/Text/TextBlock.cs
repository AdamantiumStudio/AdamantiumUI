using System.Collections.Generic;
using System.Collections.Specialized;
using System.Text;
using Adamantium.Fonts;
using Adamantium.Graphics.Fonts;
using Adamantium.UI.Controls.Automation;
using Adamantium.UI.Controls.Base;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Automation;
using Adamantium.UI.Core.Graphics;
using Adamantium.UI.Core.Media;
using Adamantium.UI.Core.RoutedEvents;

namespace Adamantium.UI.Controls.Text;

public class TextBlock : InputUIComponent
{
    public static readonly AdamantiumProperty TextProperty = AdamantiumProperty.Register(nameof(Text),
        typeof(string), typeof(TextBlock),
        new PropertyMetadata(string.Empty, PropertyMetadataOptions.AffectsMeasure|PropertyMetadataOptions.AffectsRender,
            TextChangedCallback));
    
    public static readonly AdamantiumProperty TextTrimmingProperty = AdamantiumProperty.Register(nameof(TextTrimming),
        typeof(TextTrimming), typeof(TextBlock),
        new PropertyMetadata(TextTrimming.None, PropertyMetadataOptions.AffectsRender, TextParametersChangedCallback));

    public static readonly AdamantiumProperty TextWrappingProperty = AdamantiumProperty.Register(nameof(TextWrapping),
        typeof(TextWrapping), typeof(TextBlock),
        new PropertyMetadata(TextWrapping.NoWrap, PropertyMetadataOptions.AffectsRender, TextParametersChangedCallback));
    
    public static readonly AdamantiumProperty HorizontalTextAlignmentProperty = AdamantiumProperty.Register(nameof(HorizontalTextAlignment),
        typeof(HorizontalTextAlignment), typeof(TextBlock),
        new PropertyMetadata(HorizontalTextAlignment.Left, PropertyMetadataOptions.AffectsRender, TextParametersChangedCallback));
    
    public static readonly AdamantiumProperty VerticalTextAlignmentProperty = AdamantiumProperty.Register(nameof(VerticalTextAlignment),
        typeof(VerticalTextAlignment), typeof(TextBlock),
        new PropertyMetadata(VerticalTextAlignment.Bottom, PropertyMetadataOptions.AffectsRender, TextParametersChangedCallback));

    public static readonly AdamantiumProperty JustifyLastLineProperty = AdamantiumProperty.Register(nameof(JustifyLastLine),
        typeof(bool), typeof(TextBlock),
        new PropertyMetadata(false, PropertyMetadataOptions.AffectsRender, TextParametersChangedCallback));
    
    // FontFamily is declared (inherited) on UIComponent. On a TextBlock a font change must re-shape the text, so override
    // the metadata with a callback that re-measures - this fires on BOTH a direct set and an inherited change cascaded
    // from an ancestor (RaiseInheritedChange invokes the callback; the AffectsMeasure flag would not fire on the cascade).
    static TextBlock()
    {
        FontFamilyProperty.OverrideMetadata(typeof(TextBlock),
            new PropertyMetadata(null, PropertyMetadataOptions.Inherits, OnFontFamilyChanged));
        // Foreground is the inherited property from UIComponent; keep the White default + render flag a TextBlock had,
        // and preserve Inherits so an ancestor's set Foreground cascades into unstyled text. NOT two-way: a brush is
        // something text is PAINTED with, never something it edits, and a color written back into whatever supplied it
        // is how a selected tab's label kept the selected color after the tab lost selection.
        ForegroundProperty.OverrideMetadata(typeof(TextBlock),
            new PropertyMetadata(Brushes.White,
                PropertyMetadataOptions.Inherits | PropertyMetadataOptions.AffectsRender));
        FontSizeProperty.OverrideMetadata(typeof(TextBlock),
            new PropertyMetadata(12.0d, PropertyMetadataOptions.Inherits | PropertyMetadataOptions.AffectsMeasure | PropertyMetadataOptions.AffectsRender));
    }

    private static void OnFontFamilyChanged(AdamantiumComponent a, AdamantiumPropertyChangedEventArgs e)
    {
        (a as TextBlock)?.InvalidateMeasure();
    }

    public static readonly AdamantiumProperty BackgroundProperty = AdamantiumProperty.Register(nameof(Background),
        typeof (Brush), typeof (TextBlock),
        new PropertyMetadata(Brushes.Transparent, PropertyMetadataOptions.BindsTwoWayByDefault|PropertyMetadataOptions.AffectsRender));
    

    public static readonly AdamantiumProperty StrokeProperty = AdamantiumProperty.Register(nameof(Stroke),
    typeof(Brush), typeof(TextBlock),
    new PropertyMetadata(Brushes.Transparent, PropertyMetadataOptions.BindsTwoWayByDefault | PropertyMetadataOptions.AffectsRender));

    private static void TextParametersChangedCallback(AdamantiumComponent a, AdamantiumPropertyChangedEventArgs e)
    {
        
    }

    private static void TextChangedCallback(AdamantiumComponent a, AdamantiumPropertyChangedEventArgs e)
    {
        
    }

    private TextLayout _textLayout;

    // Text shaping (ProcessText) is expensive, and measure+arrange both ran it EVERY layout pass - so a control whose
    // size animates (e.g. a Button growing, whose templated ContentPresenter re-measures this TextBlock each frame)
    // re-shaped the same unchanged text twice per frame. Cache the result and only re-shape when an input that actually
    // affects the layout changes.
    private Size _cachedSize;
    private bool _hasLayout;
    private IFont _layoutFont;   // the face _textLayout was built for (the font is fixed per TextLayout)
    private string _lastText;
    private double _lastFontSize, _lastWidth, _lastHeight;
    // The width/height the last measure was given (a wrapping block reflows to this when it has no explicit Width, so
    // the wrap boundary comes from the container - like WPF - instead of a hardcoded Width). Infinity = unconstrained.
    private Size _lastConstraint = new Size(double.PositiveInfinity, double.PositiveInfinity);
    private TextWrapping _lastWrapping;
    private TextTrimming _lastTrimming;
    private HorizontalTextAlignment _lastHAlign;
    private VerticalTextAlignment _lastVAlign;
    private bool _lastJustify;
    private TextDirection _lastDirection;
    private TextAttributes _lastShaping;
    private InlineCollection _inlines;
    private bool _inlinesDirty = true;
    private string _inlineText;
    private bool _runFontsPending;

    public TextBlock()
    {
        Id = Guid.NewGuid().ToString();
        // _textLayout is built lazily in EnsureLayout: FontFamily is inherited and may be null here (it resolves against
        // an ancestor / DefaultFontFamily), and the typeface is fixed per TextLayout, so it's rebuilt when the font changes.
    }

    // Shapes the text only when an input that affects the layout changed since the last call; otherwise returns the
    // cached size (and leaves _textLayout as-is). Cheap to call every measure/arrange/render. Width/Height use
    // double.Equals so NaN==NaN counts as "unchanged" (an auto-sized label stays a cache hit while its parent resizes).
    private Size EnsureLayout()
    {
        // Resolve the inherited font (falling back to the single shared default), and (re)build the layout when it
        // changes - the typeface is fixed per TextLayout, so a font change means a new TextLayout for that face.
        var eb0 = System.GC.GetAllocatedBytesForCurrentThread();
        var loadsSeen = TypefaceStore.LoadedCount;
        var fontReady = TryResolveFont(FontFamily ?? DefaultFontFamily, out var font);
        if (_textLayout == null || !ReferenceEquals(_layoutFont, font))
        {
            _textLayout = new TextLayout(font.Typeface, font);
            _layoutFont = font;
            _hasLayout = false;
            LayoutRebuilds++;
        }

        _textLayout.LoadFontsInBackground = !FontAtlasStore.SynchronousFill;
        _textLayout.Direction = TextDirection;
        var eb1 = System.GC.GetAllocatedBytesForCurrentThread();
        FontResolveBytes += eb1 - eb0;

        // Boundary: an explicit Width, else the available width for wrapping or trimming blocks; plain NoWrap labels stay
        // unbounded. NaN means unbounded.
        var width = Width;
        if (double.IsNaN(width)
            && (TextWrapping != TextWrapping.NoWrap || TextTrimming != TextTrimming.None)
            && !double.IsInfinity(_lastConstraint.Width))
            width = _lastConstraint.Width;
        var height = Height;
        var text = HasInlines ? InlineText() : Text;
        var shaping = TextShaping(font);

        if (_hasLayout && !_inlinesDirty
            && _lastText == text && _lastFontSize.Equals(FontSize)
            && _lastWidth.Equals(width) && _lastHeight.Equals(height)
            && _lastWrapping == TextWrapping && _lastTrimming == TextTrimming
            && _lastHAlign == HorizontalTextAlignment && _lastVAlign == VerticalTextAlignment
            && _lastJustify == JustifyLastLine && _lastDirection == TextDirection && ShapesLike(_lastShaping, shaping))
        {
            GuardBytes += System.GC.GetAllocatedBytesForCurrentThread() - eb1;
            GuardHits++;
            return _cachedSize;
        }
        GuardBytes += System.GC.GetAllocatedBytesForCurrentThread() - eb1;

        var eb2 = System.GC.GetAllocatedBytesForCurrentThread();
        _runFontsPending = false;
        var attributed = HasInlines
            ? InlineAttributedText(text, shaping)
            : shaping == null ? null : new AttributedText(text, shaping);
        _cachedSize = attributed == null
            ? _textLayout.ProcessText(text, FontSize, new Size(width, height), TextWrapping, TextTrimming,
                HorizontalTextAlignment, VerticalTextAlignment, JustifyLastLine)
            : _textLayout.ProcessText(attributed, FontSize, new Size(width, height), TextWrapping, TextTrimming,
                HorizontalTextAlignment, VerticalTextAlignment, JustifyLastLine);
        if (!fontReady || _runFontsPending || _textLayout.HasPendingFonts)
        {
            WaitForFonts(loadsSeen);
        }

        // A NoWrap block took that width only so TRIMMING had an edge to work to - it must not then ASK for it. The
        // layout reports the text AREA once one is given, not the letters, so a short label in a wide slot claimed the
        // whole slot and every tab stretched to fill it. Report the ink instead, capped by the area. A WRAPPING block is
        // untouched: there the area IS the answer, because that is the width the text was flowed into.
        if (TextTrimming != TextTrimming.None && TextWrapping == TextWrapping.NoWrap
            && double.IsNaN(Width) && !double.IsNaN(width))
        {
            var ink = System.Math.Ceiling(_textLayout.RealTextDimensions.Width);
            if (ink > 0) _cachedSize = new Size(Math.Min(_cachedSize.Width, ink), _cachedSize.Height);
        }
        ShapeBytes += System.GC.GetAllocatedBytesForCurrentThread() - eb2;
        ShapeCalls++;

        _hasLayout = true;
        _inlinesDirty = false;
        _lastShaping = shaping;
        _lastText = text; _lastFontSize = FontSize; _lastWidth = width; _lastHeight = height;
        _lastWrapping = TextWrapping; _lastTrimming = TextTrimming;
        _lastHAlign = HorizontalTextAlignment; _lastVAlign = VerticalTextAlignment; _lastJustify = JustifyLastLine;
        _lastDirection = TextDirection;
        return _cachedSize;
    }

    public string Text
    {
        get => GetValue<string>(TextProperty);
        set => SetValue(TextProperty, value);
    }

    internal TextLayout Layout => _textLayout;
    
    public TextTrimming TextTrimming
    {
        get => GetValue<TextTrimming>(TextTrimmingProperty);
        set => SetValue(TextTrimmingProperty, value);
    }

    public TextWrapping TextWrapping
    {
        get => GetValue<TextWrapping>(TextWrappingProperty);
        set => SetValue(TextWrappingProperty, value);
    }
    
    public HorizontalTextAlignment HorizontalTextAlignment
    {
        get => GetValue<HorizontalTextAlignment>(HorizontalTextAlignmentProperty);
        set => SetValue(HorizontalTextAlignmentProperty, value);
    }
    
    public VerticalTextAlignment VerticalTextAlignment
    {
        get => GetValue<VerticalTextAlignment>(VerticalTextAlignmentProperty);
        set => SetValue(VerticalTextAlignmentProperty, value);
    }

    /// <summary>
    /// When <see cref="HorizontalTextAlignment"/> is Justify, stretches the last (or only) line to
    /// the full width as well. Default is <c>false</c> - the last line stays ragged (text-align-last).
    /// </summary>
    public bool JustifyLastLine
    {
        get => GetValue<bool>(JustifyLastLineProperty);
        set => SetValue(JustifyLastLineProperty, value);
    }

    public Brush Background
    {
        get => GetValue<Brush>(BackgroundProperty);
        set => SetValue(BackgroundProperty, value);
    }
    
    public Brush Stroke
    {
        get => GetValue<Brush>(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    // --- Bindable inline runs -----------------------------------------------------------------------------------------
    // When Inlines has content it REPLACES Text: the runs lay out as one attributed text, so they shape, wrap and align
    // together. Each Run is a logical child (so it inherits this block's DataContext and its {Binding}s resolve), and this
    // block listens to every Run's Changed to re-shape when a bound value updates.

    /// <summary>Bindable inline content. When non-empty it is rendered instead of <see cref="Text"/>: each <see cref="Run"/>
    /// carries its own bound text, color, size, background, lines, features and language.</summary>
    public InlineCollection Inlines
    {
        get
        {
            if (_inlines == null)
            {
                _inlines = [];
                _inlines.CollectionChanged += OnInlinesChanged;
            }
            return _inlines;
        }
    }

    private bool HasInlines => _inlines is { Count: > 0 };

    private void OnInlinesChanged(object sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems != null)
            foreach (Inline old in e.OldItems) { old.Changed -= OnInlineChanged; RemoveLogicalChild(old); }
        if (e.NewItems != null)
            foreach (Inline added in e.NewItems) { AddLogicalChild(added); added.Changed += OnInlineChanged; }
        _inlinesDirty = true;
        InvalidateMeasure();
    }

    private void OnInlineChanged(object sender, System.EventArgs e)
    {
        _inlinesDirty = true;
        InvalidateMeasure();
    }

    private string InlineText()
    {
        if (_inlinesDirty || _inlineText == null)
        {
            var text = new StringBuilder();
            foreach (var inline in _inlines)
            {
                if (inline is Run run)
                {
                    text.Append(run.Text);
                }
            }

            _inlineText = text.ToString();
        }

        return _inlineText;
    }

    private AttributedText InlineAttributedText(string text, TextAttributes shaping)
    {
        var attributed = new AttributedText(text, shaping);
        var family = FontFamily ?? DefaultFontFamily;
        var start = 0;
        foreach (var inline in _inlines)
        {
            if (inline is not Run run)
            {
                continue;
            }

            var length = (run.Text ?? string.Empty).Length;
            var weight = run.FontWeight ?? FontWeight;
            var style = run.FontStyle ?? FontStyle;
            _runFontsPending |= !TryResolveFont(family, weight, style, run.FontStretch ?? FontStretch,
                run.FontVariations ?? FontVariations, out var runFont);
            attributed.Apply(start, length, new TextAttributes
            {
                Font = runFont,
                Synthesis = FontSynthesisRules.Needed(runFont, weight, style, run.FontSynthesis ?? FontSynthesis),
                Features = Typography.FeaturesOf(run, run.FontFeatures ?? FontFeatures),
                Language = run.Language,
                ColorPalette = run.ColorPalette,
                FontSize = double.IsNaN(run.FontSize) ? null : run.FontSize,
                Foreground = (run.Foreground as SolidColorBrush)?.Color,
                Background = (run.Background as SolidColorBrush)?.Color,
                Decorations = run.TextDecorations == TextDecorations.None ? null : run.TextDecorations,
            });
            start += length;
        }

        return attributed;
    }

    private void DrawAdornments(IDrawingSession session, bool backgrounds)
    {
        if (_textLayout.AttributedText == null)
        {
            return;
        }

        foreach (var adornment in _textLayout.GetAdornments())
        {
            if ((adornment.Kind == TextAdornmentKind.Background) != backgrounds)
            {
                continue;
            }

            var brush = adornment.Color is { } color ? new SolidColorBrush(color) : Foreground;
            var rect = adornment.Rect;
            if (adornment.Kind == TextAdornmentKind.Squiggle)
            {
                DrawSquiggle(session, rect, brush);
            }
            else
            {
                session.DrawRectangle(brush, new Rect(rect.X, rect.Y, rect.Width, rect.Height));
            }
        }
    }

    private static void DrawSquiggle(IDrawingSession session, RectangleF band, Brush brush)
    {
        var thickness = band.Height / 3;
        var pen = new Pen(brush, thickness);
        var step = band.Height;
        var top = band.Y + thickness / 2;
        var bottom = band.Y + band.Height - thickness / 2;
        var x = (double)band.X;
        var up = false;
        while (x < band.Right)
        {
            var next = Math.Min(x + step, band.Right);
            var fraction = (next - x) / step;
            var from = up ? bottom : top;
            var to = from + (up ? top - bottom : bottom - top) * fraction;
            session.DrawLine(new Vector2(x, from), new Vector2(next, to), pen);
            x = next;
            up = !up;
        }
    }

    /// <summary>TEMP: what the block's OWN measure/arrange allocates, against what the layout histogram charges the type
    /// (116KB a call). ProcessText accounts for 2.7KB of that, so the rest is either here or in the measure protocol
    /// around it - and this session has been wrong six times about which.</summary>
    public static long OverrideBytes;
    public static int OverrideCount;

    /// <summary>EnsureLayout split three ways: resolving the font (and rebuilding TextLayout when it changed), the
    /// cache guard's own property reads, and the shaping call. ProcessText's internals account for 1% of what the type
    /// is charged, so the other 99% is in one of these twenty lines.</summary>
    public static long FontResolveBytes;
    public static long GuardBytes;
    public static long ShapeBytes;
    public static int LayoutRebuilds;
    public static int GuardHits;
    public static int ShapeCalls;

    /// <summary>Lays the text out again in the fonts that arrived.</summary>
    protected internal override void OnFontsArrived()
    {
        _hasLayout = false;
        base.OnFontsArrived();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        _lastConstraint = availableSize;   // a wrapping block reflows to this (its container's width) when it has no explicit Width
        var b0 = System.GC.GetAllocatedBytesForCurrentThread();
        var size = EnsureLayout();
        // The layout is what gets drawn. A measure whose size came out the same has no arrange after it, so a block that
        // already has a slot goes back to the slot's width - else right-aligned text stood at the width it was MEASURED
        // against, short of the slot's edge.
        if (UseSlotWidth(RenderSize.Width))
            EnsureLayout();
        OverrideBytes += System.GC.GetAllocatedBytesForCurrentThread() - b0;
        OverrideCount++;
        return size;
    }

    /// <summary>Lays the text out and takes the whole slot, so text alignment works within it.</summary>
    protected override Size ArrangeOverride(Size finalSize)
    {
        // A trimmed block trims to the slot it actually GOT, which is not always the one it was measured against: a tab
        // header is measured unbounded and then capped by the tab's MaxWidth, so the boundary only exists here. Re-stated
        // before the layout call so the ellipsis lands at the real edge instead of at a width nobody will give it.
        UseSlotWidth(finalSize.Width);

        var b0 = System.GC.GetAllocatedBytesForCurrentThread();
        EnsureLayout();
        OverrideBytes += System.GC.GetAllocatedBytesForCurrentThread() - b0;
        OverrideCount++;
        return finalSize;
    }

    private bool UseSlotWidth(double slot)
    {
        if (TextTrimming == TextTrimming.None || !double.IsNaN(Width) || slot <= 0)
            return false;

        _lastConstraint = new Size(slot, _lastConstraint.Height);
        return true;
    }

    TextRenderingParameters GetTextRenderingParameters()
    {
        var textPos = new Vector2F();

        return new TextRenderingParameters()
        {
            HorizontalTextAlignment = HorizontalTextAlignment,
            VerticalTextAlignment = VerticalTextAlignment,
            JustifyLastLine = JustifyLastLine,
            TextTrimming = TextTrimming,
            TextWrapping = TextWrapping,
            Color = ((SolidColorBrush)Foreground).Color,
            TextArea = new Rectangle(textPos, DesiredSize)
        };
    }

    protected override void OnRender(IDrawingContext context)
    {
        var session = context.ForControl(this);
        EnsureLayout();   // refresh shaping if a render-only property (alignment/wrapping) changed since the last measure
        DrawAdornments(session, backgrounds: true);
        session.DrawText(GetTextRenderingParameters(), DesiredSize, _textLayout, Foreground, Background, Stroke);
        DrawAdornments(session, backgrounds: false);
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new TextBlockAutomationPeer(this);
}