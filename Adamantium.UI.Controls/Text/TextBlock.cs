using System.Collections.Generic;
using System.Collections.Specialized;
using System.Text;
using Adamantium.Core.Fonts;
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

public class TextBlock : InputUIComponent, IFocusableInParts, IGlyphEffectSource, ITextStyleUser,
    Adamantium.UI.Core.Resources.IResolvesOwnResources, IInkOverhang
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
    
    public static readonly AdamantiumProperty TextTransformProperty = AdamantiumProperty.Register(nameof(TextTransform),
        typeof(TextTransform), typeof(TextBlock),
        new PropertyMetadata(TextTransform.None, PropertyMetadataOptions.AffectsMeasure | PropertyMetadataOptions.AffectsRender));

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
        new PropertyMetadata(0, PropertyMetadataOptions.AffectsMeasure, TextParametersChangedCallback),
        value => value is 0 or >= DropCap.LeastLines);

    public static readonly AdamantiumProperty DropCapCharactersProperty = AdamantiumProperty.Register(nameof(DropCapCharacters),
        typeof(int), typeof(TextBlock),
        new PropertyMetadata(1, PropertyMetadataOptions.AffectsMeasure, TextParametersChangedCallback),
        value => value is >= 1);

    public static readonly AdamantiumProperty DropCapFontFamilyProperty = AdamantiumProperty.Register(nameof(DropCapFontFamily),
        typeof(FontFamily), typeof(TextBlock),
        new PropertyMetadata(null, PropertyMetadataOptions.AffectsMeasure, TextParametersChangedCallback));

    public static readonly AdamantiumProperty MaxLinesProperty = AdamantiumProperty.Register(nameof(MaxLines),
        typeof(int), typeof(TextBlock),
        new PropertyMetadata(0, PropertyMetadataOptions.AffectsMeasure));

    public static readonly AdamantiumProperty BalanceLinesProperty = AdamantiumProperty.Register(nameof(BalanceLines),
        typeof(bool), typeof(TextBlock),
        new PropertyMetadata(false, PropertyMetadataOptions.AffectsMeasure));

    public static readonly AdamantiumProperty CharacterStyleProperty = AdamantiumProperty.Register(nameof(CharacterStyle),
        typeof(CharacterStyle), typeof(TextBlock), new PropertyMetadata(null, PropertyMetadataOptions.AffectsMeasure));

    public static readonly AdamantiumProperty ParagraphStyleProperty = AdamantiumProperty.Register(nameof(ParagraphStyle),
        typeof(ParagraphStyle), typeof(TextBlock), new PropertyMetadata(null, PropertyMetadataOptions.AffectsMeasure));

    public static readonly AdamantiumProperty TextLineBoundsProperty = AdamantiumProperty.Register(nameof(TextLineBounds),
        typeof(TextLineBounds), typeof(TextBlock),
        new PropertyMetadata(TextLineBounds.Full, PropertyMetadataOptions.AffectsMeasure));

    public static readonly AdamantiumProperty IsOversetProperty = AdamantiumProperty.RegisterReadOnly(nameof(IsOverset),
        typeof(bool), typeof(TextBlock), new PropertyMetadata(false));

    public static readonly AdamantiumProperty OverflowContentTargetProperty = AdamantiumProperty.Register(
        nameof(OverflowContentTarget), typeof(TextBlockOverflow), typeof(TextBlock),
        new PropertyMetadata(null, PropertyMetadataOptions.AffectsMeasure | PropertyMetadataOptions.AffectsRender));

    public static readonly AdamantiumProperty HasOverflowContentProperty = AdamantiumProperty.RegisterReadOnly(
        nameof(HasOverflowContent), typeof(bool), typeof(TextBlock), new PropertyMetadata(false));

    public static readonly AdamantiumProperty WritingModeProperty = AdamantiumProperty.Register(nameof(WritingMode),
        typeof(WritingMode), typeof(TextBlock),
        new PropertyMetadata(WritingMode.Horizontal, PropertyMetadataOptions.AffectsMeasure, TextParametersChangedCallback));

    /// <summary>Whether the text can be selected and copied: on by default, with the I-beam over it; off protects the
    /// text from being copied and shows the inherited cursor. Inherited, so a control's style turns it off for every
    /// label inside it - the themes do so in buttons, tabs, menus and list rows.</summary>
    public static readonly AdamantiumProperty IsTextSelectionEnabledProperty = AdamantiumProperty.RegisterAttached(
        "IsTextSelectionEnabled", typeof(Boolean), typeof(UIComponent),
        new PropertyMetadata(true, PropertyMetadataOptions.Inherits | PropertyMetadataOptions.InheritsFromTypeStyle,
            OnTextSelectionEnabledChanged));

    public static Boolean GetIsTextSelectionEnabled(IUIComponent element) =>
        element.GetValue<Boolean>(IsTextSelectionEnabledProperty);

    public static void SetIsTextSelectionEnabled(IUIComponent element, Boolean value) =>
        element.SetValue(IsTextSelectionEnabledProperty, value);

    /// <summary>What selected text is painted on; the same default as a text box's.</summary>
    public static readonly AdamantiumProperty SelectionBrushProperty = AdamantiumProperty.Register(nameof(SelectionBrush),
        typeof(Brush), typeof(TextBlock),
        new PropertyMetadata(TextBoxBase.DefaultSelectionBrush, PropertyMetadataOptions.AffectsRender));

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
        CommandManager.RegisterClassCommandBinding(typeof(TextBlock), new CommandBinding(ApplicationCommands.Copy,
            static (sender, _) => ((TextBlock)sender).CopySelection(), static (sender, e) => AskAsText(sender, e,
                static block => block.HasSelection)));
        CommandManager.RegisterClassCommandBinding(typeof(TextBlock), new CommandBinding(ApplicationCommands.SelectAll,
            static (sender, _) => ((TextBlock)sender).SelectAll(), static (sender, e) => AskAsText(sender, e,
                static block => block._lastText?.Length > 0)));
    }

    private static void AskAsText(object sender, CanExecuteRoutedEventArgs e, Func<TextBlock, bool> can)
    {
        var block = (TextBlock)sender;
        e.Handled = block.CanSelect;
        e.CanExecute = block.CanSelect && can(block);
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

    public static readonly AdamantiumProperty TextOutlineProperty = AdamantiumProperty.Register(nameof(TextOutline),
        typeof(TextOutline), typeof(TextBlock),
        new PropertyMetadata(null, PropertyMetadataOptions.AffectsRender, OnGlyphLookChanged));

    public static readonly AdamantiumProperty TextShadowProperty = AdamantiumProperty.Register(nameof(TextShadow),
        typeof(TextShadow), typeof(TextBlock),
        new PropertyMetadata(null, PropertyMetadataOptions.AffectsRender, OnGlyphLookChanged));

    public static readonly AdamantiumProperty TextGlowProperty = AdamantiumProperty.Register(nameof(TextGlow),
        typeof(TextGlow), typeof(TextBlock),
        new PropertyMetadata(null, PropertyMetadataOptions.AffectsRender, OnGlyphLookChanged));

    public static readonly AdamantiumProperty TextReflectionProperty = AdamantiumProperty.Register(nameof(TextReflection),
        typeof(TextReflection), typeof(TextBlock),
        new PropertyMetadata(null, PropertyMetadataOptions.AffectsRender, OnGlyphLookChanged));

    private static void TextParametersChangedCallback(AdamantiumComponent a, AdamantiumPropertyChangedEventArgs e)
    {
        
    }

    private static void TextChangedCallback(AdamantiumComponent a, AdamantiumPropertyChangedEventArgs e)
    {
        if (a is TextBlock { SelectionCarries: false } block)
        {
            block.ClearSelection();
        }
    }

    private TextLayout _textLayout;

    // Text shaping (ProcessText) is expensive, and measure+arrange both ran it EVERY layout pass - so a control whose
    // size animates (e.g. a Button growing, whose templated ContentPresenter re-measures this TextBlock each frame)
    // re-shaped the same unchanged text twice per frame. Cache the result and only re-shape when an input that actually
    // affects the layout changes.
    private Size<double> _cachedSize;
    private bool _hasLayout;
    private IFont _layoutFont;   // the face _textLayout was built for (the font is fixed per TextLayout)
    private string _lastText;
    private double _lastFontSize, _lastWidth, _lastHeight;
    // The width/height the last measure was given (a wrapping block reflows to this when it has no explicit Width, so
    // the wrap boundary comes from the container - like WPF - instead of a hardcoded Width). Infinity = unconstrained.
    private Size<double> _lastConstraint = new Size<double>(double.PositiveInfinity, double.PositiveInfinity);
    private TextWrapping _lastWrapping;
    private TextTrimming _lastTrimming;
    private HorizontalTextAlignment _lastHAlign;
    private VerticalTextAlignment _lastVAlign;
    private bool _lastJustify;
    private TextDirection _lastDirection;
    private TextComposition _lastComposition;
    private TabStopList _lastTabStops;
    private DropCap _lastDropCap;
    private int _lastColumns = 1;
    private double _lastColumnGap;
    private ExclusionList _lastExclusions;
    private WritingMode _lastWritingMode;
    private double _lastTracking;
    private TextKerning _lastKerning;
    private BaselineGrid _lastBaselineGrid;
    private readonly BaselineGridFollower _grid;
    private double _lastLineHeight = double.NaN;
    private LineStackingStrategy _lastLineStacking;
    private double _lastLineSpacing;
    private int _lastMaxLines;
    private bool _lastBalanceLines;
    private TextLineBounds _lastLineBounds;
    private const float Unbounded = 1e7f;

    // The size the text was laid out at before the last, and what it came to: see EnsureLayout.
    private bool _hasOther;
    private double _otherWidth;
    private double _otherHeight;
    private Size<double> _otherSize;

    // The overflows the text runs on into, in order, and how many of the layout's frames are the block's own: none
    // while it runs into no overflow.
    private List<TextBlockOverflow> _overflows = [];
    // Kept as numbers: a room not yet known is NaN, and Size's equality would never find it the same.
    private readonly List<(double Width, double Height, double Phase)> _lastRooms = [];
    private int _ownFrames;
    private const int MostColumnGrowth = 200;
    private const char LineSeparator = (char)0x2028;
    private const char ObjectReplacement = (char)0xFFFC;
    private const char ParagraphSeparator = (char)0x2029;
    private TextAttributes _lastShaping;

    private readonly List<(int Start, int End, Hyperlink Link)> _links = [];
    private readonly List<(int Index, IMeasurableComponent Child)> _objects = [];
    private readonly List<IMeasurableComponent> _hosted = [];
    private LayoutManager _watchedLayout;
    private readonly List<Size<double>> _hostedSizes = [];
    private readonly Dictionary<IMeasurableComponent, double> _hostedRaises = [];
    private readonly HashSet<IMeasurableComponent> _placedHosted = [];
    private Hyperlink _pressedLink;
    private int _focusedLink = -1;
    private bool _overLink;
    private bool _madeFocusable;
    private bool _focusableForSelection;
    private bool _focusingForSelection;
    private bool _selectionHoldsFocus;
    private CaretPosition _selectionAnchor;
    private CaretPosition _selectionFocus;
    private string _selectedIn;
    private bool _selectionLogical;
    private bool _selectionWhole;
    private object _selectionContext;
    private bool _selecting;
    private bool _pressPending;
    private Vector2<double> _pressAt;
    private int _pressClicks;
    private bool _pressExtends;
    private IInputComponent _listenedWindow;
    private IInputComponent _listenedTop;
    private PointerPressedEventHandler _outsidePress;
    private KeyEventHandler _copyFromRoot;
    private RoutedEventHandler _selectionTaken;

    private static readonly RoutedEvent SelectionTakenEvent = EventManager.RegisterRoutedEvent("SelectionTaken",
        RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(TextBlock));
    private readonly TextCasing.Shown _shown = new();
    private InlineCollection _inlines;
    private bool _inlinesDirty = true;
    private string _inlineText;
    private readonly List<int> _inlineStarts = [];
    private readonly List<(int Start, int Length, Paragraph Paragraph)> _paragraphs = [];
    private bool _runFontsPending;
    private TextEffectCollection _textEffects;
    private readonly List<(Inline Inline, int Start, int Length)> _inlineRanges = [];
    private readonly EngineTextStyles _engineStyles;
    private readonly List<(int Start, int End, CharacterStyle Style)> _styleRanges = [];
    private readonly List<GlyphEffect> _resolvedEffects = [];
    private GlyphEffect[] _drawnEffects = [];
    private readonly HeldBrushes _effectBrushes;
    private readonly HeldBrushes _inlineBrushes;

    public TextBlock()
    {
        _effectBrushes = new HeldBrushes(this);
        _inlineBrushes = new HeldBrushes(this);
        _engineStyles = new EngineTextStyles(this, _inlineBrushes);
        _grid = new BaselineGridFollower(this, () => null, () => 0, InvalidateMeasure);
        Id = Guid.NewGuid().ToString();
        // _textLayout is built lazily in EnsureLayout: FontFamily is inherited and may be null here (it resolves against
        // an ancestor / DefaultFontFamily), and the typeface is fixed per TextLayout, so it's rebuilt when the font changes.
    }

    // Shapes the text only when an input that affects the layout changed since the last call; otherwise returns the
    // cached size (and leaves _textLayout as-is). Cheap to call every measure/arrange/render. Width/Height use
    // double.Equals so NaN==NaN counts as "unchanged" (an auto-sized label stays a cache hit while its parent resizes).
    private Size<double> EnsureLayout(bool measuring = false)
    {
        // Resolve the inherited font (falling back to the single shared default), and (re)build the layout when it
        // changes - the typeface is fixed per TextLayout, so a font change means a new TextLayout for that face.
        var loadsSeen = TypefaceStore.LoadedCount;
        var fontReady = TryResolveFont(FontFamily ?? DefaultFontFamily, out var font);
        if (_textLayout == null || !ReferenceEquals(_layoutFont, font))
        {
            _textLayout = new TextLayout(font.Typeface, font);
            _layoutFont = font;
            _hasLayout = false;
        }

        _textLayout.LoadFontsInBackground = !FontAtlasStore.SynchronousFill;
        _textLayout.Direction = TextDirection;
        var composition = ResolveTextComposition(_lastComposition);
        _textLayout.Composition = composition;
        _textLayout.TabStops = TabStops;
        var dropCap = DropCapOf(ref fontReady);
        _textLayout.DropCap = dropCap;
        var vertical = WritingMode == WritingMode.VerticalRightToLeft;
        _textLayout.WritingMode = WritingMode;
        _textLayout.Tracking = Tracking;
        _textLayout.Kerning = Kerning;
        var baselineGrid = _grid.Grid();
        _textLayout.BaselineGrid = baselineGrid;
        _textLayout.LineHeight = LineHeight;
        _textLayout.LineStacking = LineStackingStrategy;
        _textLayout.LineSpacing = LineSpacing;
        _textLayout.MaxLines = MaxLines;
        _textLayout.BalanceLines = BalanceLines;
        _textLayout.LineBounds = TextLineBounds;

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
        var text = _shown.Of(HasInlines ? InlineText() : Text, TextTransform, Language);
        var shaping = TextShaping(font);
        var threads = TakeChain(vertical ? height : width, vertical ? width : height);

        var sameInputs = _hasLayout && !_inlinesDirty && SameRooms(threads)
            && _lastText == text && _lastFontSize.Equals(FontSize)
            && _lastWrapping == TextWrapping && _lastTrimming == TextTrimming
            && _lastHAlign == HorizontalTextAlignment && _lastVAlign == VerticalTextAlignment
            && _lastJustify == JustifyLastLine && _lastDirection == TextDirection
            && ReferenceEquals(_lastComposition, composition) && Equals(_lastTabStops, TabStops)
            && Equals(_lastDropCap, dropCap)
            && _lastColumns == Columns && _lastColumnGap.Equals(ColumnGap) && Equals(_lastExclusions, Exclusions)
            && _lastWritingMode == WritingMode && _lastTracking.Equals(Tracking) && _lastKerning == Kerning
            && Equals(_lastBaselineGrid, baselineGrid)
            && _lastLineHeight.Equals(LineHeight)
            && _lastLineStacking == LineStackingStrategy && _lastLineSpacing.Equals(LineSpacing)
            && _lastMaxLines == MaxLines && _lastBalanceLines == BalanceLines && _lastLineBounds == TextLineBounds
            && ShapesLike(_lastShaping, shaping);
        if (sameInputs && _lastWidth.Equals(width) && _lastHeight.Equals(height))
        {
            return _cachedSize;
        }

        // A panel measuring twice a pass at two sizes - a grid's star column first at an upper bound, then at its
        // share - is answered the size laid out at the other one, not laid out again each time. What is drawn and
        // arranged is laid out at the size it ends up with.
        if (measuring && sameInputs && _hasOther && _otherWidth.Equals(width) && _otherHeight.Equals(height))
        {
            return _otherSize;
        }

        _hasOther = sameInputs;
        (_otherWidth, _otherHeight, _otherSize) = (_lastWidth, _lastHeight, _cachedSize);
        _runFontsPending = false;
        _links.Clear();
        _objects.Clear();
        _inlineBrushes.Clear();
        if (!HasInlines)
        {
            _inlineRanges.Clear();
            _paragraphs.Clear();
            _engineStyles.Clear();
        }

        var attributed = HasInlines
            ? InlineAttributedText(text, shaping)
            : CharacterStyle != null || ParagraphStyle != null ? StyledAttributedText(text, shaping)
            : shaping == null ? null : new AttributedText(text, shaping);
        _inlineBrushes.Take();
        if (_links.Count > 0 && !Focusable && GetValueSource(FocusableProperty) == ValuePriority.Default)
        {
            SetCurrentValue(FocusableProperty, true);
            _madeFocusable = true;
        }
        else if (_links.Count == 0 && _madeFocusable)
        {
            ClearValue(FocusableProperty, ValuePriority.Binding);
            _madeFocusable = false;
        }

        if (_focusedLink >= _links.Count)
        {
            _focusedLink = _links.Count - 1;
        }
        _textLayout.Exclusions = Exclusions;
        _textLayout.Frames = FramesOf(vertical ? height : width, vertical ? width : height, vertical, Lay, out var growth);
        _ownFrames = 0;
        if (threads)
        {
            _textLayout.Frames = Threaded(_textLayout.Frames, vertical, width, height);
            growth = 0;
        }

        _cachedSize = Lay();
        for (var grown = 0; growth > 0 && (MaxLines <= 0 || _textLayout.LineCount < MaxLines)
                            && _textLayout.OversetIndex < (text?.Length ?? 0) && grown < MostColumnGrowth; grown++)
        {
            _textLayout.Frames = _textLayout.Frames
                .Select(frame => vertical
                    ? new RectangleF(frame.X, frame.Y, frame.Width + growth, frame.Height)
                    : new RectangleF(frame.X, frame.Y, frame.Width, frame.Height + growth))
                .ToArray();
            _cachedSize = Lay();
        }

        // The layout's size takes in the overflows' frames: the block is as large as its own room.
        if (_ownFrames > 0)
        {
            _cachedSize = new Size<double>(width, height);
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
            if (ink > 0) _cachedSize = new Size<double>(Math.Min(_cachedSize.Width, ink), _cachedSize.Height);
        }

        if (TextTrimming != TextTrimming.None && TextWrapping == TextWrapping.NoWrap
            && double.IsNaN(Height) && !double.IsNaN(height) && vertical)
        {
            var ink = System.Math.Ceiling(_textLayout.RealTextDimensions.Height);
            if (ink > 0) _cachedSize = new Size<double>(_cachedSize.Width, Math.Min(_cachedSize.Height, ink));
        }

        _hasLayout = true;
        IsOverset = (_textLayout.Frames != null || _textLayout.MaxLines > 0)
                    && _textLayout.OversetIndex < (text?.Length ?? 0);
        Distribute(text?.Length ?? 0, vertical);
        _lastRooms.Clear();
        _lastRooms.AddRange(_overflows.Select(RoomOf));
        _inlinesDirty = false;
        _lastShaping = shaping;
        if (_lastText != text)
        {
            CarrySelectionTo(text);
        }

        _lastText = text; _lastFontSize = FontSize; _lastWidth = width; _lastHeight = height;
        _lastWrapping = TextWrapping; _lastTrimming = TextTrimming;
        _lastHAlign = HorizontalTextAlignment; _lastVAlign = VerticalTextAlignment; _lastJustify = JustifyLastLine;
        _lastDirection = TextDirection;
        _lastComposition = composition;
        _lastTabStops = TabStops;
        _lastDropCap = dropCap;
        _lastColumns = Columns;
        _lastColumnGap = ColumnGap;
        _lastExclusions = Exclusions;
        _lastWritingMode = WritingMode;
        _lastTracking = Tracking;
        _lastKerning = Kerning;
        _lastBaselineGrid = baselineGrid;
        _lastLineHeight = LineHeight;
        _lastLineStacking = LineStackingStrategy;
        _lastLineSpacing = LineSpacing;
        _lastMaxLines = MaxLines;
        _lastBalanceLines = BalanceLines;
        _lastLineBounds = TextLineBounds;
        return _cachedSize;

        Size<double> Lay() => attributed == null
            ? _textLayout.ProcessText(text, FontSize, new Size<double>(width, height), TextWrapping, TextTrimming,
                HorizontalTextAlignment, vertical ? AcrossLines() : VerticalTextAlignment, JustifyLastLine)
            : _textLayout.ProcessText(attributed, FontSize, new Size<double>(width, height), TextWrapping, TextTrimming,
                HorizontalTextAlignment, vertical ? AcrossLines() : VerticalTextAlignment, JustifyLastLine);
    }

    private RectangleF[] FramesOf(double length, double across, bool vertical, Func<Size<double>> lay, out float growth)
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
                // The least room between two lines: a paragraph may keep more above it.
                var spacing = _textLayout.LineCount > 1
                    ? Math.Max(0, Enumerable.Range(1, _textLayout.LineCount - 1).Min(line =>
                        _textLayout.GetLine(line).Top - _textLayout.GetLine(line - 1).Top - _textLayout.GetLine(line - 1).Height))
                    : 0;
                growth = (float)(lineHeight + spacing);
                var rows = Math.Ceiling(_textLayout.LineCount / (double)columns);
                columnAcross = (float)(rows * (lineHeight + spacing) - spacing + 1);
            }
        }

        return Enumerable.Range(0, columns)
            .Select(column => Frame((float)(column * (columnLength + gap)), columnAcross))
            .ToArray();

        RectangleF Frame(float start, float extent) => vertical
            ? new RectangleF(0, start, extent, columnLength)
            : new RectangleF(start, 0, columnLength, extent);
    }

    // The chain of overflows from the target on, up to one that is not shown or shows another block's text; a new
    // chain lays the text out again. Whether the block lays its text out to run on, its own room first - with the chain
    // cut short, what does not fit is overset: wrapped by words in a block bounded both ways.
    private bool TakeChain(double length, double across)
    {
        var chain = new List<TextBlockOverflow>();
        for (var next = OverflowContentTarget; next != null && !chain.Contains(next) && next.Join(this);
             next = next.OverflowContentTarget)
        {
            chain.Add(next);
        }

        if (!chain.SequenceEqual(_overflows))
        {
            foreach (var gone in _overflows.Where(overflow => !chain.Contains(overflow)))
            {
                gone.Leave(this);
            }

            _overflows = chain;
            _hasLayout = false;
        }

        return OverflowContentTarget != null && TextWrapping == TextWrapping.WrapByWords && Bounded(length)
               && Bounded(across);

        static bool Bounded(double extent) => !double.IsNaN(extent) && !double.IsInfinity(extent);
    }

    private bool SameRooms(bool threads) =>
        threads == _ownFrames > 0 && (!threads || _lastRooms.SequenceEqual(_overflows.Select(RoomOf)));

    private static (double Width, double Height, double Phase) RoomOf(TextBlockOverflow overflow) =>
        (overflow.Room.Width, overflow.Room.Height, overflow.GridPhase);

    // The block's own frames, then a frame for each overflow as large as the room it gives, below them all and clear of
    // the exclusions, on whole pixels, its rows where the grid the overflow stands on has them (at its top without one).
    // An overflow not yet measured, or one without bounds, ends the chain.
    private RectangleF[] Threaded(IReadOnlyList<RectangleF> own, bool vertical, double width, double height)
    {
        var frames = own?.ToList() ?? [new RectangleF(0, 0, (float)width, (float)height)];
        _ownFrames = frames.Count;
        var bottom = frames.Max(frame => frame.Bottom);
        foreach (var exclusion in Exclusions ?? (IReadOnlyList<RectangleF>)[])
        {
            bottom = Math.Max(bottom, exclusion.Bottom);
        }

        var gap = (float)(4 * FontSize);
        var grid = _textLayout.BaselineGrid;
        foreach (var overflow in _overflows)
        {
            var room = overflow.Room;
            var length = vertical ? room.Height : room.Width;
            var across = vertical ? room.Width : room.Height;
            if (double.IsNaN(length) || double.IsInfinity(length))
            {
                length = vertical ? height : width;
            }

            var unbounded = double.IsNaN(across) || double.IsInfinity(across);
            if (!(length > 0) || (unbounded && vertical) || (!unbounded && !(across > 0)))
            {
                break;
            }

            var top = bottom + gap;
            if (grid is { Step: > 0 } rows)
            {
                var phase = overflow.Grid is { } its && its.Step.Equals(rows.Step) ? its.Offset : 0;
                var start = rows.Offset - phase;
                top = (float)(start + Math.Ceiling((top - start) / rows.Step) * rows.Step);
            }

            top = MathF.Round(top);

            var extent = unbounded ? Unbounded : (float)across;
            frames.Add(vertical ? new RectangleF(0, top, extent, (float)length) : new RectangleF(0, top, (float)length, extent));
            if (unbounded)
            {
                break;
            }

            bottom = top + extent;
        }

        return frames.ToArray();
    }

    // Each overflow shown its frame, as tall as its lines where its room has no bottom; whether the text goes on past
    // the block and past each overflow.
    private void Distribute(int length, bool vertical)
    {
        var frames = _textLayout.Frames;
        var over = _textLayout.OversetIndex < length;
        if (_ownFrames == 0)
        {
            HasOverflowContent = IsOverset;
            foreach (var overflow in _overflows)
            {
                overflow.Show(null, default, false, _textLayout.Revision);
            }

            return;
        }

        var lastFrame = -1;
        var extents = new double[frames.Count];
        for (var line = 0; line < _textLayout.LineCount; line++)
        {
            var frame = _textLayout.GetLineFrame(line);
            if (frame < 0)
            {
                continue;
            }

            lastFrame = Math.Max(lastFrame, frame);
            var metrics = _textLayout.GetLine(line);
            extents[frame] = Math.Max(extents[frame], metrics.Top + metrics.Height - frames[frame].Y);
        }

        HasOverflowContent = lastFrame >= _ownFrames || over;
        for (var k = 0; k < _overflows.Count; k++)
        {
            var index = _ownFrames + k;
            if (index >= frames.Count)
            {
                _overflows[k].Show(null, default, false, _textLayout.Revision);
                continue;
            }

            var frame = frames[index];
            var content = vertical || frame.Height < Unbounded
                ? new Size<double>(frame.Width, frame.Height)
                : new Size<double>(frame.Width, Math.Ceiling(extents[index]));
            _overflows[k].Show(frame, content, lastFrame > index || over, _textLayout.Revision);
        }
    }

    // Whether a control among the inlines stands at a point of an overflow showing the frame.
    internal bool ShowsObjectAt(RectangleF frame, Vector2<double> local)
    {
        if (_hosted.Count == 0)
        {
            return false;
        }

        var x = local.X + frame.X;
        var y = local.Y + frame.Y;
        foreach (var item in _textLayout.GetInlineObjects())
        {
            if (x >= item.Rect.Left && x <= item.Rect.Right && y >= item.Rect.Top && y <= item.Rect.Bottom
                && _objects.Exists(entry => entry.Index == item.Index))
            {
                return true;
            }
        }

        return false;
    }

    // Where a rect of the layout is shown, in the block's space: in its own frames, or in the overflow showing it.
    private Rect ShownRect(RectangleF rect)
    {
        foreach (var overflow in _overflows)
        {
            if (overflow.Frame is { } frame && rect.Bottom >= frame.Top && rect.Top <= frame.Bottom
                && rect.Right >= frame.Left && rect.Left <= frame.Right)
            {
                var at = overflow.TranslatePoint(new Vector2<double>(rect.X - frame.X, rect.Y - frame.Y), this);
                return new Rect(at.X, at.Y, rect.Width, rect.Height);
            }
        }

        return new Rect(rect.X + LinesShift(), rect.Y, rect.Width, rect.Height);
    }

    // The part of the layout the block draws itself: its own frames, with room for ink past their edges.
    private RectangleF? OwnWindow()
    {
        if (_ownFrames == 0)
        {
            return null;
        }

        var frames = _textLayout.Frames;
        var margin = (float)FontSize;
        var left = frames.Take(_ownFrames).Min(frame => frame.Left) - margin;
        var top = frames.Take(_ownFrames).Min(frame => frame.Top) - margin;
        var right = frames.Take(_ownFrames).Max(frame => frame.Right) + margin;
        var bottom = frames.Take(_ownFrames).Max(frame => frame.Bottom) + margin;
        return new RectangleF(left, top, right - left, bottom - top);
    }

    internal void OverflowArranged()
    {
        if (_hosted.Count > 0)
        {
            InvalidateArrange();
        }
    }

    protected override void OnRenderInvalidated()
    {
        foreach (var overflow in _overflows ?? [])
        {
            overflow.InvalidateRender(false);
        }
    }

    protected override void OnPaintInvalidated()
    {
        foreach (var overflow in _overflows ?? [])
        {
            overflow.InvalidatePaint();
        }
    }

    private DropCap DropCapOf(ref bool fontReady)
    {
        if (DropCapLines == 0)
        {
            return null;
        }

        IFont font = null;
        if (DropCapFontFamily is { IsInstalled: true } family)
        {
            fontReady &= TryResolveFont(family, out font);
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

    /// <summary>The case the text is shown and copied in, as CSS's <c>text-transform</c>, letter by letter as the
    /// block's <c>Language</c> maps it (else the application's language). <see cref="Text"/>, the inlines and what
    /// automation reads keep the text as it is.</summary>
    public TextTransform TextTransform
    {
        get => GetValue<TextTransform>(TextTransformProperty);
        set => SetValue(TextTransformProperty, value);
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

    /// <summary>The most lines shown, as Avalonia's; 0 (the default) for any number. The rest is not shown, or, with
    /// <see cref="TextTrimming"/>, the last line ends in an ellipsis.</summary>
    public int MaxLines
    {
        get => GetValue<int>(MaxLinesProperty);
        set => SetValue(MaxLinesProperty, value);
    }

    /// <summary>Whether text wrapped by words takes the narrowest width that keeps its lines - <see cref="MaxLines"/> of
    /// them, or as many as wrapping at the full width gives - so they come out about equally long, as CSS's
    /// text-wrap: balance; the lines are still aligned in the full width. With MaxLines 2 a label of two or more words
    /// goes on two lines. Meant for short text: the text is laid out several times. Off by default.</summary>
    public bool BalanceLines
    {
        get => GetValue<bool>(BalanceLinesProperty);
        set => SetValue(BalanceLinesProperty, value);
    }

    /// <summary>The named character style of the block's text, as an inline's: over <see cref="ParagraphStyle"/>, under
    /// what the block sets itself; an inline with its own style takes that one instead. Null sets none.</summary>
    public CharacterStyle CharacterStyle
    {
        get => GetValue<CharacterStyle>(CharacterStyleProperty);
        set => SetValue(CharacterStyleProperty, value);
    }

    /// <summary>The named paragraph style of the block's text, as a <see cref="Paragraph"/>'s: how its paragraphs stand
    /// and the formatting their text takes; a paragraph with its own style takes that one instead. Null sets none.</summary>
    public ParagraphStyle ParagraphStyle
    {
        get => GetValue<ParagraphStyle>(ParagraphStyleProperty);
        set => SetValue(ParagraphStyleProperty, value);
    }

    /// <summary>Where the block's box starts above its first line and ends below its last, as WinUI's:
    /// <see cref="Adamantium.Graphics.Fonts.TextLineBounds.Full"/> (the default) takes whole lines;
    /// <c>Tight</c> takes from the top of the capitals to the baseline, so a label centered by its layout has its
    /// capitals in the middle. Horizontal text in one column: text in <see cref="Columns"/> or around
    /// <see cref="Exclusions"/> keeps whole lines.</summary>
    public TextLineBounds TextLineBounds
    {
        get => GetValue<TextLineBounds>(TextLineBoundsProperty);
        set => SetValue(TextLineBoundsProperty, value);
    }

    /// <summary>Whether the text did not all fit into the block's <see cref="Columns"/> or <see cref="MaxLines"/>, so
    /// that the rest is not shown; trimmed text says so with its ellipsis instead. Set after each layout, so it binds.</summary>
    public bool IsOverset
    {
        get => GetValue<bool>(IsOversetProperty);
        private set => SetValue(IsOversetProperty, value);
    }

    /// <summary>Where the text that does not fit into the block goes on, as WinUI's RichTextBlock sends it on: the
    /// block lays its text out through its own room and then through the room each overflow of the chain gives. Text
    /// wrapped by <see cref="TextWrapping.WrapByWords"/> into a block of bounded height (width, for vertical text).
    /// <c>OverflowContentTarget="{Binding ElementName=next}"</c>.</summary>
    public TextBlockOverflow OverflowContentTarget
    {
        get => GetValue<TextBlockOverflow>(OverflowContentTargetProperty);
        set => SetValue(OverflowContentTargetProperty, value);
    }

    /// <summary>Whether the text goes on past the block: into its <see cref="OverflowContentTarget"/>, or, with none, out
    /// of sight (<see cref="IsOverset"/>).</summary>
    public bool HasOverflowContent
    {
        get => GetValue<bool>(HasOverflowContentProperty);
        private set => SetValue(HasOverflowContentProperty, value);
    }

    /// <summary>How many lines a drop cap - the first characters set large, the lines running beside them - spans:
    /// 2 or more; 0 (the default) for none. Text wrapped by words only.</summary>
    public int DropCapLines
    {
        get => GetValue<int>(DropCapLinesProperty);
        set => SetValue(DropCapLinesProperty, value);
    }

    /// <summary>How many characters the drop cap takes: 1 (the default) or more.</summary>
    public int DropCapCharacters
    {
        get => GetValue<int>(DropCapCharactersProperty);
        set => SetValue(DropCapCharactersProperty, value);
    }

    /// <summary>The family the drop cap is set in, as a decorative face of initials; unset or not installed, the
    /// text's own.</summary>
    public FontFamily DropCapFontFamily
    {
        get => GetValue<FontFamily>(DropCapFontFamilyProperty);
        set => SetValue(DropCapFontFamilyProperty, value);
    }

    /// <summary>Which way lines run: across (the default), or down and stacked from right to left, as Chinese and
    /// Japanese are set vertically. A vertical line is as long as the block is high - its <see cref="Height"/>, else the
    /// height it is given - the block asks to be as wide as its lines take, the first line stands at its right edge
    /// unless <see cref="VerticalTextAlignment"/> is set to place the lines across a wider block (Top at its right edge,
    /// Bottom at its left), <see cref="HorizontalTextAlignment"/> places text along
    /// the lines (<see cref="HorizontalTextAlignment.Left"/> at their top), and <see cref="Columns"/> stack one under
    /// another. Drop caps are for horizontal text only.</summary>
    public WritingMode WritingMode
    {
        get => GetValue<WritingMode>(WritingModeProperty);
        set => SetValue(WritingModeProperty, value);
    }

    public Boolean IsTextSelectionEnabled
    {
        get => GetValue<Boolean>(IsTextSelectionEnabledProperty);
        set => SetValue(IsTextSelectionEnabledProperty, value);
    }

    public Brush SelectionBrush
    {
        get => GetValue<Brush>(SelectionBrushProperty);
        set => SetValue(SelectionBrushProperty, value);
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

    /// <summary>A line round each letter, where an inline sets none of its own.</summary>
    public TextOutline TextOutline
    {
        get => GetValue<TextOutline>(TextOutlineProperty);
        set => SetValue(TextOutlineProperty, value);
    }

    /// <summary>A shadow of the letters, where an inline sets none of its own.</summary>
    public TextShadow TextShadow
    {
        get => GetValue<TextShadow>(TextShadowProperty);
        set => SetValue(TextShadowProperty, value);
    }

    /// <summary>A glow round the letters, where an inline sets none of its own.</summary>
    public TextGlow TextGlow
    {
        get => GetValue<TextGlow>(TextGlowProperty);
        set => SetValue(TextGlowProperty, value);
    }

    /// <summary>A reflection of the letters below their lines, where an inline sets none of its own.</summary>
    public TextReflection TextReflection
    {
        get => GetValue<TextReflection>(TextReflectionProperty);
        set => SetValue(TextReflectionProperty, value);
    }

    private static void OnGlyphLookChanged(AdamantiumComponent d, AdamantiumPropertyChangedEventArgs e)
    {
        if (d is not TextBlock block)
        {
            return;
        }

        EventHandler redraw = block.OnGlyphLookValueChanged;
        switch (e.NewValue)
        {
            case TextOutline outline:
                outline.Changed += redraw;
                block.Follow(e.Property, () => outline.Changed -= redraw);
                break;
            case TextShadow shadow:
                shadow.Changed += redraw;
                block.Follow(e.Property, () => shadow.Changed -= redraw);
                break;
            case TextGlow glow:
                glow.Changed += redraw;
                block.Follow(e.Property, () => glow.Changed -= redraw);
                break;
            case TextReflection reflection:
                reflection.Changed += redraw;
                block.Follow(e.Property, () => reflection.Changed -= redraw);
                break;
            default:
                block.Follow(e.Property, null);
                break;
        }
    }

    private void OnGlyphLookValueChanged(object sender, EventArgs e) => InvalidateRender(false);

    private Thickness _inkOverhang;
    private double _reflectionDistance = -1;

    /// <summary>How far the letters' outlines, shadows, glows and reflections reach past the block, as last drawn.</summary>
    public Thickness InkOverhang => _inkOverhang;

    internal void ApplyGlyphLooks()
    {
        _textLayout.GlyphOutline = TextOutline?.ToGlyphOutline();
        _textLayout.GlyphShadow = TextShadow?.ToGlyphShadow();
        _textLayout.GlyphGlow = TextGlow?.ToGlyphGlow();
        _textLayout.GlyphReflection = TextReflection?.ToGlyphReflection();
        Span<double> reach = stackalloc double[4];
        _reflectionDistance = -1;
        Reach(reach, _textLayout.GlyphOutline, _textLayout.GlyphShadow, _textLayout.GlyphGlow, _textLayout.GlyphReflection);
        if (_textLayout.AttributedText is { } attributed)
        {
            foreach (var run in attributed.Runs)
            {
                Reach(reach, run.Attributes.Outline, run.Attributes.Shadow, run.Attributes.Glow, run.Attributes.Reflection);
            }
        }

        _inkOverhang = new Thickness(reach[0], reach[1], reach[2], reach[3]);
    }

    internal Thickness OverhangOf(Size<double> frame) =>
        _reflectionDistance < 0
            ? _inkOverhang
            : new Thickness(_inkOverhang.Left, _inkOverhang.Top, _inkOverhang.Right,
                Math.Max(_inkOverhang.Bottom, frame.Height + _reflectionDistance));

    private void Reach(Span<double> reach, GlyphOutline outline, GlyphShadow shadow, GlyphGlow glow, GlyphReflection reflection)
    {
        var around = Math.Max(Finite(outline?.Width), Finite(glow?.Radius));
        for (var side = 0; side < 4; side++)
        {
            reach[side] = Math.Max(reach[side], around);
        }

        if (shadow != null)
        {
            var blur = Finite(shadow.Blur);
            reach[0] = Math.Max(reach[0], blur - Math.Min(0, Finite(shadow.OffsetX)));
            reach[1] = Math.Max(reach[1], blur - Math.Min(0, Finite(shadow.OffsetY)));
            reach[2] = Math.Max(reach[2], blur + Math.Max(0, Finite(shadow.OffsetX)));
            reach[3] = Math.Max(reach[3], blur + Math.Max(0, Finite(shadow.OffsetY)));
        }

        if (reflection != null)
        {
            _reflectionDistance = Math.Max(_reflectionDistance, Finite(reflection.Distance));
            reach[3] = Math.Max(reach[3], RenderSize.Height + Finite(reflection.Distance));
        }
    }

    private static double Finite(double? value) => value is { } number && double.IsFinite(number) ? Math.Abs(number) : 0;

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

    /// <summary>Ranges of the text drawn apart from the rest, as WPF's text effects: moved, turned, scaled or recolored.
    /// Their positions count the text as it is laid out, a line break and a paragraph's end one each; an inline's own
    /// <see cref="Inline.TextEffects"/> count from its start. Only the drawing changes, so changing them draws the text
    /// again without laying it out.</summary>
    public TextEffectCollection TextEffects
    {
        get
        {
            if (_textEffects == null)
            {
                _textEffects = new TextEffectCollection(this);
                _textEffects.Changed += OnTextEffectsChanged;
            }

            return _textEffects;
        }
    }

    private void OnTextEffectsChanged(object sender, System.EventArgs e) => EffectsChanged();

    internal string ShownText => HasInlines
        ? InlineText().Replace(LineSeparator, '\n').Replace(ParagraphSeparator, '\n')
            .Replace(ObjectReplacement.ToString(), string.Empty)
        : Text;

    internal IReadOnlyList<IMeasurableComponent> HostedChildren => _hosted;

    private void OnInlinesChanged(object sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems != null)
            foreach (Inline old in e.OldItems) { old.Changed -= OnInlineChanged; RemoveLogicalChild(old); old.ReparentEffects(); old.SetInBlock(old.ReachesABlock()); }
        if (e.NewItems != null)
            foreach (Inline added in e.NewItems) { AddLogicalChild(added); added.Changed += OnInlineChanged; added.ReparentEffects(); added.SetInBlock(true); }
        if (!SelectionCarries)
        {
            ClearSelection();
        }

        _inlinesDirty = true;
        HostChildren();
        InvalidateMeasure();
    }

    private void OnInlineChanged(object sender, System.EventArgs e)
    {
        if (ReferenceEquals(e, Inline.EffectsOnly))
        {
            EffectsChanged();
            return;
        }

        if (ReferenceEquals(e, Inline.StyleOnly))
        {
            _inlinesDirty = true;
            InvalidateMeasure();
            return;
        }

        _inlinesDirty = true;
        HostChildren();
        InvalidateMeasure();
    }

    /// <summary>Discards the inlines with the block: they are not in the visual tree, so nothing else reaches them.</summary>
    protected override void OnDiscarded()
    {
        base.OnDiscarded();
        DiscardEffects(_textEffects);
        DiscardInlines(_inlines);
    }

    private static void DiscardEffects(TextEffectCollection effects)
    {
        if (effects == null)
        {
            return;
        }

        foreach (var effect in effects)
        {
            DiscardedVisuals.Publish(effect);
        }
    }

    private static void DiscardInlines(InlineCollection inlines)
    {
        if (inlines == null)
        {
            return;
        }

        foreach (var inline in inlines)
        {
            DiscardedVisuals.Publish(inline);
            if (inline.HasTextEffects)
            {
                DiscardEffects(inline.TextEffects);
            }

            if (inline is Span span)
            {
                DiscardInlines(span.Inlines);
            }
        }
    }

    private void HostChildren()
    {
        List<IMeasurableComponent> children = [];
        foreach (var container in Containers(_inlines))
        {
            if (container.Child != null)
            {
                children.Add(container.Child);
            }
        }

        foreach (var gone in _hosted.Where(child => !children.Contains(child)).ToList())
        {
            _hosted.Remove(gone);
            _hostedRaises.Remove(gone);
            RemoveVisualChild(gone);
        }

        foreach (var added in children.Where(child => !_hosted.Contains(child)))
        {
            _hosted.Add(added);
            AddVisualChild(added);
        }

        WatchHostedBaselines(IsAttachedToVisualTree);
    }

    // A control in the line whose text moved inside it, its size and slot the same, tells nobody: after each layout pass
    // that did work the block reads the baselines of its controls again, and lays itself out when one moved.
    private void WatchHostedBaselines(bool attached)
    {
        var wanted = attached && _hosted.Count > 0 ? LayoutManager.For(this) : null;
        if (ReferenceEquals(wanted, _watchedLayout))
        {
            return;
        }

        if (_watchedLayout != null)
        {
            _watchedLayout.LayoutUpdated -= OnLayoutSettled;
        }

        _watchedLayout = wanted;
        if (wanted != null)
        {
            wanted.LayoutUpdated += OnLayoutSettled;
        }
    }

    private void OnLayoutSettled(object sender, EventArgs e)
    {
        foreach (var child in _hosted)
        {
            if (_placedHosted.Contains(child) && OnTheBaseline(child) && _hostedRaises.TryGetValue(child, out var raise)
                && child.PreviousArrangeSlot is { } slot && !RaiseIn(child, slot).Equals(raise))
            {
                InvalidateMeasure();
                return;
            }
        }
    }

    // Down vertical lines a control stands centered across its line, on no baseline.
    private bool OnTheBaseline(IMeasurableComponent child) =>
        WritingMode != WritingMode.VerticalRightToLeft
        && (Containers(_inlines).FirstOrDefault(container => ReferenceEquals(container.Child, child)) is not
            { BaselineAlignment: { } alignment } || alignment == BaselineAlignment.Baseline);

    private static IEnumerable<InlineUIContainer> Containers(InlineCollection inlines)
    {
        foreach (var inline in inlines ?? [])
        {
            if (inline is InlineUIContainer container)
            {
                yield return container;
            }
            else if (inline is Span span)
            {
                foreach (var nested in Containers(span.Inlines))
                {
                    yield return nested;
                }
            }
        }
    }

    protected internal override void DisownVisualChild(IUIComponent child)
    {
        foreach (var container in Containers(_inlines).Where(container => ReferenceEquals(container.Child, child)).ToList())
        {
            container.Child = null;
        }

        base.DisownVisualChild(child);
    }

    // Each paragraph is a stretch of the text ended by U+2029. The inlines between two paragraphs make one of their own,
    // as text outside a p does in HTML, unless they hold nothing but white space. Where each top-level inline starts is
    // kept, so the attributes go where its text went; -1 for one left out.
    private string InlineText()
    {
        if (_inlinesDirty || _inlineText == null)
        {
            var text = new StringBuilder();
            _inlineStarts.Clear();
            _paragraphs.Clear();
            var paragraphs = 0;
            var hasParagraphs = _inlines.Any(inline => inline is Paragraph);
            var looseFrom = -1;
            var looseFirst = 0;
            foreach (var inline in _inlines)
            {
                if (inline is Paragraph paragraph)
                {
                    EndLoose();
                    Separate();
                    var start = text.Length;
                    _inlineStarts.Add(start);
                    AppendInlineText(paragraph.Inlines, text);
                    _paragraphs.Add((start, text.Length - start, paragraph));
                    paragraphs++;
                    continue;
                }

                if (looseFrom < 0)
                {
                    looseFrom = text.Length;
                    looseFirst = _inlineStarts.Count;
                    Separate();
                }

                _inlineStarts.Add(text.Length);
                AppendInline(inline, text);
            }

            EndLoose();
            _inlineText = text.ToString();

            void Separate()
            {
                if (paragraphs == 0)
                {
                    return;
                }

                if (_paragraphs.Count > 0 && _paragraphs[^1].Start + _paragraphs[^1].Length == text.Length)
                {
                    _paragraphs[^1] = (_paragraphs[^1].Start, _paragraphs[^1].Length + 1, _paragraphs[^1].Paragraph);
                }

                text.Append(ParagraphSeparator);
            }

            void EndLoose()
            {
                if (looseFrom < 0)
                {
                    return;
                }

                var blank = hasParagraphs;
                for (var i = looseFrom + (paragraphs > 0 ? 1 : 0); i < text.Length && blank; i++)
                {
                    blank = text[i] is ' ' or '\t' or '\r' or '\n';
                }

                if (blank)
                {
                    text.Length = looseFrom;
                    for (var i = looseFirst; i < _inlineStarts.Count; i++)
                    {
                        _inlineStarts[i] = -1;
                    }

                    if (_paragraphs.Count > 0 && _paragraphs[^1].Start + _paragraphs[^1].Length == looseFrom + 1)
                    {
                        _paragraphs[^1] = (_paragraphs[^1].Start, _paragraphs[^1].Length - 1, _paragraphs[^1].Paragraph);
                    }
                }
                else
                {
                    paragraphs++;
                }

                looseFrom = -1;
            }
        }

        return _inlineText;
    }

    private static void AppendInlineText(InlineCollection inlines, StringBuilder text)
    {
        foreach (var inline in inlines)
        {
            AppendInline(inline, text);
        }
    }

    private static void AppendInline(Inline inline, StringBuilder text)
    {
        switch (inline)
        {
            case Run run:
                text.Append(run.Text);
                break;
            case LineBreak:
                text.Append(LineSeparator);
                break;
            case InlineUIContainer { Child: not null }:
                text.Append(ObjectReplacement);
                break;
            case Span span:
                AppendInlineText(span.Inlines, text);
                break;
        }
    }

    private AttributedText InlineAttributedText(string text, TextAttributes shaping)
    {
        var attributed = new AttributedText(text, shaping);
        List<Inline> chain = [];
        _inlineRanges.Clear();
        _engineStyles.Clear();
        _styleRanges.Clear();
        for (var i = 0; i < _inlines.Count; i++)
        {
            var start = _inlineStarts[i];
            if (start >= 0)
            {
                ApplyInline(_inlines[i], chain, attributed, ref start);
            }
        }

        foreach (var (start, end, style) in _styleRanges)
        {
            attributed.SetCharacterStyle(start, end - start, _engineStyles.Of(style));
        }

        _styleRanges.Clear();

        // The block's paragraph style for text in no paragraph and paragraphs with none of their own.
        if (ParagraphStyle is { } blockStyle && text.Length > 0)
        {
            attributed.SetParagraphStyle(0, text.Length, _engineStyles.Of(blockStyle));
        }

        foreach (var (start, length, paragraph) in _paragraphs)
        {
            attributed.SetParagraph(start, length, OwnParagraphAttributes(paragraph));
            if (paragraph.ParagraphStyle is { } paragraphStyle)
            {
                attributed.SetParagraphStyle(start, length, _engineStyles.Of(paragraphStyle));
            }

            if (length > 0 && text[start + length - 1] == ParagraphSeparator)
            {
                chain.Add(paragraph);
                attributed.Apply(start + length - 1, 1, InlineAttributes(chain));
                chain.Clear();
            }
        }

        return attributed;
    }

    // What a paragraph sets itself, field by field, so its style - or the block's - shows through the rest.
    private ParagraphAttributes OwnParagraphAttributes(Paragraph paragraph)
    {
        var styled = (paragraph.ParagraphStyle ?? ParagraphStyle) != null;
        var own = new ParagraphAttributes { Alignment = paragraph.HorizontalTextAlignment };
        if (paragraph.GetValueSource(Paragraph.MarginProperty) != ValuePriority.Default || !styled)
        {
            var margin = paragraph.Margin;
            own = own.With(new ParagraphAttributes
            {
                MarginLeft = margin.Left, MarginRight = margin.Right, SpaceBefore = margin.Top, SpaceAfter = margin.Bottom,
            });
        }

        if (paragraph.GetValueSource(Paragraph.TextIndentProperty) != ValuePriority.Default || !styled)
        {
            own = own.With(new ParagraphAttributes { TextIndent = paragraph.TextIndent });
        }

        if (!double.IsNaN(paragraph.LineHeight))
        {
            own = own.With(new ParagraphAttributes { LineHeight = paragraph.LineHeight });
        }

        return own;
    }

    private bool SetsItself(AdamantiumProperty property) => EngineTextStyles.SetsItself(this, property);

    private (FontWeight Weight, FontStyle Style, FontStretch Stretch) BlockFace() =>
        EngineTextStyles.Face(this, CharacterStyle, ParagraphStyle);

    // Text with no inlines in the block's styles: its font as they choose it, and what the block sets itself over them.
    private AttributedText StyledAttributedText(string text, TextAttributes shaping)
    {
        var attributed = new AttributedText(text, shaping);
        if (text.Length == 0)
        {
            return attributed;
        }

        var (weight, fontStyle, stretch) = BlockFace();
        _runFontsPending |= !TryResolveFont(FontFamily ?? DefaultFontFamily, weight, fontStyle, stretch, FontVariations,
            out var font);
        attributed.Apply(0, text.Length, new TextAttributes
        {
            Font = font,
            Synthesis = FontSynthesisRules.Needed(font, weight, fontStyle, FontSynthesis),
        });
        _engineStyles.Style(attributed, this, CharacterStyle, ParagraphStyle);
        return attributed;
    }

    // The nearest character style along the chain: an inner inline's wins.
    private static CharacterStyle CharacterStyleOf(List<Inline> chain)
    {
        for (var i = chain.Count - 1; i >= 0; i--)
        {
            if (chain[i].CharacterStyle is { } style)
            {
                return style;
            }
        }

        return null;
    }

    private static bool IsStyled(ValuePriority source) => source is ValuePriority.Style or ValuePriority.Trigger;

    private static bool IsLook(ValuePriority source) => source == ValuePriority.Default || IsStyled(source);

    private static bool Inherits(Inline inline, AdamantiumProperty property) =>
        inline.GetValueSource(property) == ValuePriority.Inherited;

    private static TValue NearestStyleFrom<TValue>(List<Inline> chain, int from, Func<CharacterStyle, TValue> field)
    {
        for (var i = chain.Count - 1; i >= from; i--)
        {
            if (chain[i].CharacterStyle is { } format)
            {
                return field(format);
            }
        }

        return default;
    }

    private static ParagraphStyle ParagraphStyleOf(List<Inline> chain)
    {
        foreach (var inline in chain)
        {
            if (inline is Paragraph { ParagraphStyle: { } style })
            {
                return style;
            }
        }

        return null;
    }

    // Runs of one style next to each other take it as one range; an inline with no character style takes the block's,
    // unless its paragraph has a style of its own, which is nearer.
    private void ApplyStyle(List<Inline> chain, AttributedText attributed, int start, int length)
    {
        if ((CharacterStyleOf(chain) ?? (ParagraphStyleOf(chain) == null ? CharacterStyle : null)) is not { } style)
        {
            return;
        }

        if (_styleRanges.Count > 0 && _styleRanges[^1] is var (lastStart, lastEnd, lastStyle)
                                   && lastEnd == start && ReferenceEquals(lastStyle, style))
        {
            _styleRanges[^1] = (lastStart, start + length, style);
            return;
        }

        _styleRanges.Add((start, start + length, style));
    }

    private void ApplyInlines(InlineCollection inlines, List<Inline> chain, AttributedText attributed, ref int start)
    {
        foreach (var inline in inlines)
        {
            ApplyInline(inline, chain, attributed, ref start);
        }
    }

    private void ApplyInline(Inline inline, List<Inline> chain, AttributedText attributed, ref int start)
    {
        var from = start;
        chain.Add(inline);
        _inlineBrushes.Add(inline.Foreground);
        _inlineBrushes.Add(inline.Background);
        if (inline.HasDecorationLines)
        {
            foreach (var line in inline.DecorationLines)
            {
                _inlineBrushes.Add(line.Brush);
            }
        }
        switch (inline)
        {
            case Run run:
                var length = (run.Text ?? string.Empty).Length;
                attributed.Apply(start, length, InlineAttributes(chain));
                ApplyStyle(chain, attributed, start, length);
                start += length;
                break;
            case LineBreak:
                attributed.Apply(start, 1, InlineAttributes(chain));
                ApplyStyle(chain, attributed, start, 1);
                start++;
                break;
            case InlineUIContainer { Child: { } child }:
                attributed.Apply(start, 1,
                    InlineAttributes(chain, child.DesiredSize, _hostedRaises.GetValueOrDefault(child)));
                ApplyStyle(chain, attributed, start, 1);
                _objects.Add((start, child));
                start++;
                break;
            case Span span:
                var spanStart = start;
                ApplyInlines(span.Inlines, chain, attributed, ref start);
                if (span is Hyperlink link)
                {
                    _links.Add((spanStart, start, link));
                }
                else if (span is Ruby ruby && !string.IsNullOrEmpty(ruby.Annotation))
                {
                    var annotation = TextTransform == TextTransform.None
                        ? ruby.Annotation
                        : TextCasing.Transform(ruby.Annotation, TextTransform, TextCasing.CultureOf(ruby.Language ?? Language));
                    attributed.SetRuby(spanStart, start - spanStart,
                        new RubyAnnotation(annotation, ruby.RubyPosition, ruby.AnnotationFontSize));
                }

                break;
        }

        _inlineRanges.Add((inline, from, start - from));
        chain.RemoveAt(chain.Count - 1);
    }

    // The chain runs from the outermost span down to the inline itself: each sets what it sets, the nearer one wins,
    // and the lines add up.
    private TextAttributes InlineAttributes(List<Inline> chain, Size<double>? objectSize = null, double objectRaise = 0)
    {
        var fontSize = double.NaN;
        FontFamily family = null;
        Brush foreground = null;
        Brush background = null;
        var decorations = TextDecorations.None;
        var looks = TextDecorations.None;
        var styleLines = false;
        FontWeight? weight = null;
        FontStyle? style = null;
        FontStretch? stretch = null;
        FontSynthesis? synthesis = null;
        FontFeatureList features = null;
        FontVariationList variations = null;
        string language = null;
        int? palette = null;
        double? tracking = null;
        TextKerning? kerning = null;
        double? shift = null;
        BaselineAlignment? alignment = null;
        List<TextDecorationLine> lines = null;
        TextOutline outline = null;
        TextShadow shadow = null;
        TextGlow glow = null;
        TextReflection reflection = null;
        var foregroundAt = -1;
        var weightAt = -1;
        var fontStyleAt = -1;
        for (var i = 0; i < chain.Count; i++)
        {
            var inline = chain[i];
            if (inline.HasDecorationLines)
            {
                lines ??= [];
                lines.AddRange(inline.DecorationLines.Select(line => line.ToLine()));
            }

            if (!double.IsNaN(inline.FontSize))
            {
                fontSize = inline.FontSize;
            }

            family = inline.FontFamily ?? family;
            if (inline.Foreground != null)
            {
                foreground = inline.Foreground;
                foregroundAt = IsStyled(inline.GetValueSource(Inline.ForegroundProperty)) ? i : -1;
            }

            background = inline.Background ?? background;
            var source = inline.GetValueSource(Inline.TextDecorationsProperty);
            var characterLines = inline.CharacterStyle?.ResolvedDecorations(this);
            var paragraphLines = (inline as Paragraph)?.ParagraphStyle?.ResolvedDecorations(this);
            var ownStyleLines = characterLines == null && paragraphLines == null
                ? (TextDecorations?)null
                : (characterLines ?? TextDecorations.None) | (paragraphLines ?? TextDecorations.None);
            var byLook = IsStyled(source) || (inline is Hyperlink && source == ValuePriority.Default);
            styleLines |= ownStyleLines != null;
            if (source != ValuePriority.Default && !IsStyled(source))
            {
                decorations |= inline.TextDecorations;
            }
            else if (ownStyleLines is { } styled)
            {
                looks = TextDecorations.None;
                decorations |= styled | (byLook ? TextDecorations.None : inline.TextDecorations);
            }
            else if (byLook)
            {
                looks |= inline.TextDecorations;
            }
            else
            {
                decorations |= inline.TextDecorations;
            }

            // A face an inline only inherits from the block is the block's: the block's styles choose it below.
            if (inline.FontWeight != null && !Inherits(inline, Inline.FontWeightProperty))
            {
                weight = inline.FontWeight;
                weightAt = IsLook(inline.GetValueSource(Inline.FontWeightProperty)) ? i : -1;
            }

            if (inline.FontStyle != null && !Inherits(inline, Inline.FontStyleProperty))
            {
                style = inline.FontStyle;
                fontStyleAt = IsLook(inline.GetValueSource(Inline.FontStyleProperty)) ? i : -1;
            }

            stretch = Inherits(inline, Inline.FontStretchProperty) ? stretch : inline.FontStretch ?? stretch;
            synthesis = inline.FontSynthesis ?? synthesis;
            features = inline.FontFeatures ?? features;
            variations = inline.FontVariations ?? variations;
            language = inline.Language ?? language;
            palette = inline.ColorPalette ?? palette;
            tracking = inline.Tracking ?? tracking;
            kerning = inline.Kerning ?? kerning;
            shift = inline.BaselineShift ?? shift;
            alignment = inline.BaselineAlignment ?? alignment;
            outline = inline.TextOutline ?? outline;
            shadow = inline.TextShadow ?? shadow;
            glow = inline.TextGlow ?? glow;
            reflection = inline.TextReflection ?? reflection;
        }

        decorations |= looks;
        var characterStyle = CharacterStyleOf(chain);
        var paragraphStyle = ParagraphStyleOf(chain);
        if (foregroundAt >= 0)
        {
            foreground = NearestStyleFrom(chain, foregroundAt, format => format.ResolvedForeground(this)) ?? foreground;
        }

        if (weightAt >= 0)
        {
            weight = NearestStyleFrom(chain, weightAt, format => format.ResolvedWeight(this)) ?? weight;
        }

        if (fontStyleAt >= 0)
        {
            style = NearestStyleFrom(chain, fontStyleAt, format => format.ResolvedStyle(this)) ?? style;
        }

        // Text in the block's styles only: what the block sets itself wins over them, as an inline's own does.
        if (characterStyle == null && paragraphStyle == null && (CharacterStyle != null || ParagraphStyle != null))
        {
            fontSize = double.IsNaN(fontSize) && SetsItself(FontSizeProperty) ? FontSize : fontSize;
            foreground ??= SetsItself(ForegroundProperty) ? Foreground : null;
        }

        if (objectSize != null && alignment is null or BaselineAlignment.Baseline)
        {
            shift = (shift ?? characterStyle?.ResolvedBaselineShift(this) ?? paragraphStyle?.ResolvedBaselineShift(this)
                        ?? CharacterStyle?.ResolvedBaselineShift(this) ?? ParagraphStyle?.ResolvedBaselineShift(this) ?? 0)
                    + objectRaise;
        }

        // The font as the inlines choose it, else their character style, else their paragraph's style, else the block:
        // its own styles only where the inline has none, as a style of its own takes the block's place whole.
        var block = characterStyle == null && paragraphStyle == null
            ? BlockFace()
            : (Weight: FontWeight, Style: FontStyle, Stretch: FontStretch);
        var resolvedWeight = weight ?? characterStyle?.ResolvedWeight(this) ?? paragraphStyle?.ResolvedWeight(this)
                             ?? block.Weight;
        var resolvedStyle = style ?? characterStyle?.ResolvedStyle(this) ?? paragraphStyle?.ResolvedStyle(this)
                            ?? block.Style;
        var resolvedStretch = stretch ?? characterStyle?.ResolvedStretch(this) ?? paragraphStyle?.ResolvedStretch(this)
                              ?? block.Stretch;
        _runFontsPending |= !TryResolveFont(family ?? FontFamily ?? DefaultFontFamily, resolvedWeight, resolvedStyle,
            resolvedStretch, variations ?? FontVariations, out var font);
        return new TextAttributes
        {
            Font = font,
            Synthesis = FontSynthesisRules.Needed(font, resolvedWeight, resolvedStyle, synthesis ?? FontSynthesis),
            Features = Typography.FeaturesOf(chain[^1], features ?? FontFeatures),
            Language = language,
            ColorPalette = palette,
            Tracking = tracking,
            Kerning = kerning,
            BaselineShift = shift,
            BaselineAlignment = alignment,
            ObjectSize = objectSize,
            FontSize = double.IsNaN(fontSize) ? null : fontSize,
            Foreground = (foreground as SolidColorBrush)?.Color,
            Background = (background as SolidColorBrush)?.Color,
            Decorations = decorations == TextDecorations.None && !styleLines ? null : decorations,
            DecorationLines = lines,
            Outline = outline?.ToGlyphOutline(),
            Shadow = shadow?.ToGlyphShadow(),
            Glow = glow?.ToGlyphGlow(),
            Reflection = reflection?.ToGlyphReflection(),
        };
    }

    private void DrawAdornments(IDrawingSession session, bool backgrounds, RectangleF? window, Vector2F shift)
    {
        if (_textLayout.AttributedText == null)
        {
            return;
        }

        foreach (var adornment in _textLayout.GetAdornments())
        {
            if ((adornment.Kind == TextAdornmentKind.Background) != backgrounds || !Touches(window, adornment.Rect))
            {
                continue;
            }

            var brush = adornment.Color is { } color ? new SolidColorBrush(color) : Foreground;
            var rect = adornment.Rect;
            rect.X += shift.X;
            rect.Y += shift.Y;
            if (adornment.Kind == TextAdornmentKind.Squiggle)
            {
                WavyLine.Draw(session, rect, brush, WritingMode == WritingMode.VerticalRightToLeft);
            }
            else if (adornment.Kind == TextAdornmentKind.Background)
            {
                session.DrawRectangle(brush, new Rect(rect.X, rect.Y, rect.Width, rect.Height));
            }
            else if (adornment.Dashes != null)
            {
                DrawDashedLine(session, OnPixels(new Rect(rect.X, rect.Y, rect.Width, rect.Height)), brush,
                    adornment.Dashes);
            }
            else
            {
                session.DrawRectangle(brush, OnPixels(new Rect(rect.X, rect.Y, rect.Width, rect.Height)));
            }
        }
    }

    private void DrawDashedLine(IDrawingSession session, Rect band, Brush brush, IReadOnlyList<double> dashes)
    {
        var down = WritingMode == WritingMode.VerticalRightToLeft;
        var thickness = down ? band.Width : band.Height;
        var pen = new Pen(brush, thickness, dashStrokeArray: dashes, dashStartCap: PenLineCap.Flat,
            dashEndCap: PenLineCap.Flat);
        var middle = (down ? band.X : band.Y) + thickness / 2;
        session.DrawLine(down ? new Vector2<double>(middle, band.Y) : new Vector2<double>(band.X, middle),
            down ? new Vector2<double>(middle, band.Y + band.Height) : new Vector2<double>(band.X + band.Width, middle), pen);
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

    /// <summary>Lays the text out again in the fonts that arrived.</summary>
    protected internal override void OnFontsArrived()
    {
        _hasLayout = false;
        base.OnFontsArrived();
    }

    protected override Size<double> MeasureOverride(Size<double> availableSize)
    {
        _lastConstraint = availableSize;   // a wrapping block reflows to this (its container's width) when it has no explicit Width
        MeasureHosted();
        _grid.Measure();
        var size = EnsureLayout(measuring: true);
        // The layout is what gets drawn. A measure whose size came out the same has no arrange after it, so a block that
        // already has a slot goes back to the slot's width - else right-aligned text stood at the width it was MEASURED
        // against, short of the slot's edge. It does so in the arrange it asks for: laid out at the old slot here, it
        // was laid out once more at its new one on every change of width.
        if (UseSlot(RenderSize))
        {
            InvalidateArrange();
        }

        return size;
    }

    /// <summary>Lays the text out and takes the whole slot, so text alignment works within it.</summary>
    protected override Size<double> ArrangeOverride(Size<double> finalSize)
    {
        // A trimmed block trims to the slot it actually GOT, which is not always the one it was measured against: a tab
        // header is measured unbounded and then capped by the tab's MaxWidth, so the boundary only exists here. Re-stated
        // before the layout call so the ellipsis lands at the real edge instead of at a width nobody will give it.
        UseSlot(finalSize);

        EnsureLayout();
        ArrangeHosted();
        return finalSize;
    }

    public override bool IsMeasureBoundary => base.IsMeasureBoundary && _hosted.Count == 0;

    protected override void OnPropertyChanged(AdamantiumPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (_hosted.Count > 0 && e.Property.GetDefaultMetadata(GetType())?.AffectsRender == true)
        {
            InvalidateArrange();
        }

        if (e.Property == CharacterStyleProperty || e.Property == ParagraphStyleProperty)
        {
            (e.OldValue as TextFormat)?.Unuse(this);
            (e.NewValue as TextFormat)?.Use(this);
            RestyleText();
        }
    }

    void ITextStyleUser.TextStyleChanged(bool rebased, HashSet<TextFormat> telling) => RestyleText();

    // The block's styles answer their resources from where it stands: a theme scope switched round it changes them.
    void Adamantium.UI.Core.Resources.IResolvesOwnResources.ResolveResources()
    {
        if (CharacterStyle != null || ParagraphStyle != null)
        {
            RestyleText();
        }
    }

    private void RestyleText()
    {
        _inlinesDirty = true;
        InvalidateMeasure();
        InvalidateRender(false);
    }

    protected internal override bool MeasuresByPlace => BaselineGridStep > 0 && !double.IsInfinity(BaselineGridStep);


    private void MeasureHosted()
    {
        if (_hosted.Count == 0 && _hostedSizes.Count == 0)
        {
            return;
        }

        var sizes = new List<Size<double>>(_hosted.Count);
        var raised = false;
        foreach (var child in _hosted)
        {
            child.Measure(new Size<double>(double.PositiveInfinity, double.PositiveInfinity));
            sizes.Add(child.DesiredSize);
            var raise = HostedRaise(child);
            if (!_hostedRaises.TryGetValue(child, out var old) || !old.Equals(raise))
            {
                _hostedRaises[child] = raise;
                raised = true;
            }
        }

        if (raised || !sizes.SequenceEqual(_hostedSizes))
        {
            _hostedSizes.Clear();
            _hostedSizes.AddRange(sizes);
            _inlinesDirty = true;
        }
    }

    private double HostedRaise(IMeasurableComponent child)
    {
        if (!OnTheBaseline(child))
        {
            return 0;
        }

        var size = child.DesiredSize;
        var slot = child.PreviousArrangeSlot is { } previous && previous.Size == size ? previous : new Rect(size);
        child.Arrange(slot);
        return RaiseIn(child, slot);
    }

    private static double RaiseIn(IMeasurableComponent child, Rect slot) =>
        ContentBaseline(child) is { } baseline ? baseline - slot.Y - slot.Height : 0;

    private static double? ContentBaseline(IUIComponent element)
    {
        if (element.Visibility != Visibility.Visible)
        {
            return null;
        }

        if (element is TextBlock { Layout: { LineCount: > 0 } layout, WritingMode: WritingMode.Horizontal } text
            && !string.IsNullOrEmpty(text.ShownText))
        {
            return element.Bounds.Y + layout.GetLine(0).Baseline;
        }

        foreach (var child in element.VisualChildren)
        {
            if (ContentBaseline(child) is { } baseline)
            {
                return element.Bounds.Y + baseline;
            }
        }

        return null;
    }

    private void ArrangeHosted()
    {
        if (_hosted.Count == 0)
        {
            return;
        }

        // A control the text does not reach (past a cut) stands nowhere: its baseline is no line's.
        var placed = _placedHosted;
        placed.Clear();
        foreach (var item in _textLayout.GetInlineObjects())
        {
            var index = _objects.FindIndex(entry => entry.Index == item.Index);
            if (index < 0)
            {
                continue;
            }

            var child = _objects[index].Child;
            var slot = ShownRect(item.Rect);
            child.Arrange(slot);
            placed.Add(child);
            if (_hostedRaises.TryGetValue(child, out var raise) && raise != 0 && !RaiseIn(child, slot).Equals(raise))
            {
                InvalidateMeasure();
            }
        }

        foreach (var child in _hosted.Where(child => !placed.Contains(child)))
        {
            child.Arrange(new Rect(0, 0, 0, 0));
        }
    }

    private bool UseSlot(Size<double> slot)
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

            _lastConstraint = new Size<double>(_lastConstraint.Width, slot.Height);
            return true;
        }

        if (!double.IsNaN(Width) || slot.Width <= 0)
        {
            return false;
        }

        _lastConstraint = new Size<double>(slot.Width, _lastConstraint.Height);
        return true;
    }

    internal float LinesShift()
    {
        if (WritingMode != WritingMode.VerticalRightToLeft || !double.IsNaN(Width) || _textLayout == null
            || _ownFrames > 0)
        {
            return 0;
        }

        var room = RenderSize.Width - _textLayout.CalculatedLayoutSize.Width;
        return (float)Math.Round(AcrossLines() switch
        {
            VerticalTextAlignment.Center => room / 2,
            VerticalTextAlignment.Bottom => 0,
            _ => room,
        });
    }

    private VerticalTextAlignment AcrossLines() => GetValueSource(VerticalTextAlignmentProperty) == ValuePriority.Default
        ? VerticalTextAlignment.Top
        : VerticalTextAlignment;

    TextRenderingParameters GetTextRenderingParameters(Vector2F textPos, Size<double> size)
    {
        return new TextRenderingParameters()
        {
            HorizontalTextAlignment = HorizontalTextAlignment,
            VerticalTextAlignment = VerticalTextAlignment,
            JustifyLastLine = JustifyLastLine,
            TextTrimming = TextTrimming,
            TextWrapping = TextWrapping,
            Color = ((SolidColorBrush)Foreground).Color,
            TextArea = new Rectangle<int>(textPos.As<int>(), size.As<int>())
        };
    }

    protected override void OnRender(IDrawingContext context)
    {
        var session = context.ForControl(this);
        EnsureLayout();   // refresh shaping if a render-only property (alignment/wrapping) changed since the last measure
        var shift = new Vector2F(LinesShift(), 0);
        var window = OwnWindow();
        DrawAdornments(session, backgrounds: true, window, shift);
        DrawSelection(session, window, shift);
        var parameters = GetTextRenderingParameters(shift, DesiredSize);
        var effects = DrawnEffects(shift);
        ApplyGlyphLooks();
        if (window is { } own)
        {
            session.DrawText(parameters, DesiredSize, _textLayout, Foreground, Background, Stroke, effects, own);
        }
        else if (effects.Length > 0)
        {
            session.DrawText(parameters, DesiredSize, _textLayout, Foreground, Background, Stroke, effects);
        }
        else
        {
            session.DrawText(parameters, DesiredSize, _textLayout, Foreground, Background, Stroke);
        }

        DrawAdornments(session, backgrounds: false, window, shift);
    }

    // An overflow's part of the text: the glyphs, lines, backgrounds and selection of its frame, moved to its top left.
    // The block's own background and text effects stay in the block.
    internal void RenderFrame(IDrawingSession session, RectangleF frame, Size<double> size)
    {
        EnsureLayout();
        ApplyGlyphLooks();
        var margin = (float)FontSize;
        var window = new RectangleF(frame.X - margin, frame.Y - margin, frame.Width + 2 * margin, frame.Height + 2 * margin);
        var shift = new Vector2F(-frame.X, -frame.Y);
        DrawAdornments(session, backgrounds: true, window, shift);
        DrawSelection(session, window, shift);
        session.DrawText(GetTextRenderingParameters(shift, size), size, _textLayout, Foreground, Brushes.Transparent,
            Stroke, [], window);
        DrawAdornments(session, backgrounds: false, window, shift);
    }

    private static bool Touches(RectangleF? window, RectangleF rect) => window is not { } part
        || (rect.Right >= part.Left && rect.Left <= part.Right && rect.Bottom >= part.Top && rect.Top <= part.Bottom);

    private GlyphEffect[] DrawnEffects(Vector2F area)
    {
        _effectBrushes.Clear();
        _resolvedEffects.Clear();
        ResolveEffects(_resolvedEffects, _effectBrushes, area);
        _effectBrushes.Take();
        if (!SameAsDrawn())
        {
            _drawnEffects = [.. _resolvedEffects];
        }

        return _drawnEffects;
    }

    void IGlyphEffectSource.ResolveEffects(List<GlyphEffect> effects) =>
        ResolveEffects(effects, null, new Vector2F(LinesShift(), 0));

    private void ResolveEffects(List<GlyphEffect> into, HeldBrushes brushes, Vector2F area)
    {
        var length = _textLayout?.Text?.Length ?? 0;
        for (var i = 0; i < (_textEffects?.Count ?? 0); i++)
        {
            Resolve(_textEffects[i], 0, length, area, into, brushes);
        }

        if (HasInlines)
        {
            foreach (var (inline, start, count) in _inlineRanges)
            {
                if (!inline.HasTextEffects)
                {
                    continue;
                }

                var effects = inline.TextEffects;
                for (var i = 0; i < effects.Count; i++)
                {
                    Resolve(effects[i], start, Math.Min(count, length - start), area, into, brushes);
                }
            }
        }
    }

    private void Resolve(TextEffect effect, int offset, int limit, Vector2F area, List<GlyphEffect> into,
        HeldBrushes brushes)
    {
        if (!effect.TryResolve(offset, limit, area, RenderSize, out var resolved))
        {
            return;
        }

        into.Add(resolved);
        brushes?.Add(effect.Foreground);
    }

    private void EffectsChanged()
    {
        _effectBrushes.Clear();
        _resolvedEffects.Clear();
        ResolveEffects(_resolvedEffects, _effectBrushes, new Vector2F(LinesShift(), 0));
        _effectBrushes.Take();
        if (_resolvedEffects.Count > 0 != _drawnEffects.Length > 0)
        {
            InvalidateRender(false);
        }
        else if (_drawnEffects.Length > 0)
        {
            RenderDirty.MarkEffects(this);
        }
    }

    internal override void OnRenderValueChanged(object sender, System.EventArgs e)
    {
        if (sender is Brush brush)
        {
            if (_inlineBrushes.Contains(brush))
            {
                _inlinesDirty = true;
                InvalidateMeasure();
            }

            if (_effectBrushes.Contains(brush))
            {
                EffectsChanged();
            }
        }

        base.OnRenderValueChanged(sender, e);
    }

    private bool SameAsDrawn()
    {
        if (_drawnEffects.Length != _resolvedEffects.Count)
        {
            return false;
        }

        for (var i = 0; i < _drawnEffects.Length; i++)
        {
            if (_drawnEffects[i] != _resolvedEffects[i])
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>The selected text as it is copied: a line break and a paragraph's end as a new line, a control in the
    /// line left out.</summary>
    public string SelectedText
    {
        get
        {
            EnsureLayout();
            if (!HasSelection)
            {
                return string.Empty;
            }

            var selected = new StringBuilder();
            foreach (var (start, end) in SelectedRanges())
            {
                selected.Append(_lastText, start, end - start);
            }

            return selected.ToString().Replace(LineSeparator, '\n').Replace(ParagraphSeparator, '\n')
                .Replace(ObjectReplacement.ToString(), string.Empty);
        }
    }

    /// <summary>Selects the whole text.</summary>
    public void SelectAll()
    {
        EnsureLayout();
        SelectLogical(0, _lastText?.Length ?? 0, whole: true);
    }

    /// <summary>Selects <paramref name="length"/> characters from <paramref name="start"/>; a length of zero clears
    /// the selection.</summary>
    public void Select(int start, int length)
    {
        EnsureLayout();
        var size = _lastText?.Length ?? 0;
        var from = Math.Clamp(start, 0, size);
        SelectLogical(from, (int)Math.Clamp((long)from + length, 0, size));
    }

    private bool CanSelect => IsTextSelectionEnabled;

    private bool HoldsAnchor => CanSelect && _selectedIn != null && _selectedIn == _lastText;

    private bool SelectionCarries => _selectedIn != null && SameData(DataContext, _selectionContext);

    private static bool SameData(object now, object then) =>
        ReferenceEquals(now, then) || (now is ValueType or string && Equals(now, then));

    internal void LetSelectionGo() => ClearSelection();

    private void CarrySelectionTo(string text)
    {
        if (_selectedIn == null)
        {
            return;
        }

        if (!SelectionCarries)
        {
            _selectedIn = null;
            _selectionContext = null;
            StopListeningToTheWindow();
            return;
        }

        var size = text?.Length ?? 0;
        if (_selectionWhole && !_selecting)
        {
            _selectionAnchor = new CaretPosition(0);
            _selectionFocus = new CaretPosition(size, afterPrevious: size > 0);
            _selectionLogical = true;
        }
        else
        {
            _selectionAnchor = Snapped(_selectionAnchor, size);
            _selectionFocus = Snapped(_selectionFocus, size);
        }

        _selectedIn = text;
    }

    private CaretPosition Snapped(CaretPosition position, int size)
    {
        var index = Math.Min(position.Index, size);
        if (index > 0 && index < size)
        {
            index = _textLayout.PreviousCaretStop(_textLayout.NextCaretStop(index));
        }

        return new CaretPosition(index, position.AfterPrevious && index > 0);
    }

    private bool HasSelection => HoldsAnchor &&
                                 (_selectionAnchor.Index != _selectionFocus.Index ||
                                  (!_selectionLogical && _selectionAnchor.AfterPrevious != _selectionFocus.AfterPrevious &&
                                   _textLayout.GetVisualRanges(_selectionAnchor, _selectionFocus).Count > 0));

    private IReadOnlyList<(int Start, int End)> SelectedRanges()
    {
        if (!HasSelection)
        {
            return [];
        }

        if (_selectionLogical)
        {
            return [(Math.Min(_selectionAnchor.Index, _selectionFocus.Index),
                Math.Max(_selectionAnchor.Index, _selectionFocus.Index))];
        }

        return _textLayout.GetVisualRanges(_selectionAnchor, _selectionFocus);
    }

    private void Select(CaretPosition anchor, CaretPosition focus, bool logical = false, bool whole = false)
    {
        if (!CanSelect)
        {
            return;
        }

        _selectionAnchor = anchor;
        _selectionFocus = focus;
        _selectionLogical = logical;
        _selectionWhole = whole;
        _selectionContext = DataContext;
        _selectedIn = _lastText;
        ListenToTheWindow();
        InvalidateRender(false);
        CommandManager.InvalidateRequerySuggested(this);
    }

    private void ListenToTheWindow()
    {
        if (_listenedWindow != null || (RootVisual?.Window ?? RootVisual) is not IInputComponent window)
        {
            return;
        }

        IUIComponent top = this;
        while (top.VisualParent != null)
        {
            top = top.VisualParent;
        }

        RaiseEvent(new RoutedEventArgs(SelectionTakenEvent));
        if (!ReferenceEquals(top, window))
        {
            window.RaiseEvent(new RoutedEventArgs(SelectionTakenEvent));
        }

        _outsidePress ??= OnPressAnywhere;
        _copyFromRoot ??= OnKeyAnywhere;
        _selectionTaken ??= OnSelectionTakenElsewhere;
        _listenedWindow = window;
        Listen(window);
        DataContextChanged += OnDataSwapped;
        if (top is IInputComponent own && !ReferenceEquals(top, window))
        {
            _listenedTop = own;
            Listen(own);
        }
    }

    private void Listen(IInputComponent root)
    {
        root.AddHandler(PointerEvents.PreviewPointerPressedEvent, _outsidePress, handledEventsToo: true);
        root.AddHandler(Keyboard.KeyDownEvent, _copyFromRoot);
        root.AddHandler(SelectionTakenEvent, _selectionTaken);
    }

    private void StopListening(IInputComponent root)
    {
        root.RemoveHandler(PointerEvents.PreviewPointerPressedEvent, _outsidePress);
        root.RemoveHandler(Keyboard.KeyDownEvent, _copyFromRoot);
        root.RemoveHandler(SelectionTakenEvent, _selectionTaken);
    }

    private void StopListeningToTheWindow()
    {
        if (_listenedWindow == null)
        {
            return;
        }

        StopListening(_listenedWindow);
        _listenedWindow = null;
        DataContextChanged -= OnDataSwapped;
        if (_listenedTop != null)
        {
            StopListening(_listenedTop);
            _listenedTop = null;
        }
    }

    private void OnKeyAnywhere(object sender, KeyEventArgs e)
    {
        if (!e.Handled && e.Key == Key.C && IsCopyKey(e.Key, e.Modifiers) && CopySelection())
        {
            e.Handled = true;
        }
    }

    private bool CopySelection()
    {
        var copied = SelectedText;
        if (copied.Length == 0)
        {
            return false;
        }

        Clipboard.SetText(copied);
        return true;
    }

    private void OnDataSwapped(object sender, AdamantiumPropertyChangedEventArgs e)
    {
        if (!SameData(DataContext, _selectionContext))
        {
            ClearSelection();
        }
    }

    private void OnSelectionTakenElsewhere(object sender, RoutedEventArgs e)
    {
        if (!ReferenceEquals(e.OriginalSource, this))
        {
            ClearSelection();
        }
    }

    private static bool IsCopyKey(Key key, InputModifiers modifiers)
    {
        const InputModifiers ctrl = InputModifiers.LeftControl | InputModifiers.RightControl;
        const InputModifiers others = InputModifiers.LeftShift | InputModifiers.RightShift | InputModifiers.LeftAlt |
                                      InputModifiers.RightAlt;
        return key is (Key.C or Key.Insert) && (modifiers & ctrl) != 0 && (modifiers & others) == 0;
    }

    private void OnPressAnywhere(object sender, PointerPressedEventArgs e)
    {
        if (e.ChangedButton != MouseButtons.Left)
        {
            return;
        }

        for (var node = e.OriginalSource as IUIComponent; node != null; node = node.VisualParent)
        {
            if (ReferenceEquals(node, this))
            {
                return;
            }

            if (node is TextBlock or TextBoxBase)
            {
                break;
            }
        }

        if (e.OriginalSource is IUIComponent pressed && !GetIsTextSelectionEnabled(pressed) && !InTextField(pressed))
        {
            return;
        }

        ClearSelection();
    }

    private static bool InTextField(IUIComponent pressed)
    {
        for (var node = pressed; node != null; node = node.VisualParent)
        {
            if (node is TextBoxBase)
            {
                return true;
            }
        }

        return false;
    }

    private void SelectLogical(int start, int end, bool whole = false) =>
        Select(new CaretPosition(start), new CaretPosition(end, afterPrevious: end > start), logical: true, whole);

    private void ClearSelection()
    {
        if (_selecting)
        {
            _selecting = false;
            foreach (var pointer in PointerDevice.Current.Pointers)
            {
                if (ReferenceEquals(pointer.Captured, this))
                {
                    pointer.Capture(null);
                }
            }
        }

        StopListeningToTheWindow();
        if (_selectedIn == null)
        {
            return;
        }

        _selectedIn = null;
        _selectionContext = null;
        InvalidateRender(false);
        CommandManager.InvalidateRequerySuggested(this);
    }

    private CaretPosition PositionAt(double x, double y)
    {
        EnsureLayout();
        var size = _lastText?.Length ?? 0;
        if (size == 0)
        {
            return new CaretPosition(0);
        }

        return Snapped(_textLayout.HitTest(x - LinesShift(), y));
    }

    // Where a hit puts the caret: on a caret stop, after the character before when the hit was past it.
    private CaretPosition Snapped(TextHit hit)
    {
        var size = _lastText?.Length ?? 0;
        var index = Math.Clamp(hit.CaretIndex, 0, size);
        if (index > 0 && index < size)
        {
            index = _textLayout.PreviousCaretStop(_textLayout.NextCaretStop(index));
        }

        return new CaretPosition(index, hit.IsTrailing && index == hit.CaretIndex);
    }

    private void DrawSelection(IDrawingSession session, RectangleF? window, Vector2F shift)
    {
        if (!HasSelection || SelectionBrush == null)
        {
            return;
        }

        foreach (var (start, end) in SelectedRanges())
        {
            foreach (var rect in _textLayout.GetRangeRects(start, end))
            {
                if (Touches(window, rect))
                {
                    session.DrawRectangle(SelectionBrush,
                        new Rect(rect.X + shift.X, rect.Y + shift.Y, rect.Width, rect.Height));
                }
            }
        }
    }

    private void TakeSelectionFocus()
    {
        if (!Focusable && GetValueSource(FocusableProperty) == ValuePriority.Default &&
            GetValueSource(KeyboardNavigation.IsTabStopProperty) == ValuePriority.Default)
        {
            SetCurrentValue(FocusableProperty, true);
            SetCurrentValue(KeyboardNavigation.IsTabStopProperty, false);
            _focusableForSelection = true;
        }

        _focusingForSelection = true;
        try
        {
            if (!IsKeyboardFocused && Focus())
            {
                _selectionHoldsFocus = true;
            }
        }
        finally
        {
            _focusingForSelection = false;
        }
    }

    private void GiveUpSelectionFocus()
    {
        if (!_focusableForSelection)
        {
            return;
        }

        _focusableForSelection = false;
        ClearValue(KeyboardNavigation.IsTabStopProperty, ValuePriority.Binding);
        if (_links.Count > 0)
        {
            _madeFocusable = true;
        }
        else
        {
            ClearValue(FocusableProperty, ValuePriority.Binding);
        }
    }

    private Cursor ShownCursor => CanSelect && GetValueSource(CursorProperty) is ValuePriority.Inherited or ValuePriority.Default
        ? Cursors.IBeam
        : Cursor;

    private static void OnTextSelectionEnabledChanged(AdamantiumComponent component, AdamantiumPropertyChangedEventArgs e)
    {
        if (component is not TextBlock block)
        {
            return;
        }

        if (!block.IsTextSelectionEnabled)
        {
            block.ClearSelection();
        }

        if (block.IsPointerOver && !block._overLink)
        {
            Mouse.Cursor = block.ShownCursor;
        }
    }

    protected override void OnPointerEntered(PointerEventArgs e)
    {
        base.OnPointerEntered(e);
        if (e.Pointer.Type != PointerType.Touch)
        {
            Mouse.Cursor = ShownCursor;
        }
    }

    protected override bool ShowsFocusRing => _focusedLink >= 0 || (!_selectionHoldsFocus && !_focusingForSelection);

    public override Rect? FocusBounds
    {
        get
        {
            if (_focusedLink < 0 || _focusedLink >= _links.Count)
            {
                return null;
            }

            var rects = LinkRects(_links[_focusedLink].Link);
            if (rects.Count == 0)
            {
                return null;
            }

            var left = rects.Min(rect => rect.X);
            var top = rects.Min(rect => rect.Y);
            return new Rect(left, top, rects.Max(rect => rect.X + rect.Width) - left,
                rects.Max(rect => rect.Y + rect.Height) - top);
        }
    }

    internal IReadOnlyList<Hyperlink> Links => _links.ConvertAll(link => link.Link);

    internal Hyperlink FocusedLink => _focusedLink >= 0 && _focusedLink < _links.Count ? _links[_focusedLink].Link : null;

    internal void FocusLink(Hyperlink link)
    {
        var index = _links.FindIndex(candidate => ReferenceEquals(candidate.Link, link));
        if (index < 0 || !Focus())
        {
            return;
        }

        _focusedLink = index;
        InvalidateRender(false);
    }

    internal IReadOnlyList<Rect> LinkRects(Hyperlink link)
    {
        foreach (var (start, end, candidate) in _links)
        {
            if (ReferenceEquals(candidate, link))
            {
                return _textLayout.GetRangeRects(start, end).Select(ShownRect).ToList();
            }
        }

        return [];
    }

    internal string LinkText(Hyperlink link)
    {
        foreach (var (start, end, candidate) in _links)
        {
            if (ReferenceEquals(candidate, link) && _lastText is { } text && end <= text.Length)
            {
                return text.Substring(start, end - start).Replace(LineSeparator, ' ')
                    .Replace(ObjectReplacement.ToString(), string.Empty);
            }
        }

        return string.Empty;
    }

    private Hyperlink LinkAt(PointerEventArgs e)
    {
        if (_links.Count == 0)
        {
            return null;
        }

        var point = PointerAt(e);
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

    bool IFocusableInParts.TakesFocusAt(PointerPressedEventArgs e) =>
        LinkAt(e) != null || (IsKeyboardFocused && HoldsAnchor &&
                              (e.Modifiers & (InputModifiers.LeftShift | InputModifiers.RightShift)) != 0);

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        Moved(e);
    }

    private void Moved(PointerEventArgs e)
    {
        var overLink = LinkAt(e) != null;
        if (overLink != _overLink)
        {
            _overLink = overLink;
            Mouse.Cursor = overLink ? Cursors.Of(CursorType.Hand) : ShownCursor;
        }
        else if (!overLink && Shows(e.OriginalSource) && e.Pointer.Type != PointerType.Touch &&
                 !ReferenceEquals(Mouse.Cursor, ShownCursor))
        {
            Mouse.Cursor = ShownCursor;
        }

        if (Drag(e))
        {
            e.Handled = true;
        }
    }

    // The block, or an overflow showing its text.
    private bool Shows(object element) =>
        ReferenceEquals(element, this) || (element is TextBlockOverflow overflow && _overflows.Contains(overflow));

    // The pointer in the block's space: over an overflow, where the block would show that part of its text.
    private Vector2<double> PointerAt(PointerEventArgs e)
    {
        foreach (var overflow in _overflows)
        {
            if (overflow.Frame is not { } frame || !overflow.IsAttachedToVisualTree)
            {
                continue;
            }

            var point = e.GetPosition(overflow);
            if (point.X >= 0 && point.Y >= 0 && point.X < overflow.RenderSize.Width && point.Y < overflow.RenderSize.Height)
            {
                return new Vector2<double>(point.X + frame.X + LinesShift(), point.Y + frame.Y);
            }
        }

        return e.GetPosition(this);
    }

    internal void OverflowPointerEntered(PointerEventArgs e)
    {
        if (e.Pointer.Type != PointerType.Touch)
        {
            Mouse.Cursor = ShownCursor;
        }
    }

    internal void OverflowPointerMoved(PointerEventArgs e) => Moved(e);

    internal void OverflowPointerPressed(PointerPressedEventArgs e) => PressedOnText(e);

    internal void OverflowPointerReleased(PointerReleasedEventArgs e) => ReleasedOnText(e);

    internal void OverflowPointerExited(PointerEventArgs e) => Exited(e);

    private bool Drag(PointerEventArgs e)
    {
        if ((!_selecting && !_pressPending) || !IsAttachedToVisualTree)
        {
            return false;
        }

        if (!e.Properties.IsLeftButtonPressed)
        {
            EndSelecting(e);
            _pressPending = false;
            return false;
        }

        var point = PointerAt(e);
        if (_selecting)
        {
            Select(_selectionAnchor, PositionAt(point.X, point.Y));
            return true;
        }

        var captured = e.Pointer.Captured;
        if (captured != null && !ReferenceEquals(captured, this))
        {
            _pressPending = false;
            return false;
        }

        if (!PlatformSettings.ExceedsDragThreshold(point - _pressAt, this))
        {
            return false;
        }

        var anchor = _pressExtends && HoldsAnchor ? _selectionAnchor : PositionAt(_pressAt.X, _pressAt.Y);
        _pressPending = false;
        _selecting = true;
        e.Pointer.Capture(this);
        Mouse.Cursor = ShownCursor;
        TakeSelectionFocus();
        Select(anchor, PositionAt(point.X, point.Y));
        return true;
    }

    private void Pressed(PointerPressedEventArgs e)
    {
        _pressPending = CanSelect && e.Pointer.Type != PointerType.Touch && IsAttachedToVisualTree;
        if (!_pressPending)
        {
            return;
        }

        _pressAt = PointerAt(e);
        _pressClicks = e.ClickCount;
        _pressExtends = (e.Modifiers & (InputModifiers.LeftShift | InputModifiers.RightShift)) != 0;
    }

    private void EndSelecting(PointerEventArgs e)
    {
        if (!_selecting)
        {
            return;
        }

        _selecting = false;
        if (ReferenceEquals(e.Pointer.Captured, this))
        {
            e.Pointer.Capture(null);
        }
    }

    private void Released(PointerReleasedEventArgs e)
    {
        if (_selecting)
        {
            EndSelecting(e);
            e.Handled = true;
            return;
        }

        if (!_pressPending)
        {
            return;
        }

        _pressPending = false;
        var at = PositionAt(_pressAt.X, _pressAt.Y);
        var size = _lastText?.Length ?? 0;

        if (_pressClicks >= 3)
        {
            TakeSelectionFocus();
            SelectAll();
            e.Handled = true;
        }
        else if (_pressClicks == 2 && size > 0)
        {
            var (start, end) = _textLayout.GetWordAt(Math.Min(at.Index, size - 1));
            TakeSelectionFocus();
            SelectLogical(start, end);
            e.Handled = true;
        }
        else if (_pressExtends && HoldsAnchor)
        {
            Select(_selectionAnchor, at);
            e.Handled = true;
        }
        else if (IsKeyboardFocused)
        {
            Select(at, at);
        }
        else
        {
            ClearSelection();
        }
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        Exited(e);
    }

    private void Exited(PointerEventArgs e)
    {
        _overLink = false;
        _pressedLink = null;
        if (_pressPending)
        {
            Drag(e);
            _pressPending = false;
        }
    }

    protected override void OnPointerCaptureLost(PointerEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        _selecting = false;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        PressedOnText(e);
    }

    private void PressedOnText(PointerPressedEventArgs e)
    {
        if (e.ChangedButton != MouseButtons.Left)
        {
            return;
        }

        _pressedLink = LinkAt(e);
        if (_pressedLink == null)
        {
            Pressed(e);
            return;
        }

        e.Handled = true;
        var index = _links.FindIndex(candidate => ReferenceEquals(candidate.Link, _pressedLink));
        if (IsKeyboardFocused && index != _focusedLink)
        {
            _focusedLink = index;
            InvalidateRender(false);
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        ReleasedOnText(e);
    }

    private void ReleasedOnText(PointerReleasedEventArgs e)
    {
        if (e.ChangedButton != MouseButtons.Left)
        {
            return;
        }

        var link = LinkAt(e);
        var pressed = _pressedLink;
        _pressedLink = null;
        if (link != null && ReferenceEquals(link, pressed))
        {
            e.Handled = true;
            link.Activate();
            return;
        }

        Released(e);
    }

    private bool SelectionKey(Key key, InputModifiers modifiers)
    {
        if (!CanSelect)
        {
            return false;
        }

        var ctrl = (modifiers & (InputModifiers.LeftControl | InputModifiers.RightControl)) != 0;
        var shift = (modifiers & (InputModifiers.LeftShift | InputModifiers.RightShift)) != 0;
        if ((modifiers & (InputModifiers.LeftAlt | InputModifiers.RightAlt)) != 0)
        {
            return false;
        }

        if (!HoldsAnchor)
        {
            return false;
        }

        // Down vertical lines the arrows turn with the text: up and down go along a line, left to the next line.
        var vertical = WritingMode == WritingMode.VerticalRightToLeft;
        var (back, forth) = vertical ? (Key.UpArrow, Key.DownArrow) : (Key.LeftArrow, Key.RightArrow);
        var (before, after) = vertical ? (Key.RightArrow, Key.LeftArrow) : (Key.UpArrow, Key.DownArrow);
        if (!shift && !ctrl && (key == back || key == forth) && HasSelection)
        {
            var (anchorX, anchorLine) = _textLayout.GetCaretPoint(_selectionAnchor);
            var (focusX, focusLine) = _textLayout.GetCaretPoint(_selectionFocus);
            var anchorFirst = anchorLine == focusLine ? anchorX <= focusX : anchorLine < focusLine;
            var start = anchorFirst ? _selectionAnchor : _selectionFocus;
            var end = anchorFirst ? _selectionFocus : _selectionAnchor;
            var edge = key == back ? start : end;
            Select(edge, edge);
            return true;
        }

        var size = _lastText?.Length ?? 0;
        var focus = _selectionFocus;
        if ((key == back || key == forth) && ctrl)
        {
            focus = _textLayout.MoveWordVisually(focus, key == forth);
        }
        else if (key == back || key == forth)
        {
            focus = _textLayout.MoveVisually(focus, key == forth);
        }
        else if (key == Key.Home)
        {
            focus = ctrl ? new CaretPosition(0) : _textLayout.GetLineStart(_textLayout.GetCaretPoint(focus).LineIndex);
        }
        else if (key == Key.End)
        {
            focus = ctrl ? new CaretPosition(size) : _textLayout.GetLineEnd(_textLayout.GetCaretPoint(focus).LineIndex);
        }
        else if (key == before || key == after)
        {
            focus = LineAway(focus, key == after ? 1 : -1);
        }
        else
        {
            return false;
        }

        Select(shift ? _selectionAnchor : focus, focus);
        return true;
    }

    private CaretPosition LineAway(CaretPosition from, int step)
    {
        var (x, line) = _textLayout.GetCaretPoint(from);
        var target = _textLayout.GetLineBeyond(line, x, step > 0);
        if (target < 0)
        {
            return step > 0 ? new CaretPosition(_lastText?.Length ?? 0) : new CaretPosition(0);
        }

        return Snapped(_textLayout.HitTestLine(target, x));
    }

    protected override void OnGotFocus(RoutedEventArgs e)
    {
        base.OnGotFocus(e);
        if (_links.Count > 0 && ReferenceEquals(e.OriginalSource, this) && !_focusingForSelection &&
            !_selectionHoldsFocus)
        {
            var backwards = (FocusManager.ModifiersOf(e) & (InputModifiers.LeftShift | InputModifiers.RightShift)) != 0;
            _focusedLink = backwards ? _links.Count - 1 : 0;
            InvalidateRender(false);
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _inlineBrushes.Take();
        _effectBrushes.Take();
        if (_selectedIn != null)
        {
            ListenToTheWindow();
        }

        _grid.Follow(attached: true);
        WatchHostedBaselines(attached: true);
        // Moved under other resources, its styles and its inlines' answer from there.
        foreach (var inline in AllInlines(_inlines))
        {
            ((Adamantium.UI.Core.Resources.IResolvesOwnResources)inline).ResolveResources();
        }

        ((Adamantium.UI.Core.Resources.IResolvesOwnResources)this).ResolveResources();
    }

    private static IEnumerable<Inline> AllInlines(InlineCollection inlines)
    {
        foreach (var inline in inlines ?? [])
        {
            yield return inline;
            if (inline is Span span)
            {
                foreach (var nested in AllInlines(span.Inlines))
                {
                    yield return nested;
                }
            }
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        foreach (var overflow in _overflows)
        {
            overflow.Leave(this);
        }

        _overflows = [];
        _hasLayout = false;
        _inlineBrushes.Released();
        _effectBrushes.Released();
        ClearSelection();
        _selectionHoldsFocus = false;
        GiveUpSelectionFocus();
        _grid.Follow(attached: false);
        WatchHostedBaselines(attached: false);
    }

    protected override void OnLostFocus(RoutedEventArgs e)
    {
        base.OnLostFocus(e);
        if (!ReferenceEquals(e.OriginalSource, this))
        {
            return;
        }

        _focusedLink = -1;
        InvalidateRender(false);
        if (FocusManager.IsLeavingWindow)
        {
            return;
        }

        _selectionHoldsFocus = false;
        GiveUpSelectionFocus();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (!e.Handled && ReferenceEquals(e.OriginalSource, this) && SelectionKey(e.Key, e.Modifiers))
        {
            e.Handled = true;
            return;
        }

        const InputModifiers shift = InputModifiers.LeftShift | InputModifiers.RightShift;
        const InputModifiers others = InputModifiers.LeftControl | InputModifiers.RightControl | InputModifiers.LeftAlt |
                                      InputModifiers.RightAlt;
        if (!e.Handled && ReferenceEquals(e.OriginalSource, this) && e.Key == Key.Tab && _focusedLink < 0 &&
            _links.Count > 0 && _selectionHoldsFocus && (e.Modifiers & others) == 0)
        {
            _focusedLink = (e.Modifiers & shift) != 0 ? _links.Count - 1 : 0;
            if (!FocusManager.Reveal())
            {
                RaiseEvent(new RoutedEventArgs(FocusManager.FocusRevealedEvent));
            }

            InvalidateRender(false);
            e.Handled = true;
            return;
        }

        if (_focusedLink < 0 || _focusedLink >= _links.Count || e.Handled || !ReferenceEquals(e.OriginalSource, this))
        {
            return;
        }

        var modifiers = e.Modifiers;
        if ((modifiers & others) != 0)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Tab:
                var step = (modifiers & shift) != 0 ? -1 : 1;
                if (_focusedLink + step >= 0 && _focusedLink + step < _links.Count)
                {
                    _focusedLink += step;
                    InvalidateRender(false);
                    e.Handled = true;
                }

                break;
            case Key.Enter or Key.Space when !e.IsRepeated && (modifiers & shift) == 0:
                e.Handled = true;
                _links[_focusedLink].Link.Activate();
                break;
        }
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new TextBlockAutomationPeer(this);
}