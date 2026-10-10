using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Adamantium.Fonts;
using Adamantium.Graphics.Fonts;
using Adamantium.Mathematics;
using Adamantium.ProceduralGeometry;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Base;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Automation;
using Adamantium.UI.Core.Graphics;
using Adamantium.UI.Core.Input;
using Adamantium.UI.Core.Media;
using Adamantium.UI.Core.Media.Animation;
using Adamantium.UI.Core.RoutedEvents;

namespace Adamantium.UI.Controls.Text;

/// <summary>The editing core of text input controls: buffer, caret, selection, keyboard, clipboard and scroll. The
/// template supplies a <see cref="TextPresenter"/> named <c>PART_TextPresenter</c>.</summary>
public abstract class TextBoxBase : Control
{
    // --- Text + editing state ------------------------------------------------------------------------------------

    public static readonly AdamantiumProperty TextProperty = AdamantiumProperty.Register(nameof(Text),
        typeof(string), typeof(TextBoxBase),
        new PropertyMetadata(string.Empty,
            PropertyMetadataOptions.AffectsMeasure | PropertyMetadataOptions.BindsTwoWayByDefault, OnTextChanged));

    public static readonly AdamantiumProperty CaretIndexProperty = AdamantiumProperty.Register(nameof(CaretIndex),
        typeof(int), typeof(TextBoxBase), new PropertyMetadata(0, OnCaretOrSelectionChanged));

    public static readonly AdamantiumProperty SelectionStartProperty = AdamantiumProperty.Register(nameof(SelectionStart),
        typeof(int), typeof(TextBoxBase), new PropertyMetadata(0, OnCaretOrSelectionChanged));

    public static readonly AdamantiumProperty SelectionLengthProperty = AdamantiumProperty.Register(nameof(SelectionLength),
        typeof(int), typeof(TextBoxBase), new PropertyMetadata(0, OnCaretOrSelectionChanged));

    public static readonly AdamantiumProperty IsReadOnlyProperty = AdamantiumProperty.Register(nameof(IsReadOnly),
        typeof(bool), typeof(TextBoxBase), new PropertyMetadata(false));

    public static readonly AdamantiumProperty MaxLengthProperty = AdamantiumProperty.Register(nameof(MaxLength),
        typeof(int), typeof(TextBoxBase), new PropertyMetadata(0));   // 0 = unlimited

    public static readonly AdamantiumProperty TextWrappingProperty = AdamantiumProperty.Register(nameof(TextWrapping),
        typeof(TextWrapping), typeof(TextBoxBase),
        new PropertyMetadata(TextWrapping.NoWrap, PropertyMetadataOptions.AffectsMeasure, OnLayoutAffectingChanged));

    public static readonly AdamantiumProperty HorizontalTextAlignmentProperty = AdamantiumProperty.Register(
        nameof(HorizontalTextAlignment), typeof(HorizontalTextAlignment), typeof(TextBoxBase),
        new PropertyMetadata(HorizontalTextAlignment.Left, PropertyMetadataOptions.AffectsMeasure, OnLayoutAffectingChanged));

    public static readonly AdamantiumProperty PlaceholderProperty = AdamantiumProperty.Register(nameof(Placeholder),
        typeof(string), typeof(TextBoxBase), new PropertyMetadata(null, PropertyMetadataOptions.AffectsRender));

    public static readonly AdamantiumProperty FloatingPlaceholderProperty = AdamantiumProperty.Register(nameof(FloatingPlaceholder),
        typeof(bool), typeof(TextBoxBase),
        new PropertyMetadata(false, PropertyMetadataOptions.AffectsMeasure, OnLayoutAffectingChanged));

    // --- Presentation (Background + Foreground are inherited from Control) ----------------------------------------

    public static readonly AdamantiumProperty PlaceholderForegroundProperty = AdamantiumProperty.Register(nameof(PlaceholderForeground),
        typeof(Brush), typeof(TextBoxBase), new PropertyMetadata(Brushes.Gray, PropertyMetadataOptions.AffectsRender));

    public static readonly AdamantiumProperty FloatingPlaceholderForegroundProperty = AdamantiumProperty.Register(nameof(FloatingPlaceholderForeground),
        typeof(Brush), typeof(TextBoxBase), new PropertyMetadata(null, PropertyMetadataOptions.AffectsRender));

    public static readonly AdamantiumProperty CaretBrushProperty = AdamantiumProperty.Register(nameof(CaretBrush),
        typeof(Brush), typeof(TextBoxBase), new PropertyMetadata(Brushes.White, PropertyMetadataOptions.AffectsRender));

    public static readonly AdamantiumProperty SelectionBrushProperty = AdamantiumProperty.Register(nameof(SelectionBrush),
        typeof(Brush), typeof(TextBoxBase), new PropertyMetadata(null, PropertyMetadataOptions.AffectsRender));

    static TextBoxBase()
    {
        FontFamilyProperty.OverrideMetadata(typeof(TextBoxBase),
            new PropertyMetadata(null, PropertyMetadataOptions.Inherits, (a, _) => (a as TextBoxBase)?.OnFontChanged()));
        // An editor is a keyboard-focus target - opt in (the base default is now false). Its inner TextPresenter stays
        // non-focusable, so the focus walk from a click on the surface lands here, on the TextBox.
        FocusableProperty.OverrideMetadata(typeof(TextBoxBase), new PropertyMetadata(true));

        // The chrome properties are Control's; an editor only differs in what it defaults them TO - a framed box with
        // room around its text, rather than Control's bare zeroes.
        BorderThicknessProperty.OverrideMetadata(typeof(TextBoxBase),
            new PropertyMetadata(new Thickness(1), PropertyMetadataOptions.AffectsMeasure));
        PaddingProperty.OverrideMetadata(typeof(TextBoxBase),
            new PropertyMetadata(new Thickness(8, 4, 8, 4), PropertyMetadataOptions.AffectsMeasure));

        // Scroll-bar policy uses the shared ScrollViewer.* ATTACHED properties (no per-control duplicates). An editor
        // defaults to Hidden on BOTH axes - a single-line box scrolls its caret into view WITHOUT ever showing a bar - and
        // its OnScrollSettingChanged callback re-forwards the policy to PART_ContentHost (see PushScrollSettings, which also
        // forces the horizontal axis to Disabled while the text wraps). ListBox/etc. keep the base Disabled/Auto defaults.
        ScrollViewer.HorizontalScrollBarVisibilityProperty.OverrideMetadata(typeof(TextBoxBase),
            new PropertyMetadata(ScrollBarVisibility.Hidden, OnScrollSettingChanged));
        ScrollViewer.VerticalScrollBarVisibilityProperty.OverrideMetadata(typeof(TextBoxBase),
            new PropertyMetadata(ScrollBarVisibility.Hidden, OnScrollSettingChanged));
    }

    protected TextBoxBase()
    {
        AddHandler(Keyboard.TextInputEvent, new TextInputEventHandler(OnTextInputHandler));
    }

    public string Text
    {
        get => GetValue<string>(TextProperty);
        set => SetValue(TextProperty, value);
    }

    /// <summary>Caret position as a character index in <see cref="Text"/> (0..Length). It is the moving end of the selection.</summary>
    public int CaretIndex
    {
        get => GetValue<int>(CaretIndexProperty);
        set => SetValue(CaretIndexProperty, value);
    }

    public int SelectionStart
    {
        get => GetValue<int>(SelectionStartProperty);
        set => SetValue(SelectionStartProperty, value);
    }

    public int SelectionLength
    {
        get => GetValue<int>(SelectionLengthProperty);
        set => SetValue(SelectionLengthProperty, value);
    }

    public bool IsReadOnly
    {
        get => GetValue<bool>(IsReadOnlyProperty);
        set => SetValue(IsReadOnlyProperty, value);
    }

    /// <summary>Maximum number of characters; 0 (default) = unlimited.</summary>
    public int MaxLength
    {
        get => GetValue<int>(MaxLengthProperty);
        set => SetValue(MaxLengthProperty, value);
    }

    /// <summary>
    /// Soft wrapping of long lines at the viewport width. <see cref="TextWrapping.NoWrap"/> (default) keeps each line
    /// on one row (it scrolls horizontally to follow the caret); any wrapping value lays overflowing text onto extra
    /// rows. Independent of hard newlines - a control can accept <c>\n</c> (see <see cref="AcceptsNewLines"/>) with or
    /// without soft wrapping.
    /// </summary>
    public TextWrapping TextWrapping
    {
        get => GetValue<TextWrapping>(TextWrappingProperty);
        set => SetValue(TextWrappingProperty, value);
    }

    /// <summary>How wrapped lines stand in the field: at their start (the default), centered, right or justified.
    /// Text that does not wrap stays at its start.</summary>
    public HorizontalTextAlignment HorizontalTextAlignment
    {
        get => GetValue<HorizontalTextAlignment>(HorizontalTextAlignmentProperty);
        set => SetValue(HorizontalTextAlignmentProperty, value);
    }

    /// <summary>Scroll-bar policy on the horizontal axis (the shared <see cref="ScrollViewer.HorizontalScrollBarVisibilityProperty"/>
    /// attached property, forwarded to the internal ScrollViewer). Ignored while the box
    /// wraps - a wrapping box never scrolls horizontally.</summary>
    public ScrollBarVisibility HorizontalScrollBarVisibility
    {
        get => ScrollViewer.GetHorizontalScrollBarVisibility(this);
        set => ScrollViewer.SetHorizontalScrollBarVisibility(this, value);
    }

    /// <summary>Scroll-bar policy on the vertical axis (the shared <see cref="ScrollViewer.VerticalScrollBarVisibilityProperty"/>
    /// attached property, forwarded to the internal ScrollViewer). Use <c>Auto</c> for a multi-line box so a bar appears
    /// once the text outgrows the height.</summary>
    public ScrollBarVisibility VerticalScrollBarVisibility
    {
        get => ScrollViewer.GetVerticalScrollBarVisibility(this);
        set => ScrollViewer.SetVerticalScrollBarVisibility(this, value);
    }

    /// <summary>Prompt text shown (in <see cref="PlaceholderForeground"/>) while the control is empty and unfocused.</summary>
    public string Placeholder
    {
        get => GetValue<string>(PlaceholderProperty);
        set => SetValue(PlaceholderProperty, value);
    }

    public Brush PlaceholderForeground
    {
        get => GetValue<Brush>(PlaceholderForegroundProperty);
        set => SetValue(PlaceholderForegroundProperty, value);
    }

    /// <summary>When true, the <see cref="Placeholder"/> stays visible while typing: it animates up into a reserved strip
    /// above the text and shrinks (the Material / MahApps floating-label effect). When false (default) the placeholder is
    /// the classic in-line prompt shown only while empty and unfocused.</summary>
    public bool FloatingPlaceholder
    {
        get => GetValue<bool>(FloatingPlaceholderProperty);
        set => SetValue(FloatingPlaceholderProperty, value);
    }

    /// <summary>Color of the floated (raised) label - typically an accent. The label's color blends from
    /// <see cref="PlaceholderForeground"/> (resting) to this as it floats up. Falls back to PlaceholderForeground when null.</summary>
    public Brush FloatingPlaceholderForeground
    {
        get => GetValue<Brush>(FloatingPlaceholderForegroundProperty);
        set => SetValue(FloatingPlaceholderForegroundProperty, value);
    }

    public Brush CaretBrush
    {
        get => GetValue<Brush>(CaretBrushProperty);
        set => SetValue(CaretBrushProperty, value);
    }

    /// <summary>Highlight behind the selected text. Null falls back to a translucent default.</summary>
    public Brush SelectionBrush
    {
        get => GetValue<Brush>(SelectionBrushProperty);
        set => SetValue(SelectionBrushProperty, value);
    }

    // --- Template wiring -----------------------------------------------------------------------------------------

    private TextPresenter _presenter;
    private IMeasurableComponent _border;   // PART_Border - between the box and the presenter; must be re-measured too
    private ScrollViewer _scrollViewer;      // PART_ContentHost - owns the scroll offset + bars around the presenter

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        if (_presenter != null) _presenter.Owner = null;
        _presenter = GetTemplateChild("PART_TextPresenter") as TextPresenter;
        _border = GetTemplateChild("PART_Border") as IMeasurableComponent;
        _scrollViewer = GetTemplateChild("PART_ContentHost") as ScrollViewer;
        if (_presenter != null) _presenter.Owner = this;
        PushScrollSettings();
        InvalidateSurface(measure: true);
    }

    // The presenter renders our text; changes to our state must re-render (and text changes re-measure) THAT element. On
    // a measure change we also invalidate the intervening Border: it is measure-valid and would otherwise GATE, so the
    // box would re-measure against the Border's cached (e.g. strip-less) desired and never see the presenter's new size.
    private void InvalidateSurface(bool measure = false)
    {
        if (_presenter == null) return;
        if (measure)
        {
            _presenter.InvalidateMeasure();
            _border?.InvalidateMeasure();
            InvalidateMeasure();
        }
        _presenter.InvalidateRender(false);
    }

    // --- Text layout (mirrors TextBlock's cached shaping) --------------------------------------------------------

    private TextLayout _textLayout;
    private IFont _layoutFont;
    private IFont _placeholderFont;
    private double _textWidth;                // widest line's ink width (horizontal scroll bound in NoWrap)
    private double _lineHeight;
    private double _lineLeading;
    private double[] _lineTops = [0];
    private double[] _lineHeights = [0];

    // Caret model, rebuilt on every (re)shape: for each of the TextLength+1 slots (slot i = before character i) its
    // text-local X, visual line and the width of the character starting there, from the layout's caret stops.
    private double[] _caretX = [0];
    private int[] _caretLine = [0];
    private double[] _caretWidth = [0];
    private int _caretHeldAt = -1;
    private bool _bidi;
    private CaretPosition _selectionAnchor;
    private IReadOnlyList<(int Start, int End)> _visualRanges;
    private (string Text, int Start, int Length) _visualFor;
    private int _lineCount = 1;

    private string _lastShapedText;
    private double _lastShapedFontSize = -1;
    private double _lastShapedWidth = double.NaN;
    private TextWrapping _lastShapedWrapping = TextWrapping.NoWrap;
    private HorizontalTextAlignment _lastShapedAlignment;
    private SpacingRange _lastShapedWordSpacing;
    private SpacingRange _lastShapedLetterSpacing;
    private double _lastShapedTracking;
    private SpacingRange _lastShapedGlyphScaling;
    private bool _lastShapedAlternates;
    private HorizontalTextAlignment _lastShapedLastLine;
    private HorizontalTextAlignment _lastShapedSingleWord;
    private bool _lastShapedKashidas;
    private double _lastShapedLineHeight = double.NaN;
    private LineStackingStrategy _lastShapedLineStacking;
    private double _lastShapedLineSpacing;
    private TextDirection _lastShapedDirection;
    private Hyphens _lastShapedHyphens;
    private LineBreaking _lastShapedLineBreaking;
    private TabStopList _lastShapedTabStops;
    private bool _lastShapedOpticalMargins;
    private TextAttributes _lastShapedShaping;
    private double _wrapWidth = double.PositiveInfinity;   // live viewport width for soft wrap; set by measure/render

    private const double CaretWidth = 1.0;
    private const double BlinkSeconds = 0.53;
    private const double CaretPadding = 2.0;   // keep the caret this far from the viewport edge when auto-scrolling

    private bool _caretVisible = true;
    private bool _caretSuppressed;   // an owner has taken the press for something that is not typing - see SuppressCaret
    private bool _blinking;
    private double _blinkAccum;
    private double _textOy;                     // vertical offset of the text within the surface (float strip + single-line centering)
    private double _textOx;                     // horizontal offset: a right-to-left paragraph starts on the right edge
    private double? _desiredColumnX;            // sticky X for Up/Down navigation (WPF behavior); cleared by any horizontal move

    private double _floatProgress;             // floating placeholder: 0 = resting (in text), 1 = floated (small, top strip)
    private bool _floatAnimating;
    private bool _floatLoaded;
    private const double FloatScale = 0.75;    // floated label font size = FloatScale * FontSize
    private const double FloatSeconds = 0.14;  // float in/out animation duration

    // Undo / redo: snapshots of (text + caret + selection) taken BEFORE each mutation. Consecutive typed characters
    // coalesce into one entry (a "typing run"); deletes, paste, newline and every caret move break the run so each is
    // its own undo step. `_restoring` guards the stacks while a snapshot is being re-applied.
    private readonly record struct TextState(string Text, int Caret, int SelStart, int SelLength);
    private readonly List<TextState> _undo = [];
    private readonly List<TextState> _redo = [];
    private bool _typingRun;
    private bool _restoring;
    private const int UndoLimit = 500;

    /// <summary>Lays the text and the placeholder out again in the fonts that arrived.</summary>
    protected internal override void OnFontsArrived()
    {
        _placeholderShaped = null;
        OnFontChanged();
    }

    private void OnFontChanged()
    {
        _lastShapedText = null;
        InvalidateMeasure();
        InvalidateSurface(measure: true);
    }

    private void EnsureLayout()
    {
        var loadsSeen = TypefaceStore.LoadedCount;
        var fontReady = TryResolveFont(FontFamily ?? DefaultFontFamily, out var font);
        if (_textLayout == null || !ReferenceEquals(_layoutFont, font))
        {
            _textLayout = new TextLayout(font.Typeface, font) { EmitNewlineCarets = true };
            _layoutFont = font;
            _lastShapedText = null;
        }

        _textLayout.LoadFontsInBackground = !FontAtlasStore.SynchronousFill;
        _textLayout.Direction = TextDirection;
        _textLayout.Hyphens = Hyphens;
        _textLayout.LineBreaking = LineBreaking;
        _textLayout.TabStops = TabStops;
        _textLayout.OpticalMarginAlignment = OpticalMarginAlignment;
        _textLayout.WordSpacing = WordSpacing;
        _textLayout.LetterSpacing = LetterSpacing;
        _textLayout.Tracking = Tracking;
        _textLayout.GlyphScaling = GlyphScaling;
        _textLayout.JustificationAlternates = JustificationAlternates;
        _textLayout.LastLineAlignment = LastLineAlignment;
        _textLayout.SingleWordJustification = SingleWordJustification;
        _textLayout.Kashidas = Kashidas;
        _textLayout.LineHeight = LineHeight;
        _textLayout.LineStacking = LineStackingStrategy;
        _textLayout.LineSpacing = LineSpacing;

        // The height of a line of this font, as ProcessText advances it: what an empty field's one line is.
        var iFont = _textLayout.Font;
        var naturalHeight = (iFont.LineAscent + iFont.LineDescent + iFont.LineGap) * (FontSize / iFont.UnitsPerEm);
        _lineHeight = LineHeight > 0 && double.IsFinite(LineHeight) ? LineHeight : naturalHeight;
        _lineLeading = (_lineHeight - naturalHeight) / 2;

        var text = Text ?? string.Empty;
        var wrapping = TextWrapping;
        // NaN width => ProcessText treats the axis as unbounded and returns the real ink extent (its explicit
        // "unbounded" sentinel). Soft wrap passes the live viewport width so lines break there.
        var width = wrapping == TextWrapping.NoWrap || _wrapWidth <= 0 || double.IsInfinity(_wrapWidth)
            ? double.NaN
            : _wrapWidth;

        var shaping = TextShaping(font);
        if (_lastShapedText == text && _lastShapedFontSize.Equals(FontSize)
            && _lastShapedWrapping == wrapping && _lastShapedWidth.Equals(width)
            && _lastShapedAlignment == HorizontalTextAlignment && _lastShapedWordSpacing.Equals(WordSpacing)
            && _lastShapedLetterSpacing.Equals(LetterSpacing) && _lastShapedTracking.Equals(Tracking)
            && _lastShapedGlyphScaling.Equals(GlyphScaling) && _lastShapedAlternates == JustificationAlternates
            && _lastShapedLastLine == LastLineAlignment && _lastShapedSingleWord == SingleWordJustification
            && _lastShapedKashidas == Kashidas && _lastShapedLineHeight.Equals(LineHeight)
            && _lastShapedLineStacking == LineStackingStrategy && _lastShapedLineSpacing.Equals(LineSpacing)
            && _lastShapedDirection == TextDirection && _lastShapedHyphens == Hyphens
            && _lastShapedLineBreaking == LineBreaking && Equals(_lastShapedTabStops, TabStops)
            && _lastShapedOpticalMargins == OpticalMarginAlignment && ShapesLike(_lastShapedShaping, shaping))
        {
            return;
        }

        if (text.Length == 0)
        {
            _textWidth = 0;
            _caretX = [AlignedStart(double.IsNaN(width) ? 0 : width, TextDirection == TextDirection.RightToLeft)];
            _caretLine = [0];
            _caretWidth = [0];
            _bidi = false;
            _lineCount = 1;
            _lineTops = [0];
            _lineHeights = [_lineHeight];
        }
        else
        {
            var size = shaping == null
                ? _textLayout.ProcessText(text, FontSize,
                    new Size(width, double.NaN),
                    wrapping, TextTrimming.None,
                    LaidOutAlignment, VerticalTextAlignment.Top)
                : _textLayout.ProcessText(new AttributedText(text, shaping), FontSize,
                    new Size(width, double.NaN),
                    wrapping, TextTrimming.None,
                    LaidOutAlignment, VerticalTextAlignment.Top);
            _textWidth = size.Width;
            BuildCaretModel();
        }

        if (!fontReady || (text.Length > 0 && _textLayout.HasPendingFonts))
        {
            WaitForFonts(loadsSeen);
        }

        _lastShapedText = text;
        _lastShapedFontSize = FontSize;
        _lastShapedWrapping = wrapping;
        _lastShapedAlignment = HorizontalTextAlignment;
        _lastShapedWordSpacing = WordSpacing;
        _lastShapedLetterSpacing = LetterSpacing;
        _lastShapedTracking = Tracking;
        _lastShapedGlyphScaling = GlyphScaling;
        _lastShapedAlternates = JustificationAlternates;
        _lastShapedLastLine = LastLineAlignment;
        _lastShapedSingleWord = SingleWordJustification;
        _lastShapedKashidas = Kashidas;
        _lastShapedLineHeight = LineHeight;
        _lastShapedLineStacking = LineStackingStrategy;
        _lastShapedLineSpacing = LineSpacing;
        _lastShapedWidth = width;
        _lastShapedShaping = shaping;
        _lastShapedDirection = TextDirection;
        _lastShapedHyphens = Hyphens;
        _lastShapedLineBreaking = LineBreaking;
        _lastShapedTabStops = TabStops;
        _lastShapedOpticalMargins = OpticalMarginAlignment;
    }

    private void BuildCaretModel()
    {
        var stops = _textLayout.GetCaretStops();
        _caretX = new double[stops.Length];
        _caretLine = new int[stops.Length];
        _caretWidth = new double[stops.Length];

        var maxLine = 0;
        _bidi = false;
        for (var i = 0; i < stops.Length; i++)
        {
            _caretX[i] = stops[i].X;
            _caretLine[i] = stops[i].LineIndex;
            _caretWidth[i] = stops[i].Width;
            _bidi |= stops[i].IsRightToLeft;
            maxLine = Math.Max(maxLine, stops[i].LineIndex);
        }

        _lineCount = maxLine + 1;
        _lineTops = new double[_lineCount];
        _lineHeights = new double[_lineCount];
        for (var line = 0; line < _lineCount; line++)
        {
            var metrics = _textLayout.GetLine(line);
            _lineTops[line] = metrics.Top;
            _lineHeights[line] = metrics.Height;
            _bidi |= _textLayout.IsRightToLeftParagraph(metrics.Start);
        }
    }

    private int LineAt(double y)
    {
        for (var line = 0; line < _lineCount - 1; line++)
        {
            if (y < _lineTops[line] + _lineHeights[line])
            {
                return line;
            }
        }

        return _lineCount - 1;
    }

    // --- Caret / selection geometry ------------------------------------------------------------------------------

    private int MaxLineIndex => _lineCount - 1;
    private double ContentHeight => _lineTops[_lineCount - 1] + _lineHeights[_lineCount - 1];

    // Text-local caret rect before character index: the whole height of its line, as the selection covers it, on whole
    // pixels.
    internal Rect CaretRect(int index)
    {
        EnsureLayout();
        var (x, line) = CaretPoint(Math.Clamp(index, 0, _caretX.Length - 1));
        var top = Math.Round(_lineTops[line]);
        return new Rect(x, top, CaretWidth, Math.Round(_lineTops[line] + _lineHeights[line]) - top);
    }

    private (double X, int Line) CaretPoint(int index) => _bidi
        ? _textLayout.GetCaretPoint(new CaretPosition(index, index == _caretHeldAt))
        : (_caretX[index], _caretLine[index]);

    private int CaretLineOf(int index)
    {
        EnsureLayout();
        return CaretPoint(Math.Clamp(index, 0, _caretLine.Length - 1)).Line;
    }

    // First / last caret slot index on a given visual line (slots for a line are a contiguous run, text order).
    private (int first, int last) LineSlotRange(int line)
    {
        int first = -1, last = -1;
        for (var i = 0; i < _caretLine.Length; i++)
        {
            if (_caretLine[i] != line) { if (first >= 0) break; continue; }
            if (first < 0) first = i;
            last = i;
        }
        return first < 0 ? (0, _caretX.Length - 1) : (first, last);
    }

    // Nearest caret index to a text-local point: pick the visual line by Y, then the slot on it nearest X (splitting on
    // glyph mid-points like a text cursor). Empty lines are handled because their single caret slot carries the line.
    private int IndexFromPoint(double x, double y) => HitFromPoint(x, y).Index;

    private (int Index, bool After) HitFromPoint(double x, double y)
    {
        EnsureLayout();
        if (_caretX.Length == 1) return (0, false);

        var hit = _textLayout.HitTest(x, y);
        var index = SnapToCaretStop(Clamp(hit.CaretIndex));
        return (index, hit.IsTrailing && index == hit.CaretIndex);
    }

    private int TextLength => (Text ?? string.Empty).Length;

    private bool HasSelection => SelectionLength > 0;

    private int Clamp(int index) => Math.Clamp(index, 0, TextLength);

    private (int start, int end) SelectionRange()
    {
        var s = Clamp(SelectionStart);
        var e = Clamp(SelectionStart + SelectionLength);
        return s <= e ? (s, e) : (e, s);
    }

    // --- Editing operations --------------------------------------------------------------------------------------

    private void MoveCaretTo(int newIndex, bool extend, bool keepColumn = false, bool afterPrevious = false)
    {
        newIndex = Clamp(newIndex);
        var focus = new CaretPosition(newIndex, afterPrevious);
        if (!keepColumn) _desiredColumnX = null;   // any non-vertical move drops the sticky Up/Down column
        _typingRun = false;                        // a caret move ends the current typing run (its own undo step)
        if (extend && _bidi)
        {
            SelectVisually(focus);
        }
        else if (extend)
        {
            var anchor = SelectionLength == 0 ? CaretIndex : SelectionAnchor();
            SetSelection(anchor, newIndex);
        }
        else
        {
            _visualRanges = null;
            SelectionStart = newIndex;
            SelectionLength = 0;
        }
        _caretHeldAt = afterPrevious ? newIndex : -1;
        CaretIndex = newIndex;
        ResetBlink();
        ScrollCaretIntoView();
    }

    // Move the caret one visual line up/down, preserving the sticky column X (WPF behavior). Past the first/last line
    // it snaps to the document start/end.
    private void MoveCaretVertical(int dir, bool extend)
    {
        EnsureLayout();
        var target = CaretLineOf(CaretIndex) + dir;
        if (target < 0) { MoveCaretTo(0, extend); return; }
        if (target > MaxLineIndex) { MoveCaretTo(TextLength, extend); return; }

        var col = _desiredColumnX ?? CaretRect(CaretIndex).X;
        var (idx, after) = HitFromPoint(col, _lineTops[target] + _lineHeights[target] / 2.0);
        MoveCaretTo(idx, extend, keepColumn: true, afterPrevious: after);
        _desiredColumnX = col;
    }

    // Start / end of the caret's current VISUAL line (soft-wrapped rows count as separate lines, like WPF).
    private int CurrentLineStart() { var (f, _) = LineSlotRange(CaretLineOf(CaretIndex)); return f; }
    private int CurrentLineEnd() { var (_, l) = LineSlotRange(CaretLineOf(CaretIndex)); return l; }

    private int SelectionAnchor()
    {
        var (s, e) = SelectionRange();
        return CaretIndex == e ? s : e;
    }

    private void SetSelection(int anchor, int caret)
    {
        SelectionStart = Math.Min(anchor, caret);
        SelectionLength = Math.Abs(caret - anchor);
    }

    private void SelectVisually(CaretPosition focus)
    {
        EnsureLayout();
        var anchor = VisualSelectionIsCurrent() ? _selectionAnchor
            : SelectionLength == 0 ? new CaretPosition(CaretIndex, _caretHeldAt == CaretIndex)
            : new CaretPosition(SelectionAnchor());
        var ranges = _textLayout.GetVisualRanges(anchor, focus);
        var start = ranges.Count > 0 ? ranges[0].Start : focus.Index;
        var length = ranges.Count > 0 ? ranges[^1].End - start : 0;
        _selectionAnchor = anchor;
        _visualRanges = ranges;
        _visualFor = (Text, start, length);
        SelectionStart = start;
        SelectionLength = length;
    }

    private bool VisualSelectionIsCurrent() =>
        _visualRanges != null && _visualFor.Text == Text
        && _visualFor.Start == SelectionStart && _visualFor.Length == SelectionLength;

    private IReadOnlyList<(int Start, int End)> SelectedRanges() =>
        HasSelection && VisualSelectionIsCurrent() ? _visualRanges : [SelectionRange()];

    private void MoveVisually(bool toRight, bool extend)
    {
        EnsureLayout();
        var to = _textLayout.MoveVisually(new CaretPosition(CaretIndex, _caretHeldAt == CaretIndex), toRight);
        MoveCaretTo(to.Index, extend, afterPrevious: to.AfterPrevious);
    }

    private void MoveToLineEdge(int line, bool start, bool extend)
    {
        EnsureLayout();
        var to = start ? _textLayout.GetLineStart(line) : _textLayout.GetLineEnd(line);
        MoveCaretTo(to.Index, extend, afterPrevious: to.AfterPrevious);
    }

    public void SelectAll()
    {
        _visualRanges = null;
        _caretHeldAt = -1;
        SelectionStart = 0;
        SelectionLength = TextLength;
        CaretIndex = TextLength;
    }

    /// <summary>Replace the selection (or insert at the caret when there is none) with <paramref name="insert"/>.
    /// <paramref name="isTyping"/> lets consecutive single characters coalesce into one undo step.</summary>
    protected void ReplaceSelection(string insert, bool isTyping = false)
    {
        if (IsReadOnly) return;
        insert ??= string.Empty;

        var text = Text ?? string.Empty;
        var ranges = HasSelection ? SelectedRanges() : [(Clamp(CaretIndex), Clamp(CaretIndex))];
        var start = ranges[0].Start;
        var removed = ranges.Sum(r => r.End - r.Start);

        if (MaxLength > 0)
        {
            var room = MaxLength - (text.Length - removed);
            if (room <= 0 && insert.Length > 0) insert = string.Empty;
            else if (insert.Length > room)
            {
                var keep = Math.Max(0, room);
                if (keep > 0 && char.IsHighSurrogate(insert[keep - 1])) keep--;
                insert = insert.Substring(0, keep);
            }
        }

        var built = new StringBuilder(text.Length - removed + insert.Length);
        var at = 0;
        for (var k = 0; k < ranges.Count; k++)
        {
            built.Append(text, at, ranges[k].Start - at);
            if (k == 0) built.Append(insert);
            at = ranges[k].End;
        }
        built.Append(text, at, text.Length - at);
        var newText = built.ToString();
        if (newText == text) return;   // nothing actually changed -> no edit, no undo entry

        RecordUndo(isTyping);
        _visualRanges = null;

        // SetCurrentValue, NOT `Text = ` (which is a Local-priority set): an edit must write in the BINDING's own slot so a
        // two-way {Binding}/{Ancestor} fires its write-back AND later source->target updates still apply. A plain Local set
        // MASKS the binding - the box then "lives on its own", never reflecting a later source change (a ColorPicker field
        // stuck after you typed in it). Mirrors Slider.SetValueFromInput.
        SetCurrentValue(TextProperty, newText);
        var newCaret = start + insert.Length;
        _caretHeldAt = insert.Length > 0 ? newCaret : -1;
        SelectionStart = newCaret;
        SelectionLength = 0;
        CaretIndex = newCaret;
        ResetBlink();
        ScrollCaretIntoView();
    }

    // --- Undo / redo ---------------------------------------------------------------------------------------------

    private TextState Snapshot() => SelectedRanges().Count > 1
        ? new(Text ?? string.Empty, CaretIndex, CaretIndex, 0)
        : new(Text ?? string.Empty, CaretIndex, SelectionStart, SelectionLength);

    // Capture the PRE-edit state. A typing run keeps only its first snapshot (so undo removes the whole run at once);
    // any non-typing edit, or the first char after a caret move, starts a fresh entry. Every edit clears the redo stack.
    private void RecordUndo(bool isTyping)
    {
        if (_restoring) return;
        _redo.Clear();
        if (isTyping && _typingRun) return;
        _undo.Add(Snapshot());
        if (_undo.Count > UndoLimit) _undo.RemoveAt(0);
        _typingRun = isTyping;
    }

    public void Undo()
    {
        if (IsReadOnly || _undo.Count == 0) return;
        _redo.Add(Snapshot());
        var s = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        RestoreState(s);
    }

    public void Redo()
    {
        if (IsReadOnly || _redo.Count == 0) return;
        _undo.Add(Snapshot());
        var s = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);
        RestoreState(s);
    }

    private void RestoreState(TextState s)
    {
        _restoring = true;
        _typingRun = false;
        _caretHeldAt = -1;
        _visualRanges = null;
        SetCurrentValue(TextProperty, s.Text);   // binding-slot write (see ReplaceSelection), so undo doesn't mask a two-way binding either
        var len = TextLength;
        SelectionStart = Math.Clamp(s.SelStart, 0, len);
        SelectionLength = Math.Clamp(s.SelLength, 0, len - Math.Clamp(s.SelStart, 0, len));
        CaretIndex = Math.Clamp(s.Caret, 0, len);
        _restoring = false;
        ResetBlink();
    }

    private void DeleteBackward()
    {
        if (IsReadOnly) return;
        if (HasSelection) { ReplaceSelection(string.Empty); return; }
        if (CaretIndex <= 0) return;
        _visualRanges = null;
        SelectionStart = PreviousCaretStop(CaretIndex);
        SelectionLength = CaretIndex - SelectionStart;
        ReplaceSelection(string.Empty);
        _caretHeldAt = CaretIndex;
    }

    private void DeleteForward()
    {
        if (IsReadOnly) return;
        if (HasSelection) { ReplaceSelection(string.Empty); return; }
        if (CaretIndex >= TextLength) return;
        var held = _caretHeldAt;
        _visualRanges = null;
        SelectionStart = CaretIndex;
        SelectionLength = NextCaretStop(CaretIndex) - CaretIndex;
        ReplaceSelection(string.Empty);
        _caretHeldAt = held;
    }

    private int NextCaretStop(int index)
    {
        EnsureLayout();
        return TextLength == 0 ? 0 : _textLayout.NextCaretStop(index);
    }

    private int PreviousCaretStop(int index)
    {
        EnsureLayout();
        return TextLength == 0 ? 0 : _textLayout.PreviousCaretStop(index);
    }

    private int SnapToCaretStop(int index)
    {
        if (index <= 0 || index >= TextLength)
        {
            return Clamp(index);
        }

        return PreviousCaretStop(NextCaretStop(index));
    }

    private int WordBoundary(int index, int dir)
    {
        var text = Text ?? string.Empty;
        var i = index;
        if (dir < 0)
        {
            while (i > 0 && char.IsWhiteSpace(text[i - 1])) i--;
            while (i > 0 && !char.IsWhiteSpace(text[i - 1])) i--;
        }
        else
        {
            while (i < text.Length && char.IsWhiteSpace(text[i])) i++;
            while (i < text.Length && !char.IsWhiteSpace(text[i])) i++;
        }
        return i;
    }

    // --- Input: keyboard + characters ----------------------------------------------------------------------------

    private void OnTextInputHandler(object sender, TextInputEventArgs e)
    {
        if (IsReadOnly || string.IsNullOrEmpty(e.Text)) return;
        var filtered = new string(e.Text.Where(c => !char.IsControl(c)).ToArray());
        if (filtered.Length == 0) return;
        ReplaceSelection(filtered, isTyping: true);
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        var ctrl = (Keyboard.Modifiers & (InputModifiers.LeftControl | InputModifiers.RightControl)) != 0;
        var shift = (Keyboard.Modifiers & (InputModifiers.LeftShift | InputModifiers.RightShift)) != 0;

        switch (e.Key)
        {
            case Key.LeftArrow when _bidi && !ctrl:
                MoveVisually(toRight: false, shift);
                e.Handled = true;
                break;
            case Key.RightArrow when _bidi && !ctrl:
                MoveVisually(toRight: true, shift);
                e.Handled = true;
                break;
            case Key.LeftArrow:
                MoveCaretTo(ctrl ? WordBoundary(CaretIndex, -1) : PreviousCaretStop(CaretIndex), shift);
                e.Handled = true;
                break;
            case Key.RightArrow:
                MoveCaretTo(ctrl ? WordBoundary(CaretIndex, +1) : NextCaretStop(CaretIndex), shift);
                e.Handled = true;
                break;
            case Key.UpArrow:
                MoveCaretVertical(-1, shift);
                e.Handled = true;
                break;
            case Key.DownArrow:
                MoveCaretVertical(+1, shift);
                e.Handled = true;
                break;
            case Key.Home when _bidi:
                MoveToLineEdge(ctrl ? 0 : CaretLineOf(CaretIndex), start: true, shift);
                e.Handled = true;
                break;
            case Key.End when _bidi:
                MoveToLineEdge(ctrl ? MaxLineIndex : CaretLineOf(CaretIndex), start: false, shift);
                e.Handled = true;
                break;
            case Key.Home:
                MoveCaretTo(ctrl ? 0 : CurrentLineStart(), shift);
                e.Handled = true;
                break;
            case Key.End:
                MoveCaretTo(ctrl ? TextLength : CurrentLineEnd(), shift);
                e.Handled = true;
                break;
            case Key.BackSpace:
                if (ctrl && !HasSelection) { SelectionStart = WordBoundary(CaretIndex, -1); SelectionLength = CaretIndex - SelectionStart; }
                DeleteBackward();
                e.Handled = true;
                break;
            case Key.Delete:
                if (ctrl && !HasSelection) { SelectionStart = CaretIndex; SelectionLength = WordBoundary(CaretIndex, +1) - CaretIndex; }
                DeleteForward();
                e.Handled = true;
                break;
            case Key.Z when ctrl && shift:   // Ctrl+Shift+Z = redo (common alias)
                Redo();
                e.Handled = true;
                break;
            case Key.Z when ctrl:
                Undo();
                e.Handled = true;
                break;
            case Key.Y when ctrl:
                Redo();
                e.Handled = true;
                break;
            case Key.A when ctrl:
                SelectAll();
                e.Handled = true;
                break;
            case Key.C when ctrl:
                CopyToClipboard();
                e.Handled = true;
                break;
            case Key.X when ctrl:
                CutToClipboard();
                e.Handled = true;
                break;
            case Key.V when ctrl:
                PasteFromClipboard();
                e.Handled = true;
                break;
            default:
                OnUnhandledKey(e);
                break;
        }
    }

    /// <summary>Keys not handled by the shared editing map (e.g. Enter) - a concrete control overrides to react.</summary>
    protected virtual void OnUnhandledKey(KeyEventArgs e) { }

    /// <summary>Whether newline characters are kept in the buffer (multi-line control). When false, pasted newlines
    /// collapse to spaces. A concrete control ties this to its own AcceptsReturn-style option.</summary>
    protected virtual bool AcceptsNewLines => false;

    protected virtual void CopyToClipboard()
    {
        if (HasSelection) Clipboard.SetText(SelectedText());
    }

    protected virtual void CutToClipboard()
    {
        if (IsReadOnly || !HasSelection) return;
        Clipboard.SetText(SelectedText());
        ReplaceSelection(string.Empty);
    }

    protected virtual void PasteFromClipboard()
    {
        if (IsReadOnly) return;
        var text = Clipboard.GetText();
        if (string.IsNullOrEmpty(text)) return;
        text = text.Replace("\r\n", "\n").Replace("\r", "\n").Replace((char)0x2028, '\n');       // normalize line endings
        if (!AcceptsNewLines) text = text.Replace("\n", " ");        // single-line: newlines become spaces
        ReplaceSelection(text);
    }

    protected string SelectedText()
    {
        var text = Text ?? string.Empty;
        return string.Concat(SelectedRanges().Select(r => text.Substring(r.Start, r.End - r.Start)));
    }

    // --- Surface callbacks (the TextPresenter measures / renders / hit-tests through these) -----------------------

    internal Size MeasureSurface(double availableWidth)
    {
        _wrapWidth = availableWidth;   // soft wrap breaks at the viewport width
        EnsureLayout();
        // Wrapped text fills the given width; unwrapped desires its widest line (+ caret). Height is all the lines plus
        // the reserved strip for a floating label (zero when that effect is off).
        var w = TextWrapping != TextWrapping.NoWrap && !double.IsInfinity(availableWidth)
            ? availableWidth
            : _textWidth + CaretWidth;
        return new Size(w, ContentHeight + FloatStripHeight());
    }

    internal void SurfaceMouseDown(double localX, double localY, bool extend)
    {
        Focus();
        var (index, after) = HitFromPoint(localX - _textOx, localY - _textOy);
        MoveCaretTo(index, extend, afterPrevious: after);
    }

    internal void SurfaceMouseMove(double localX, double localY)
    {
        var (index, after) = HitFromPoint(localX - _textOx, localY - _textOy);
        MoveCaretTo(index, extend: true, afterPrevious: after);
    }

    /// <summary>Double-click: take the word under the point (or the run of spaces, when the click lands between words -
    /// a gesture that selects nothing at all reads as a dead click).</summary>
    internal void SurfaceSelectWord(double localX, double localY)
    {
        Focus();
        SelectWordAt(IndexFromPoint(localX - _textOx, localY - _textOy));
    }

    /// <summary>Selects the word containing <paramref name="index"/> - what a double-click does, and what anything else
    /// that wants a word rather than a character can call.</summary>
    public void SelectWordAt(int index)
    {
        var (start, end) = WordAt(index);
        _visualRanges = null;
        _caretHeldAt = -1;
        SelectionStart = start;
        SelectionLength = end - start;
        CaretIndex = end;
    }

    /// <summary>Triple-click: everything.</summary>
    internal void SurfaceSelectAll()
    {
        Focus();
        SelectAll();
    }

    /// <summary>Drop a mouse selection that is still in progress - see <see cref="TextPresenter.CancelSelection"/>.</summary>
    internal void CancelMouseSelection()
    {
        _presenter?.CancelSelection();
        SelectionLength = 0;
    }

    /// <summary>Hide the caret while an owner has taken the press for something that is not typing - a NumericUpDown
    /// running its value under a drag. The box keeps focus (the keys still belong to it the moment the drag ends), but a
    /// caret blinking through the drag claims it is being edited as text, and points at a place in a number that is
    /// being replaced several times a second.</summary>
    internal void SuppressCaret(bool suppress)
    {
        if (_caretSuppressed == suppress) return;

        _caretSuppressed = suppress;
        if (suppress) InvalidateSurface();
        else ResetBlink();   // back from the drag: show it at once rather than mid-blink
    }

    // The run around an index: a word, or the whitespace between two. Not WordBoundary() twice - that one walks OVER the
    // gap to the next word, so a click at a word's first character would have selected the word BEFORE it.
    private (int Start, int End) WordAt(int index)
    {
        var text = Text ?? string.Empty;
        if (text.Length == 0) return (0, 0);

        var i = Math.Clamp(index, 0, text.Length - 1);
        var inWhitespace = char.IsWhiteSpace(text[i]);
        var start = i;
        var end = i;
        while (start > 0 && char.IsWhiteSpace(text[start - 1]) == inWhitespace) start--;
        while (end < text.Length && char.IsWhiteSpace(text[end]) == inWhitespace) end++;
        return (start, end);
    }

    internal void RenderSurface(IDrawingSession session, Size size)
    {
        EnsureLayout();

        // First time the field is actually drawn: all initial bindings have settled, so snap the floating label to its
        // resolved state (floated if it already holds text) WITHOUT animating - only later user changes animate.
        if (!_floatLoaded) { _floatLoaded = true; _floatProgress = FloatTarget(); }

        var hasText = !string.IsNullOrEmpty(Text);
        // Scrolling is owned by the enclosing ScrollViewer (its ScrollContentPresenter translates this whole surface by
        // the offset), so we render the FULL content at our own origin (ox = 0). Text sits below the floating-label strip
        // (zero when off); single-line content then centers vertically in the surface (which the ScrollContentPresenter
        // sizes to at least the viewport), multi-line is top-aligned. vOffset is 0 for the multi-line case.
        var rightToLeft = hasText ? _textLayout.IsRightToLeftParagraph(0) : TextDirection == TextDirection.RightToLeft;
        var contentWidth = TextWrapping != TextWrapping.NoWrap && !double.IsInfinity(_wrapWidth)
            ? size.Width
            : _textWidth + CaretWidth;
        var ox = rightToLeft ? Math.Floor(Math.Max(0, size.Width - contentWidth)) : 0;
        _textOx = ox;
        var stripH = FloatStripHeight();
        var textAreaH = size.Height - stripH;
        var vOffset = !AcceptsNewLines && ContentHeight < textAreaH ? (textAreaH - ContentHeight) / 2 : 0;
        // WHOLE pixels. The glyph pipeline snaps the vertical origin it is handed (it rounds the baseline and the
        // ascender line to whole rows), so a fractional offset moves the caret and the selection while leaving the text
        // where it was: centering a 15.8-tall line in a 17.4-tall surface handed the text 0.8 it silently dropped and the
        // caret 0.8 it kept, and the caret drew a visible row below the letters. One snapped origin for all three.
        var oy = Math.Floor(stripH + vOffset);
        _textOy = oy;

        // Selection highlight (behind the text) - one rect per spanned visual line.
        if (HasSelection) DrawSelection(session, ox, oy);

        // Text.
        if (hasText)
        {
            session.DrawText(BuildTextParameters(Foreground, ox, oy, size), size, _textLayout, Foreground, Brushes.Transparent, Brushes.Transparent);
        }

        // Placeholder: floating label (always visible, animates up/shrinks) or the classic in-line prompt (only while
        // empty and unfocused).
        if (!string.IsNullOrEmpty(Placeholder))
        {
            if (FloatingPlaceholder) RenderFloatingPlaceholder(session, oy, size);
            else if (!hasText && !IsFocused) RenderPlaceholder(session, oy, size);
        }

        // Caret.
        if (IsFocused && _caretVisible && !_caretSuppressed)
        {
            var c = CaretRect(CaretIndex);
            session.DrawRectangle(CaretBrush, new Rect(ox + c.X, oy + c.Y, CaretWidth, c.Height));
        }
    }

    private void DrawSelection(IDrawingSession session, double ox, double oy)
    {
        var brush = SelectionBrush ?? DefaultSelectionBrush;
        foreach (var piece in SelectedRanges().SelectMany(r => _textLayout.GetRangeRects(r.Start, r.End)))
        {
            // A sliver, so a selected empty or blank line is visible.
            var w = piece.Width > 0 ? piece.Width : piece.Height * 0.4;
            session.DrawRectangle(brush, new Rect(ox + piece.X, oy + piece.Y, w, piece.Height));
        }
    }

    // Scroll the enclosing ScrollViewer the minimum needed to keep the caret visible (WPF caret-follow). The caret rect is
    // in text-layout coords; content coords add the vertical text offset (float strip + single-line centering). Horizontal
    // is 1:1 (ox = 0). No-op until the template's ScrollViewer exists / when the caret already fits.
    private void ScrollCaretIntoView()
    {
        ScrollIndexIntoView(CaretIndex);
        ShowInputMethodAtCaret();
    }

    private void ShowInputMethodAtCaret()
    {
        if (!IsFocused || _presenter == null)
        {
            return;
        }

        var caret = CaretRect(CaretIndex);
        InputMethod.SetCaretBounds(_presenter, new Rect(caret.X + _textOx, caret.Y + _textOy, CaretWidth, caret.Height));
    }

    internal void ScrollIndexIntoView(int index)
    {
        if (_scrollViewer == null) return;
        var c = CaretRect(Clamp(index));
        _scrollViewer.BringIntoView(new Rect(c.X + _textOx, c.Y + _textOy, CaretWidth + CaretPadding, c.Height));
    }

    internal UIComponent TextSurface => _presenter;

    internal int LineOfIndex(int index) => CaretLineOf(Clamp(index));

    internal (int Start, int End) LineIndexRange(int line)
    {
        EnsureLayout();
        var (first, last) = LineSlotRange(Math.Clamp(line, 0, MaxLineIndex));
        return (first, Math.Min(last + 1, TextLength));
    }

    internal int IndexAtSurfacePoint(double x, double y) => IndexFromPoint(x - _textOx, y - _textOy);

    internal List<Rect> SurfaceRects(int start, int end)
    {
        EnsureLayout();
        (start, end) = (Clamp(Math.Min(start, end)), Clamp(Math.Max(start, end)));
        if (start == end)
        {
            var caret = CaretRect(start);
            return [new Rect(caret.X + _textOx, caret.Y + _textOy, 0, caret.Height)];
        }

        var rects = new List<Rect>();
        foreach (var piece in _textLayout.GetRangeRects(start, end))
        {
            if (piece.Width > 0)
            {
                rects.Add(new Rect(piece.X + _textOx, _textOy + piece.Y, piece.Width, piece.Height));
            }
        }

        return rects;
    }

    private TextRenderingParameters BuildTextParameters(Brush color, double originX, double originY, Size size)
    {
        return new TextRenderingParameters
        {
            HorizontalTextAlignment = HorizontalTextAlignment.Left,
            VerticalTextAlignment = VerticalTextAlignment.Top,
            TextTrimming = TextTrimming.None,
            TextWrapping = TextWrapping,
            Color = (color as SolidColorBrush)?.Color ?? Colors.White,
            TextArea = new Rectangle(new Vector2F((float)originX, (float)originY), size)
        };
    }

    private TextLayout _placeholderLayout;
    private string _placeholderShaped;
    private void EnsurePlaceholderShaped(double fontSize)
    {
        var loadsSeen = TypefaceStore.LoadedCount;
        var fontReady = TryResolveFont(FontFamily ?? DefaultFontFamily, out var font);
        if (_placeholderLayout == null || !ReferenceEquals(_placeholderFont, font))
        {
            _placeholderLayout = new TextLayout(font.Typeface, font);
            _placeholderFont = font;
            _placeholderShaped = null;
        }

        _placeholderLayout.LoadFontsInBackground = !FontAtlasStore.SynchronousFill;
        _placeholderLayout.Direction = TextDirection;
        var key = Placeholder + "|" + fontSize + "|" + TextDirection;
        if (_placeholderShaped == key) return;
        _placeholderLayout.ProcessText(Placeholder, fontSize, new Size(double.NaN, double.NaN),
            TextWrapping.NoWrap, TextTrimming.None, HorizontalTextAlignment.Left, VerticalTextAlignment.Top);
        _placeholderShaped = key;
        if (!fontReady || _placeholderLayout.HasPendingFonts)
        {
            WaitForFonts(loadsSeen);
        }
    }

    private void RenderPlaceholder(IDrawingSession session, double oy, Size size)
    {
        EnsurePlaceholderShaped(FontSize);
        session.DrawText(BuildTextParameters(PlaceholderForeground, PlaceholderX(size), oy + _lineLeading, size), size, _placeholderLayout, PlaceholderForeground, Brushes.Transparent, Brushes.Transparent);
    }

    private double PlaceholderX(Size size) => Math.Floor(AlignedStart(
        Math.Max(0, size.Width - _placeholderLayout.GetLine(0).Width), _placeholderLayout.IsRightToLeftParagraph(0)));

    private HorizontalTextAlignment LaidOutAlignment =>
        TextWrapping == TextWrapping.NoWrap ? HorizontalTextAlignment.Left : HorizontalTextAlignment;

    private double AlignedStart(double room, bool rightToLeft) =>
        (LaidOutAlignment == HorizontalTextAlignment.Justify ? LastLineAlignment : LaidOutAlignment) switch
        {
            HorizontalTextAlignment.Center => room / 2,
            HorizontalTextAlignment.Right => rightToLeft ? 0 : room,
            _ => rightToLeft ? room : 0,
        };

    // Floating label: interpolate font size (full -> shrunk), Y (text position -> top strip) and color (placeholder ->
    // accent) by _floatProgress.
    private void RenderFloatingPlaceholder(IDrawingSession session, double textOy, Size size)
    {
        var t = _floatProgress;
        var floatFs = FontSize + (FontSize * FloatScale - FontSize) * t;
        EnsurePlaceholderShaped(floatFs);
        var rest = textOy + _lineLeading;
        var y = rest + (0.0 - rest) * t;   // rest (t=0): at the text; floated (t=1): at the top of the strip
        var brush = FloatLabelBrush(t);
        session.DrawText(BuildTextParameters(brush, PlaceholderX(size), y, size), size, _placeholderLayout, brush, Brushes.Transparent, Brushes.Transparent);
    }

    // Blend the label color from the resting placeholder color to the accent (FloatingPlaceholderForeground) as it
    // rises. Blends only when both are solid; otherwise switches at the half-way point.
    private Brush FloatLabelBrush(double t)
    {
        var rest = PlaceholderForeground;
        var accent = FloatingPlaceholderForeground ?? PlaceholderForeground;
        if (t <= 0 || ReferenceEquals(rest, accent)) return rest;
        if (t >= 1) return accent;
        if (rest is SolidColorBrush a && accent is SolidColorBrush b)
        {
            byte L(byte from, byte to) => (byte)(from + (to - from) * t);
            return new SolidColorBrush(new Color(L(a.Color.R, b.Color.R), L(a.Color.G, b.Color.G), L(a.Color.B, b.Color.B), L(a.Color.A, b.Color.A)));
        }
        return t < 0.5 ? rest : accent;
    }

    // --- Focus + caret blink -------------------------------------------------------------------------------------

    protected override void OnGotFocus(RoutedEventArgs e)
    {
        base.OnGotFocus(e);
        StartBlink();
        UpdateFloatState();
        InvalidateSurface();
        ShowInputMethodAtCaret();
    }

    protected override void OnLostFocus(RoutedEventArgs e)
    {
        base.OnLostFocus(e);
        _caretVisible = false;
        UpdateFloatState();
        InvalidateSurface();
    }

    // --- Floating placeholder (Material / MahApps label) ---------------------------------------------------------

    // Height reserved above the text for the floated (shrunk) label. Zero unless the effect is on and there is a label.
    private double FloatStripHeight()
        => FloatingPlaceholder && !string.IsNullOrEmpty(Placeholder) ? Math.Ceiling(FontSize * FloatScale * 1.3) : 0;

    // The label floats up whenever the field is focused or non-empty; it returns to the text line (full size) only when
    // the field is both empty AND unfocused.
    private double FloatTarget() => IsFocused || !string.IsNullOrEmpty(Text) ? 1.0 : 0.0;

    private void UpdateFloatState()
    {
        var target = FloatTarget();
        // Before the field is first shown (initial bindings for Text/FloatingPlaceholder are still settling, in any
        // order) - or whenever the effect is off - keep the label at its RESOLVED position with no animation. Otherwise
        // (shown + effect on) a real change (focus, first char, last delete) animates. This fixes the label resting at
        // the bottom when the field loads already holding text: it must start floated, not slide up on the first click.
        if (!_floatLoaded || !FloatingPlaceholder) { _floatProgress = target; InvalidateSurface(); return; }
        if (Math.Abs(_floatProgress - target) > 1e-3) StartFloatAnim();
    }

    private void StartFloatAnim()
    {
        if (_floatAnimating) return;
        _floatAnimating = true;
        AnimationManager.AddTicker(dt =>
        {
            var target = FloatTarget();
            var step = dt / FloatSeconds;
            _floatProgress = _floatProgress < target
                ? Math.Min(target, _floatProgress + step)
                : Math.Max(target, _floatProgress - step);
            InvalidateSurface();
            if (Math.Abs(_floatProgress - target) < 1e-3) { _floatProgress = target; _floatAnimating = false; return true; }
            return false;
        });
    }

    private void StartBlink()
    {
        _caretVisible = true;
        _blinkAccum = 0;
        InvalidateSurface();
        if (_blinking) return;
        _blinking = true;
        AnimationManager.AddEndlessTicker(dt =>
        {
            if (!IsFocused) { _blinking = false; return true; }
            _blinkAccum += dt;
            if (_blinkAccum >= BlinkSeconds)
            {
                _blinkAccum -= BlinkSeconds;
                _caretVisible = !_caretVisible;
                InvalidateSurface();
            }
            return false;
        });
    }

    private void ResetBlink()
    {
        _blinkAccum = 0;
        _caretVisible = true;
        InvalidateSurface();
    }

    private static void OnTextChanged(AdamantiumComponent a, AdamantiumPropertyChangedEventArgs e)
    {
        if (a is not TextBoxBase tb) return;
        tb._lastShapedText = null;
        var len = (tb.Text ?? string.Empty).Length;
        if (tb.CaretIndex > len) tb.SetCurrentValue(CaretIndexProperty, len);
        if (tb.SelectionStart > len) tb.SetCurrentValue(SelectionStartProperty, len);
        if (tb.SelectionStart + tb.SelectionLength > len)
            tb.SetCurrentValue(SelectionLengthProperty, Math.Max(0, len - tb.SelectionStart));
        tb.UpdateFloatState();
        tb.InvalidateSurface(measure: true);
    }

    private static void OnCaretOrSelectionChanged(AdamantiumComponent a, AdamantiumPropertyChangedEventArgs e)
    {
        if (a is not TextBoxBase box) return;
        box.InvalidateSurface();
        if (AutomationEvents.IsListening && box.FindAutomationPeer() != null)
            AutomationEvents.Raise(box, AutomationEvent.TextSelectionChanged);
    }

    // A property that changes how the text lays out (e.g. wrapping, the floating-label strip): drop the shaping cache,
    // resolve the label state, and re-measure (InvalidateSurface re-measures the box + Border + presenter so the strip
    // change actually reaches the presenter - see InvalidateSurface).
    private static void OnLayoutAffectingChanged(AdamantiumComponent a, AdamantiumPropertyChangedEventArgs e)
    {
        if (a is not TextBoxBase tb) return;
        tb._lastShapedText = null;
        tb.UpdateFloatState();
        tb.PushScrollSettings();   // TextWrapping toggles whether the horizontal axis is scrollable vs. wrapping
        tb.InvalidateSurface(measure: true);
    }

    private static void OnScrollSettingChanged(AdamantiumComponent a, AdamantiumPropertyChangedEventArgs e)
        => (a as TextBoxBase)?.PushScrollSettings();

    // Forward our policy to the internal ScrollViewer. A wrapping box must NOT scroll horizontally (the content is bound
    // to the viewport width so it wraps), so we force Disabled there regardless of HorizontalScrollBarVisibility.
    private void PushScrollSettings()
    {
        if (_scrollViewer == null) return;
        _scrollViewer.HorizontalScrollBarVisibility =
            TextWrapping != TextWrapping.NoWrap ? ScrollBarVisibility.Disabled : HorizontalScrollBarVisibility;
        _scrollViewer.VerticalScrollBarVisibility = VerticalScrollBarVisibility;
    }

    private static readonly Brush DefaultSelectionBrush = new SolidColorBrush(new Color(0x33, 0x99, 0xFF, 0x66));
}
