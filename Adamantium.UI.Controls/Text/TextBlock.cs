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
using Adamantium.UI.Core.Input;
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

    public static readonly AdamantiumProperty ColumnsProperty = AdamantiumProperty.Register(nameof(Columns),
        typeof(int), typeof(TextBlock),
        new PropertyMetadata(1, PropertyMetadataOptions.AffectsMeasure, TextParametersChangedCallback));

    public static readonly AdamantiumProperty ColumnGapProperty = AdamantiumProperty.Register(nameof(ColumnGap),
        typeof(double), typeof(TextBlock),
        new PropertyMetadata(16.0, PropertyMetadataOptions.AffectsMeasure, TextParametersChangedCallback));

    public static readonly AdamantiumProperty ExclusionsProperty = AdamantiumProperty.Register(nameof(Exclusions),
        typeof(ExclusionList), typeof(TextBlock),
        new PropertyMetadata(null, PropertyMetadataOptions.AffectsMeasure, TextParametersChangedCallback));

    public static readonly AdamantiumProperty DropCapLinesProperty = AdamantiumProperty.Register(nameof(DropCapLines),
        typeof(int), typeof(TextBlock),
        new PropertyMetadata(0, PropertyMetadataOptions.AffectsMeasure, TextParametersChangedCallback));

    public static readonly AdamantiumProperty DropCapCharactersProperty = AdamantiumProperty.Register(nameof(DropCapCharacters),
        typeof(int), typeof(TextBlock),
        new PropertyMetadata(1, PropertyMetadataOptions.AffectsMeasure, TextParametersChangedCallback));

    public static readonly AdamantiumProperty DropCapFontFamilyProperty = AdamantiumProperty.Register(nameof(DropCapFontFamily),
        typeof(FontFamily), typeof(TextBlock),
        new PropertyMetadata(null, PropertyMetadataOptions.AffectsMeasure, TextParametersChangedCallback));

    public static readonly AdamantiumProperty WritingModeProperty = AdamantiumProperty.Register(nameof(WritingMode),
        typeof(WritingMode), typeof(TextBlock),
        new PropertyMetadata(WritingMode.Horizontal, PropertyMetadataOptions.AffectsMeasure, TextParametersChangedCallback));
    
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
    private Hyphens _lastHyphens;
    private LineBreaking _lastLineBreaking;
    private TabStopList _lastTabStops;
    private bool _lastOpticalMargins;
    private DropCap _lastDropCap;
    private int _lastColumns = 1;
    private double _lastColumnGap;
    private ExclusionList _lastExclusions;
    private WritingMode _lastWritingMode;
    private SpacingRange _lastWordSpacing;
    private SpacingRange _lastLetterSpacing;
    private double _lastTracking;
    private SpacingRange _lastGlyphScaling;
    private bool _lastJustificationAlternates;
    private HorizontalTextAlignment _lastLastLineAlignment;
    private HorizontalTextAlignment _lastSingleWord;
    private bool _lastKashidas;
    private const float Unbounded = 1e7f;
    private const int MostColumnGrowth = 200;
    private const char LineSeparator = (char)0x2028;
    private TextAttributes _lastShaping;

    private readonly List<(int Start, int End, Hyperlink Link)> _links = [];
    private Hyperlink _pressedLink;
    private int _focusedLink = -1;
    private bool _overridingCursor;
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
        _textLayout.Hyphens = Hyphens;
        _textLayout.LineBreaking = LineBreaking;
        _textLayout.TabStops = TabStops;
        _textLayout.OpticalMarginAlignment = OpticalMarginAlignment;
        var dropCap = DropCapOf(ref fontReady);
        _textLayout.DropCap = dropCap;
        var vertical = WritingMode == WritingMode.VerticalRightToLeft;
        _textLayout.WritingMode = WritingMode;
        _textLayout.WordSpacing = WordSpacing;
        _textLayout.LetterSpacing = LetterSpacing;
        _textLayout.Tracking = Tracking;
        _textLayout.GlyphScaling = GlyphScaling;
        _textLayout.JustificationAlternates = JustificationAlternates;
        _textLayout.LastLineAlignment = LastLineAlignment;
        _textLayout.SingleWordJustification = SingleWordJustification;
        _textLayout.Kashidas = Kashidas;
        var eb1 = System.GC.GetAllocatedBytesForCurrentThread();
        FontResolveBytes += eb1 - eb0;

        // Boundary: an explicit Width, else the available width for wrapping or trimming blocks; plain NoWrap labels stay
        // unbounded. NaN means unbounded. Vertical lines run down, so there the height bounds them.
        var width = Width;
        var height = Height;
        var bounded = TextWrapping != TextWrapping.NoWrap || TextTrimming != TextTrimming.None;
        if (!vertical && double.IsNaN(width) && bounded && !double.IsInfinity(_lastConstraint.Width))
        {
            width = _lastConstraint.Width;
        }

        if (vertical && double.IsNaN(height) && bounded && !double.IsInfinity(_lastConstraint.Height))
        {
            height = _lastConstraint.Height;
        }
        var text = HasInlines ? InlineText() : Text;
        var shaping = TextShaping(font);

        if (_hasLayout && !_inlinesDirty
            && _lastText == text && _lastFontSize.Equals(FontSize)
            && _lastWidth.Equals(width) && _lastHeight.Equals(height)
            && _lastWrapping == TextWrapping && _lastTrimming == TextTrimming
            && _lastHAlign == HorizontalTextAlignment && _lastVAlign == VerticalTextAlignment
            && _lastJustify == JustifyLastLine && _lastDirection == TextDirection && _lastHyphens == Hyphens
            && _lastLineBreaking == LineBreaking && Equals(_lastTabStops, TabStops)
            && _lastOpticalMargins == OpticalMarginAlignment && Equals(_lastDropCap, dropCap)
            && _lastColumns == Columns && _lastColumnGap.Equals(ColumnGap) && Equals(_lastExclusions, Exclusions)
            && _lastWritingMode == WritingMode && _lastWordSpacing.Equals(WordSpacing)
            && _lastLetterSpacing.Equals(LetterSpacing) && _lastTracking.Equals(Tracking)
            && _lastGlyphScaling.Equals(GlyphScaling) && _lastJustificationAlternates == JustificationAlternates
            && _lastLastLineAlignment == LastLineAlignment && _lastSingleWord == SingleWordJustification
            && _lastKashidas == Kashidas
            && ShapesLike(_lastShaping, shaping))
        {
            GuardBytes += System.GC.GetAllocatedBytesForCurrentThread() - eb1;
            GuardHits++;
            return _cachedSize;
        }
        GuardBytes += System.GC.GetAllocatedBytesForCurrentThread() - eb1;

        var eb2 = System.GC.GetAllocatedBytesForCurrentThread();
        _runFontsPending = false;
        _links.Clear();
        var attributed = HasInlines
            ? InlineAttributedText(text, shaping)
            : shaping == null ? null : new AttributedText(text, shaping);
        if (Focusable != _links.Count > 0)
        {
            SetCurrentValue(FocusableProperty, _links.Count > 0);
        }

        if (_focusedLink >= _links.Count)
        {
            _focusedLink = _links.Count - 1;
        }
        _textLayout.Exclusions = Exclusions;
        _textLayout.Frames = FramesOf(vertical ? height : width, vertical ? width : height, vertical, Lay, out var growth);
        _cachedSize = Lay();
        for (var grown = 0; growth > 0 && _textLayout.OversetIndex < (text?.Length ?? 0) && grown < MostColumnGrowth; grown++)
        {
            _textLayout.Frames = _textLayout.Frames
                .Select(frame => vertical
                    ? new RectangleF(frame.X, frame.Y, frame.Width + growth, frame.Height)
                    : new RectangleF(frame.X, frame.Y, frame.Width, frame.Height + growth))
                .ToArray();
            _cachedSize = Lay();
        }
        if (!fontReady || _runFontsPending || _textLayout.HasPendingFonts)
        {
            WaitForFonts(loadsSeen);
        }

        // A NoWrap block took that width only so TRIMMING had an edge to work to - it must not then ASK for it. The
        // layout reports the text AREA once one is given, not the letters, so a short label in a wide slot claimed the
        // whole slot and every tab stretched to fill it. Report the ink instead, capped by the area. A WRAPPING block is
        // untouched: there the area IS the answer, because that is the width the text was flowed into.
        if (TextTrimming != TextTrimming.None && TextWrapping == TextWrapping.NoWrap
            && double.IsNaN(Width) && !double.IsNaN(width) && !vertical)
        {
            var ink = System.Math.Ceiling(_textLayout.RealTextDimensions.Width);
            if (ink > 0) _cachedSize = new Size(Math.Min(_cachedSize.Width, ink), _cachedSize.Height);
        }

        if (TextTrimming != TextTrimming.None && TextWrapping == TextWrapping.NoWrap
            && double.IsNaN(Height) && !double.IsNaN(height) && vertical)
        {
            var ink = System.Math.Ceiling(_textLayout.RealTextDimensions.Height);
            if (ink > 0) _cachedSize = new Size(_cachedSize.Width, Math.Min(_cachedSize.Height, ink));
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
        _lastHyphens = Hyphens;
        _lastLineBreaking = LineBreaking;
        _lastTabStops = TabStops;
        _lastOpticalMargins = OpticalMarginAlignment;
        _lastDropCap = dropCap;
        _lastColumns = Columns;
        _lastColumnGap = ColumnGap;
        _lastExclusions = Exclusions;
        _lastWritingMode = WritingMode;
        _lastWordSpacing = WordSpacing;
        _lastLetterSpacing = LetterSpacing;
        _lastTracking = Tracking;
        _lastGlyphScaling = GlyphScaling;
        _lastJustificationAlternates = JustificationAlternates;
        _lastLastLineAlignment = LastLineAlignment;
        _lastSingleWord = SingleWordJustification;
        _lastKashidas = Kashidas;
        return _cachedSize;

        Size Lay() => attributed == null
            ? _textLayout.ProcessText(text, FontSize, new Size(width, height), TextWrapping, TextTrimming,
                HorizontalTextAlignment, vertical ? VerticalTextAlignment.Top : VerticalTextAlignment, JustifyLastLine)
            : _textLayout.ProcessText(attributed, FontSize, new Size(width, height), TextWrapping, TextTrimming,
                HorizontalTextAlignment, vertical ? VerticalTextAlignment.Top : VerticalTextAlignment, JustifyLastLine);
    }

    private RectangleF[] FramesOf(double length, double across, bool vertical, Func<Size> lay, out float growth)
    {
        growth = 0;
        var columns = Math.Max(1, Columns);
        if (TextWrapping != TextWrapping.WrapByWords || double.IsNaN(length) || double.IsInfinity(length)
            || (columns == 1 && (Exclusions == null || Exclusions.Count == 0)))
        {
            return null;
        }

        var gap = Math.Max(0, ColumnGap);
        var columnLength = (float)Math.Max(1, (length - gap * (columns - 1)) / columns);
        var columnAcross = (float)across;
        if (double.IsNaN(across) || double.IsInfinity(across))
        {
            columnAcross = Unbounded;
            if (columns > 1 || vertical)
            {
                _textLayout.Frames = [Frame(0, Unbounded)];
                lay();
                var lineHeight = Enumerable.Range(0, _textLayout.LineCount).Min(line => _textLayout.GetLine(line).Height);
                growth = (float)lineHeight;
                columnAcross = (float)(Math.Ceiling(_textLayout.LineCount / (double)columns) * lineHeight + 1);
            }
        }

        return Enumerable.Range(0, columns)
            .Select(column => Frame((float)(column * (columnLength + gap)), columnAcross))
            .ToArray();

        RectangleF Frame(float start, float extent) => vertical
            ? new RectangleF(0, start, extent, columnLength)
            : new RectangleF(start, 0, columnLength, extent);
    }

    private DropCap DropCapOf(ref bool fontReady)
    {
        if (DropCapLines < 2 || DropCapCharacters < 1)
        {
            return null;
        }

        IFont font = null;
        if (DropCapFontFamily != null)
        {
            fontReady &= TryResolveFont(DropCapFontFamily, out font);
        }

        return new DropCap(DropCapLines, DropCapCharacters, font);
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

    /// <summary>How many columns text wrapped by words flows through, side by side, the next taking up where the last
    /// ends; 1 by default. Without a <see cref="Height"/> the columns are as tall as the text split evenly needs.</summary>
    public int Columns
    {
        get => GetValue<int>(ColumnsProperty);
        set => SetValue(ColumnsProperty, value);
    }

    /// <summary>The space between <see cref="Columns"/>; 16 by default.</summary>
    public double ColumnGap
    {
        get => GetValue<double>(ColumnGapProperty);
        set => SetValue(ColumnGapProperty, value);
    }

    /// <summary>Areas of the block, as a picture placed over it takes, that text wrapped by words flows around:
    /// <c>Exclusions="0,0,120,90; 240,200,100,100"</c> - left, top, width and height of each.</summary>
    public ExclusionList Exclusions
    {
        get => GetValue<ExclusionList>(ExclusionsProperty);
        set => SetValue(ExclusionsProperty, value);
    }

    /// <summary>Whether the text did not all fit into the block's <see cref="Columns"/>, so that the rest is not shown.</summary>
    public bool IsOverset => _textLayout != null && _textLayout.Frames != null && _textLayout.OversetIndex < (_textLayout.Text?.Length ?? 0);

    /// <summary>How many lines a drop cap - the first characters set large, the lines running beside them - spans:
    /// 2 or more; 0 (the default) for none. Text wrapped by words only.</summary>
    public int DropCapLines
    {
        get => GetValue<int>(DropCapLinesProperty);
        set => SetValue(DropCapLinesProperty, value);
    }

    /// <summary>How many characters the drop cap takes; 1 by default.</summary>
    public int DropCapCharacters
    {
        get => GetValue<int>(DropCapCharactersProperty);
        set => SetValue(DropCapCharactersProperty, value);
    }

    /// <summary>The family the drop cap is set in, as a decorative face of initials; unset, the text's own.</summary>
    public FontFamily DropCapFontFamily
    {
        get => GetValue<FontFamily>(DropCapFontFamilyProperty);
        set => SetValue(DropCapFontFamilyProperty, value);
    }

    /// <summary>Which way lines run: across (the default), or down and stacked from right to left, as Chinese and
    /// Japanese are set vertically. A vertical line is as long as the block is high - its <see cref="Height"/>, else the
    /// height it is given - the block asks to be as wide as its lines take, the first line stands at its right edge
    /// (<see cref="VerticalTextAlignment"/> does not apply), <see cref="HorizontalTextAlignment"/> places text along
    /// the lines (<see cref="HorizontalTextAlignment.Left"/> at their top), and <see cref="Columns"/> stack one under
    /// another. Drop caps are for horizontal text only.</summary>
    public WritingMode WritingMode
    {
        get => GetValue<WritingMode>(WritingModeProperty);
        set => SetValue(WritingModeProperty, value);
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
    // together. Each inline is a logical child (so it inherits this block's DataContext and its {Binding}s resolve), and
    // this block listens to every inline's Changed - a span passes on its own inlines' - to re-shape when a bound value
    // updates.

    /// <summary>Bindable inline content. When non-empty it is rendered instead of <see cref="Text"/>: each <see cref="Run"/>
    /// carries its own bound text, color, size, background, lines, features and language, a <see cref="Span"/> (or
    /// <see cref="Bold"/>, <see cref="Italic"/>, <see cref="Underline"/>) gives them to the inlines in it, and a
    /// <see cref="LineBreak"/> ends the line. In markup the inlines and text can be written straight inside the block:
    /// <c>&lt;TextBlock&gt;Hello &lt;Bold&gt;world&lt;/Bold&gt;&lt;/TextBlock&gt;</c>.</summary>
    [Content]
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

    internal string ShownText => HasInlines ? InlineText().Replace(LineSeparator, '\n') : Text;

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
            AppendInlineText(_inlines, text);
            _inlineText = text.ToString();
        }

        return _inlineText;
    }

    private static void AppendInlineText(InlineCollection inlines, StringBuilder text)
    {
        foreach (var inline in inlines)
        {
            switch (inline)
            {
                case Run run:
                    text.Append(run.Text);
                    break;
                case LineBreak:
                    text.Append(LineSeparator);
                    break;
                case Span span:
                    AppendInlineText(span.Inlines, text);
                    break;
            }
        }
    }

    private AttributedText InlineAttributedText(string text, TextAttributes shaping)
    {
        var attributed = new AttributedText(text, shaping);
        var start = 0;
        _links.Clear();
        ApplyInlines(_inlines, [], attributed, ref start);
        return attributed;
    }

    private void ApplyInlines(InlineCollection inlines, List<Inline> chain, AttributedText attributed, ref int start)
    {
        foreach (var inline in inlines)
        {
            chain.Add(inline);
            switch (inline)
            {
                case Run run:
                    var length = (run.Text ?? string.Empty).Length;
                    attributed.Apply(start, length, InlineAttributes(chain));
                    start += length;
                    break;
                case LineBreak:
                    attributed.Apply(start, 1, InlineAttributes(chain));
                    start++;
                    break;
                case Span span:
                    var spanStart = start;
                    ApplyInlines(span.Inlines, chain, attributed, ref start);
                    if (span is Hyperlink link)
                    {
                        _links.Add((spanStart, start, link));
                    }

                    break;
            }

            chain.RemoveAt(chain.Count - 1);
        }
    }

    // The chain runs from the outermost span down to the inline itself: each sets what it sets, the nearer one wins,
    // and the lines add up.
    private TextAttributes InlineAttributes(List<Inline> chain)
    {
        var fontSize = double.NaN;
        Brush foreground = null;
        Brush background = null;
        var decorations = TextDecorations.None;
        FontWeight? weight = null;
        FontStyle? style = null;
        FontStretch? stretch = null;
        FontSynthesis? synthesis = null;
        FontFeatureList features = null;
        FontVariationList variations = null;
        string language = null;
        int? palette = null;
        double? tracking = null;
        double? shift = null;
        foreach (var inline in chain)
        {
            if (!double.IsNaN(inline.FontSize))
            {
                fontSize = inline.FontSize;
            }

            foreground = inline.Foreground ?? foreground;
            background = inline.Background ?? background;
            decorations |= inline.TextDecorations;
            weight = inline.FontWeight ?? weight;
            style = inline.FontStyle ?? style;
            stretch = inline.FontStretch ?? stretch;
            synthesis = inline.FontSynthesis ?? synthesis;
            features = inline.FontFeatures ?? features;
            variations = inline.FontVariations ?? variations;
            language = inline.Language ?? language;
            palette = inline.ColorPalette ?? palette;
            tracking = inline.Tracking ?? tracking;
            shift = inline.BaselineShift ?? shift;
        }

        var resolvedWeight = weight ?? FontWeight;
        var resolvedStyle = style ?? FontStyle;
        _runFontsPending |= !TryResolveFont(FontFamily ?? DefaultFontFamily, resolvedWeight, resolvedStyle,
            stretch ?? FontStretch, variations ?? FontVariations, out var font);
        return new TextAttributes
        {
            Font = font,
            Synthesis = FontSynthesisRules.Needed(font, resolvedWeight, resolvedStyle, synthesis ?? FontSynthesis),
            Features = Typography.FeaturesOf(chain[^1], features ?? FontFeatures),
            Language = language,
            ColorPalette = palette,
            Tracking = tracking,
            BaselineShift = shift,
            FontSize = double.IsNaN(fontSize) ? null : fontSize,
            Foreground = (foreground as SolidColorBrush)?.Color,
            Background = (background as SolidColorBrush)?.Color,
            Decorations = decorations == TextDecorations.None ? null : decorations,
        };
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
            rect.X += LinesShift();
            if (adornment.Kind == TextAdornmentKind.Squiggle)
            {
                DrawSquiggle(session, rect, brush);
            }
            else if (adornment.Kind == TextAdornmentKind.Background)
            {
                session.DrawRectangle(brush, new Rect(rect.X, rect.Y, rect.Width, rect.Height));
            }
            else
            {
                session.DrawRectangle(brush, OnPixels(new Rect(rect.X, rect.Y, rect.Width, rect.Height)));
            }
        }
    }

    private Rect OnPixels(Rect line)
    {
        var vertical = WritingMode == WritingMode.VerticalRightToLeft;
        var thickness = vertical ? line.Width : line.Height;
        var snapped = line;
        if (!this.Snap(ref snapped, ref thickness))
        {
            return line;
        }

        return vertical
            ? new Rect(snapped.X, snapped.Y, thickness, snapped.Height)
            : new Rect(snapped.X, snapped.Y, snapped.Width, thickness);
    }

    private void DrawSquiggle(IDrawingSession session, RectangleF band, Brush brush)
    {
        var down = WritingMode == WritingMode.VerticalRightToLeft;
        var depth = down ? band.Width : band.Height;
        var thickness = depth / 3;
        var pen = new Pen(brush, thickness);
        var step = depth;
        var top = (down ? band.X : band.Y) + thickness / 2;
        var bottom = (down ? band.X : band.Y) + depth - thickness / 2;
        var x = (double)(down ? band.Y : band.X);
        var end = down ? band.Bottom : band.Right;
        var up = false;
        while (x < end)
        {
            var next = Math.Min(x + step, end);
            var fraction = (next - x) / step;
            var from = up ? bottom : top;
            var to = from + (up ? top - bottom : bottom - top) * fraction;
            session.DrawLine(down ? new Vector2(from, x) : new Vector2(x, from),
                down ? new Vector2(to, next) : new Vector2(next, to), pen);
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
        if (UseSlot(RenderSize))
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
        UseSlot(finalSize);

        var b0 = System.GC.GetAllocatedBytesForCurrentThread();
        EnsureLayout();
        OverrideBytes += System.GC.GetAllocatedBytesForCurrentThread() - b0;
        OverrideCount++;
        return finalSize;
    }

    private bool UseSlot(Size slot)
    {
        if (TextTrimming == TextTrimming.None)
        {
            return false;
        }

        if (WritingMode == WritingMode.VerticalRightToLeft)
        {
            if (!double.IsNaN(Height) || slot.Height <= 0)
            {
                return false;
            }

            _lastConstraint = new Size(_lastConstraint.Width, slot.Height);
            return true;
        }

        if (!double.IsNaN(Width) || slot.Width <= 0)
        {
            return false;
        }

        _lastConstraint = new Size(slot.Width, _lastConstraint.Height);
        return true;
    }

    internal float LinesShift() => WritingMode == WritingMode.VerticalRightToLeft && double.IsNaN(Width)
        ? (float)Math.Round(RenderSize.Width - _textLayout.CalculatedLayoutSize.Width)
        : 0;

    TextRenderingParameters GetTextRenderingParameters()
    {
        var textPos = new Vector2F(LinesShift(), 0);

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
        DrawFocusedLink(session);
    }

    private void DrawFocusedLink(IDrawingSession session)
    {
        if (_focusedLink < 0 || _focusedLink >= _links.Count)
        {
            return;
        }

        var (start, end, _) = _links[_focusedLink];
        var pen = new Pen(Foreground, 1);
        foreach (var rect in _textLayout.GetRangeRects(start, end))
        {
            session.DrawRectangle(Brushes.Transparent, new Rect(rect.X + LinesShift(), rect.Y, rect.Width, rect.Height), pen);
        }
    }

    internal IReadOnlyList<Hyperlink> Links => _links.ConvertAll(link => link.Link);

    internal IReadOnlyList<Rect> LinkRects(Hyperlink link)
    {
        foreach (var (start, end, candidate) in _links)
        {
            if (ReferenceEquals(candidate, link))
            {
                return _textLayout.GetRangeRects(start, end)
                    .Select(rect => new Rect(rect.X + LinesShift(), rect.Y, rect.Width, rect.Height))
                    .ToList();
            }
        }

        return [];
    }

    internal string LinkText(Hyperlink link)
    {
        foreach (var (start, end, candidate) in _links)
        {
            if (ReferenceEquals(candidate, link))
            {
                return InlineText().Substring(start, end - start).Replace(LineSeparator, ' ');
            }
        }

        return string.Empty;
    }

    private Hyperlink LinkAt(Vector2 point)
    {
        if (_links.Count == 0)
        {
            return null;
        }

        var hit = _textLayout.HitTest(point.X - LinesShift(), point.Y);
        if (!hit.IsInside)
        {
            return null;
        }

        foreach (var (start, end, link) in _links)
        {
            if (hit.Index >= start && hit.Index < end)
            {
                return link;
            }
        }

        return null;
    }

    protected override void OnMouseMove(object sender, MouseEventArgs e)
    {
        base.OnMouseMove(sender, e);
        var overLink = LinkAt(e.GetPosition(this)) != null;
        if (overLink && Mouse.OverrideCursor == null)
        {
            Mouse.OverrideCursor = Cursors.Of(CursorType.Hand);
            _overridingCursor = true;
        }
        else if (!overLink && _overridingCursor)
        {
            Mouse.OverrideCursor = null;
            _overridingCursor = false;
        }
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        if (_overridingCursor)
        {
            Mouse.OverrideCursor = null;
            _overridingCursor = false;
        }
    }

    protected override void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(sender, e);
        _pressedLink = LinkAt(e.GetPosition(this));
        if (_pressedLink != null)
        {
            e.Handled = true;
        }
    }

    protected override void OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(sender, e);
        var link = LinkAt(e.GetPosition(this));
        if (link != null && ReferenceEquals(link, _pressedLink))
        {
            e.Handled = true;
            link.Activate();
        }

        _pressedLink = null;
    }

    protected override void OnGotFocus(RoutedEventArgs e)
    {
        base.OnGotFocus(e);
        if (_links.Count > 0)
        {
            var backwards = (Keyboard.Modifiers & (InputModifiers.LeftShift | InputModifiers.RightShift)) != 0;
            _focusedLink = backwards ? _links.Count - 1 : 0;
            InvalidateRender(false);
        }
    }

    protected override void OnLostFocus(RoutedEventArgs e)
    {
        base.OnLostFocus(e);
        _focusedLink = -1;
        InvalidateRender(false);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (_focusedLink < 0 || _focusedLink >= _links.Count || e.Handled)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Tab:
                var step = (Keyboard.Modifiers & (InputModifiers.LeftShift | InputModifiers.RightShift)) != 0 ? -1 : 1;
                if (_focusedLink + step >= 0 && _focusedLink + step < _links.Count)
                {
                    _focusedLink += step;
                    InvalidateRender(false);
                    e.Handled = true;
                }

                break;
            case Key.Enter:
            case Key.Space:
                e.Handled = true;
                _links[_focusedLink].Link.Activate();
                break;
        }
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new TextBlockAutomationPeer(this);
}