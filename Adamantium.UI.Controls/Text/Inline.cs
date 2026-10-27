using System;
using System.Collections.Specialized;
using Adamantium.Core.Fonts;
using Adamantium.Fonts;
using Adamantium.Graphics.Fonts;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Media;
using Adamantium.UI.Core.RoutedEvents;

namespace Adamantium.UI.Controls.Text;

/// <summary>
/// Base for a piece of inline content inside a <see cref="TextBlock"/>: a <see cref="Run"/> of text, a
/// <see cref="Span"/> grouping other inlines, a <see cref="LineBreak"/>. An inline is a <see cref="FundamentalUIComponent"/>,
/// so it lives in the logical tree and INHERITS the TextBlock's DataContext - which is exactly why its properties are
/// bindable (<c>&lt;Run Text="{Binding ...}"/&gt;</c>). Its formatting is optional: what it leaves unset comes from the
/// span around it, and in the end from the TextBlock. The hosting TextBlock listens to <see cref="Changed"/> to re-shape
/// when a bound value updates.
/// </summary>
public abstract class Inline : FundamentalUIComponent, ITextStyleUser, Adamantium.UI.Core.Resources.IResolvesOwnResources
{
    public static readonly AdamantiumProperty CharacterStyleProperty = AdamantiumProperty.Register(
        nameof(CharacterStyle), typeof(CharacterStyle), typeof(Inline),
        new PropertyMetadata(null, PropertyMetadataOptions.AffectsMeasure));

    public static readonly AdamantiumProperty ForegroundProperty = AdamantiumProperty.Register(nameof(Foreground),
        typeof(Brush), typeof(Inline), new PropertyMetadata(null, PropertyMetadataOptions.AffectsMeasure));

    public static readonly AdamantiumProperty FontFamilyProperty = AdamantiumProperty.Register(nameof(FontFamily),
        typeof(FontFamily), typeof(Inline), new PropertyMetadata(null, PropertyMetadataOptions.AffectsMeasure));

    // NaN = "inherit the enclosing span's or the TextBlock's FontSize".
    public static readonly AdamantiumProperty FontSizeProperty = AdamantiumProperty.Register(nameof(FontSize),
        typeof(double), typeof(Inline), new PropertyMetadata(double.NaN, PropertyMetadataOptions.AffectsMeasure));

    public static readonly AdamantiumProperty BackgroundProperty = AdamantiumProperty.Register(nameof(Background),
        typeof(Brush), typeof(Inline), new PropertyMetadata(null, PropertyMetadataOptions.AffectsMeasure));

    public static readonly AdamantiumProperty TextDecorationsProperty = AdamantiumProperty.Register(
        nameof(TextDecorations), typeof(TextDecorations), typeof(Inline),
        new PropertyMetadata(TextDecorations.None, PropertyMetadataOptions.AffectsMeasure));

    public static readonly AdamantiumProperty FontWeightProperty = AdamantiumProperty.Register(nameof(FontWeight),
        typeof(FontWeight?), typeof(Inline), new PropertyMetadata(null, PropertyMetadataOptions.AffectsMeasure));

    public static readonly AdamantiumProperty FontStyleProperty = AdamantiumProperty.Register(nameof(FontStyle),
        typeof(FontStyle?), typeof(Inline), new PropertyMetadata(null, PropertyMetadataOptions.AffectsMeasure));

    public static readonly AdamantiumProperty FontStretchProperty = AdamantiumProperty.Register(nameof(FontStretch),
        typeof(FontStretch?), typeof(Inline), new PropertyMetadata(null, PropertyMetadataOptions.AffectsMeasure));

    public static readonly AdamantiumProperty FontSynthesisProperty = AdamantiumProperty.Register(nameof(FontSynthesis),
        typeof(FontSynthesis?), typeof(Inline), new PropertyMetadata(null, PropertyMetadataOptions.AffectsMeasure));

    public static readonly AdamantiumProperty FontFeaturesProperty = AdamantiumProperty.Register(nameof(FontFeatures),
        typeof(FontFeatureList), typeof(Inline), new PropertyMetadata(null, PropertyMetadataOptions.AffectsMeasure));

    public static readonly AdamantiumProperty FontVariationsProperty = AdamantiumProperty.Register(nameof(FontVariations),
        typeof(FontVariationList), typeof(Inline), new PropertyMetadata(null, PropertyMetadataOptions.AffectsMeasure));

    public static readonly AdamantiumProperty LanguageProperty = AdamantiumProperty.Register(nameof(Language),
        typeof(string), typeof(Inline), new PropertyMetadata(null, PropertyMetadataOptions.AffectsMeasure));

    public static readonly AdamantiumProperty ColorPaletteProperty = AdamantiumProperty.Register(nameof(ColorPalette),
        typeof(int?), typeof(Inline), new PropertyMetadata(null, PropertyMetadataOptions.AffectsMeasure));

    public static readonly AdamantiumProperty TrackingProperty = AdamantiumProperty.Register(nameof(Tracking),
        typeof(double?), typeof(Inline), new PropertyMetadata(null, PropertyMetadataOptions.AffectsMeasure));

    public static readonly AdamantiumProperty KerningProperty = AdamantiumProperty.Register(nameof(Kerning),
        typeof(TextKerning?), typeof(Inline), new PropertyMetadata(null, PropertyMetadataOptions.AffectsMeasure));

    public static readonly AdamantiumProperty BaselineShiftProperty = AdamantiumProperty.Register(nameof(BaselineShift),
        typeof(double?), typeof(Inline), new PropertyMetadata(null, PropertyMetadataOptions.AffectsMeasure));

    public static readonly AdamantiumProperty BaselineAlignmentProperty = AdamantiumProperty.Register(
        nameof(BaselineAlignment), typeof(BaselineAlignment?), typeof(Inline),
        new PropertyMetadata(null, PropertyMetadataOptions.AffectsMeasure));

    public static readonly AdamantiumProperty TextOutlineProperty = AdamantiumProperty.Register(nameof(TextOutline),
        typeof(TextOutline), typeof(Inline), new PropertyMetadata(null, PropertyMetadataOptions.AffectsMeasure));

    public static readonly AdamantiumProperty TextShadowProperty = AdamantiumProperty.Register(nameof(TextShadow),
        typeof(TextShadow), typeof(Inline), new PropertyMetadata(null, PropertyMetadataOptions.AffectsMeasure));

    public static readonly AdamantiumProperty TextGlowProperty = AdamantiumProperty.Register(nameof(TextGlow),
        typeof(TextGlow), typeof(Inline), new PropertyMetadata(null, PropertyMetadataOptions.AffectsMeasure));

    public static readonly AdamantiumProperty TextReflectionProperty = AdamantiumProperty.Register(nameof(TextReflection),
        typeof(TextReflection), typeof(Inline), new PropertyMetadata(null, PropertyMetadataOptions.AffectsMeasure));

    private static readonly AdamantiumProperty[] Overrides =
    [
        ForegroundProperty, FontFamilyProperty, FontSizeProperty, BackgroundProperty, TextDecorationsProperty,
        FontWeightProperty, FontStyleProperty, FontStretchProperty, FontSynthesisProperty, FontFeaturesProperty, FontVariationsProperty,
        LanguageProperty, ColorPaletteProperty, TrackingProperty, KerningProperty, BaselineShiftProperty,
        TextOutlineProperty, TextShadowProperty, TextGlowProperty, TextReflectionProperty,
    ];

    private TextDecorationCollection _decorationLines;
    private TextEffectCollection _textEffects;
    private Brush _bakedForeground;
    private Brush _bakedBackground;
    private Brush _bakedStyleForeground;
    private Brush _bakedStyleBackground;
    private Brush _bakedParagraphForeground;
    private Brush _bakedParagraphBackground;
    private bool _inBlock;
    private bool _linesBaked;

    internal static readonly EventArgs EffectsOnly = new();

    // A style the inline takes changed: its text lays out again, the inlines themselves are as they were.
    internal static readonly EventArgs StyleOnly = new();

    /// <summary>Raised when a property that affects this inline's rendered text changes (so the TextBlock re-lays-out).</summary>
    internal event EventHandler Changed;

    /// <summary>This inline's text color; null takes the enclosing span's, then the TextBlock's <c>Foreground</c>.</summary>
    public Brush Foreground
    {
        get => GetValue<Brush>(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    /// <summary>The family of this inline's text; null takes the enclosing span's, then the TextBlock's.</summary>
    public FontFamily FontFamily
    {
        get => GetValue<FontFamily>(FontFamilyProperty);
        set => SetValue(FontFamilyProperty, value);
    }

    /// <summary>This inline's font size; NaN (default) takes the enclosing span's, then the TextBlock's.</summary>
    public double FontSize
    {
        get => GetValue<double>(FontSizeProperty);
        set => SetValue(FontSizeProperty, value);
    }

    /// <summary>A fill behind this inline's text, as high as the line; null takes the enclosing span's.</summary>
    public Brush Background
    {
        get => GetValue<Brush>(BackgroundProperty);
        set => SetValue(BackgroundProperty, value);
    }

    /// <summary>Lines drawn with this inline: <c>Underline</c>, <c>Strikethrough</c>, <c>Squiggle</c>, in its color.
    /// They add to the lines of the spans around it.</summary>
    public TextDecorations TextDecorations
    {
        get => GetValue<TextDecorations>(TextDecorationsProperty);
        set => SetValue(TextDecorationsProperty, value);
    }

    /// <summary>How heavy this inline is; null takes the enclosing span's, then the TextBlock's weight.</summary>
    public FontWeight? FontWeight
    {
        get => GetValue<FontWeight?>(FontWeightProperty);
        set => SetValue(FontWeightProperty, value);
    }

    /// <summary>Upright, italic or oblique; null takes the enclosing span's, then the TextBlock's.</summary>
    public FontStyle? FontStyle
    {
        get => GetValue<FontStyle?>(FontStyleProperty);
        set => SetValue(FontStyleProperty, value);
    }

    /// <summary>How wide this inline is; null takes the enclosing span's, then the TextBlock's.</summary>
    public FontStretch? FontStretch
    {
        get => GetValue<FontStretch?>(FontStretchProperty);
        set => SetValue(FontStretchProperty, value);
    }

    /// <summary>What may be drawn for a bold or italic face the family lacks; null takes the enclosing span's, then
    /// the TextBlock's.</summary>
    public FontSynthesis? FontSynthesis
    {
        get => GetValue<FontSynthesis?>(FontSynthesisProperty);
        set => SetValue(FontSynthesisProperty, value);
    }

    /// <summary>OpenType features for this inline; null takes the enclosing span's, then the TextBlock's.</summary>
    public FontFeatureList FontFeatures
    {
        get => GetValue<FontFeatureList>(FontFeaturesProperty);
        set => SetValue(FontFeaturesProperty, value);
    }

    /// <summary>Axis values of a variable font for this inline; null takes the enclosing span's, then the
    /// TextBlock's.</summary>
    public FontVariationList FontVariations
    {
        get => GetValue<FontVariationList>(FontVariationsProperty);
        set => SetValue(FontVariationsProperty, value);
    }

    /// <summary>The language of this inline (BCP 47); null takes the enclosing span's, then the TextBlock's.</summary>
    public string Language
    {
        get => GetValue<string>(LanguageProperty);
        set => SetValue(LanguageProperty, value);
    }

    /// <summary>The palette this inline's color glyphs are drawn in; null takes the enclosing span's, then the
    /// TextBlock's.</summary>
    public int? ColorPalette
    {
        get => GetValue<int?>(ColorPaletteProperty);
        set => SetValue(ColorPaletteProperty, value);
    }

    /// <summary>A line round each letter of this inline; null takes the enclosing span's, then the TextBlock's.</summary>
    public TextOutline TextOutline
    {
        get => GetValue<TextOutline>(TextOutlineProperty);
        set => SetValue(TextOutlineProperty, value);
    }

    /// <summary>A shadow of this inline's letters; null takes the enclosing span's, then the TextBlock's.</summary>
    public TextShadow TextShadow
    {
        get => GetValue<TextShadow>(TextShadowProperty);
        set => SetValue(TextShadowProperty, value);
    }

    /// <summary>A glow round this inline's letters; null takes the enclosing span's, then the TextBlock's.</summary>
    public TextGlow TextGlow
    {
        get => GetValue<TextGlow>(TextGlowProperty);
        set => SetValue(TextGlowProperty, value);
    }

    /// <summary>A reflection of this inline's letters; null takes the enclosing span's, then the TextBlock's.</summary>
    public TextReflection TextReflection
    {
        get => GetValue<TextReflection>(TextReflectionProperty);
        set => SetValue(TextReflectionProperty, value);
    }

    /// <summary>Space added after each character of this inline, in thousandths of an em; null takes the enclosing
    /// span's, then the TextBlock's <c>Tracking</c>.</summary>
    public double? Tracking
    {
        get => GetValue<double?>(TrackingProperty);
        set => SetValue(TrackingProperty, value);
    }

    /// <summary>Where the space between two letters of this inline comes from; null takes the enclosing span's, then the
    /// TextBlock's <c>Kerning</c>.</summary>
    public TextKerning? Kerning
    {
        get => GetValue<TextKerning?>(KerningProperty);
        set => SetValue(KerningProperty, value);
    }

    /// <summary>The named character style its text takes, over its paragraph's style and under what this inline and
    /// the spans around it set themselves; an inner inline's style wins over an outer one's. Null sets none.</summary>
    public CharacterStyle CharacterStyle
    {
        get => GetValue<CharacterStyle>(CharacterStyleProperty);
        set => SetValue(CharacterStyleProperty, value);
    }

    void ITextStyleUser.TextStyleChanged(bool rebased)
    {
        Rebake();
        RaiseChanged(StyleOnly);
    }

    /// <summary>How far this inline is raised above the line's baseline, as InDesign's baseline shift; negative lowers
    /// it. Its size and the line's height stay as they are. Null takes the enclosing span's shift, or none.</summary>
    public double? BaselineShift
    {
        get => GetValue<double?>(BaselineShiftProperty);
        set => SetValue(BaselineShiftProperty, value);
    }

    /// <summary>Where this inline stands across its line, as WPF's: on the baseline, at the top, middle or bottom of the
    /// line, at the block font's ascender or descender, or where the font sets a superscript or a subscript. Its size
    /// and the line's height stay as they are; <see cref="BaselineShift"/> adds to it. Null takes the enclosing span's,
    /// or the baseline.</summary>
    public BaselineAlignment? BaselineAlignment
    {
        get => GetValue<BaselineAlignment?>(BaselineAlignmentProperty);
        set => SetValue(BaselineAlignmentProperty, value);
    }

    /// <summary>Lines drawn with this inline, each with its own place, brush, thickness, dashes and offset, as WPF's text
    /// decorations; they add to the lines of the spans around it and to <see cref="TextDecorations"/>.</summary>
    public TextDecorationCollection DecorationLines
    {
        get
        {
            if (_decorationLines == null)
            {
                _decorationLines = [];
                _decorationLines.CollectionChanged += OnDecorationLinesChanged;
            }

            return _decorationLines;
        }
    }

    internal bool HasDecorationLines => _decorationLines is { Count: > 0 };

    /// <summary>Takes away the character formatting set on this inline and on the inlines inside it, as InDesign's Clear
    /// Overrides: the text keeps its styles and what it inherits; a paragraph keeps its own alignment, margins and indent. Bindings on those properties go with their values; an inline
    /// object keeps its <see cref="BaselineAlignment"/>, which places it rather than formats it.</summary>
    public void ClearOverrides()
    {
        foreach (var property in Overrides)
        {
            ClearValue(property);
        }

        if (this is not InlineUIContainer)
        {
            ClearValue(BaselineAlignmentProperty);
        }

        _decorationLines?.Clear();
        if (this is Span span)
        {
            foreach (var inline in span.Inlines)
            {
                inline.ClearOverrides();
            }
        }
    }

    /// <summary>Ranges of this inline drawn apart from the rest, as WPF's text effects; their positions count from the
    /// start of this inline's own text. Only the drawing changes, so changing them draws the text again without laying
    /// it out.</summary>
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

    internal bool HasTextEffects => _textEffects is { Count: > 0 };

    internal virtual void ReparentEffects()
    {
        if (!HasTextEffects)
        {
            return;
        }

        foreach (var effect in _textEffects)
        {
            effect.ReparentTransform();
        }
    }

    protected void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);

    private protected void RaiseChanged(EventArgs e) => Changed?.Invoke(this, e);

    private void OnTextEffectsChanged(object sender, EventArgs e) => RaiseChanged(EffectsOnly);

    private void OnDecorationLinesChanged(object sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems != null)
        {
            foreach (TextDecoration line in e.OldItems)
            {
                line.Changed -= OnDecorationLineChanged;
                if (_linesBaked)
                {
                    line.Bake(false);
                }
            }
        }

        if (e.NewItems != null)
        {
            foreach (TextDecoration line in e.NewItems)
            {
                line.Changed += OnDecorationLineChanged;
                if (_linesBaked)
                {
                    line.Bake(true);
                }
            }
        }

        RaiseChanged();
    }

    private void OnDecorationLineChanged(object sender, EventArgs e) => RaiseChanged();

    internal bool InBlock => _inBlock;

    // An inline takes no part in the visual tree: what a {ResourceReference} on it asks is answered from its block's place
    // there, each time it joins a live tree. Its styles are shared and keep no answer: they are read as the block sees
    // them, so a theme scope switched round the block lays its text out again.
    void Adamantium.UI.Core.Resources.IResolvesOwnResources.ResolveResources()
    {
        ResolveResources();
        if (CharacterStyle != null || (this as Paragraph)?.ParagraphStyle != null)
        {
            Rebake();
            RaiseChanged(StyleOnly);
        }
    }

    private void ResolveResources()
    {
        if (Block() is { } block)
        {
            Adamantium.UI.Core.Resources.ResourceResolver.ResolveFrom(this, block);
        }
    }

    protected override void OnAttachedToLogicalTree(LogicalTreeAttachmentEventArgs e)
    {
        base.OnAttachedToLogicalTree(e);
        ResolveResources();
    }

    internal bool ReachesABlock() => Block() != null;

    private TextBlock Block()
    {
        for (var node = LogicalParent; node != null; node = node.LogicalParent)
        {
            if (node is TextBlock block)
            {
                return block;
            }
        }

        return null;
    }

    internal virtual void SetInBlock(bool inBlock)
    {
        _inBlock = inBlock;
        Rebake();
        if (_textEffects == null)
        {
            return;
        }

        foreach (var effect in _textEffects)
        {
            effect.SetInBlock(inBlock);
        }
    }

    protected override void OnDiscarded()
    {
        base.OnDiscarded();
        _inBlock = false;
        Rebake();
    }

    private void Rebake()
    {
        var baking = _inBlock && !IsDiscarded;
        _bakedForeground = Bake(_bakedForeground, baking ? Foreground : null);
        _bakedBackground = Bake(_bakedBackground, baking ? Background : null);
        // The brushes of its styles, as its block sees them, are drawn into its text as its own are.
        var paragraphStyle = (this as Paragraph)?.ParagraphStyle;
        var scope = baking && (CharacterStyle != null || paragraphStyle != null) ? Block() : null;
        _bakedStyleForeground = Bake(_bakedStyleForeground, baking ? CharacterStyle?.ResolvedForeground(scope) : null);
        _bakedStyleBackground = Bake(_bakedStyleBackground, baking ? CharacterStyle?.ResolvedBackground(scope) : null);
        _bakedParagraphForeground = Bake(_bakedParagraphForeground, baking ? paragraphStyle?.ResolvedForeground(scope) : null);
        _bakedParagraphBackground = Bake(_bakedParagraphBackground, baking ? paragraphStyle?.ResolvedBackground(scope) : null);
        if (_linesBaked == baking)
        {
            return;
        }

        _linesBaked = baking;
        if (_decorationLines == null)
        {
            return;
        }

        foreach (var line in _decorationLines)
        {
            line.Bake(baking);
        }
    }

    private static Brush Bake(Brush baked, Brush brush)
    {
        if (ReferenceEquals(baked, brush))
        {
            return baked;
        }

        baked?.ReleaseBaked();
        brush?.HoldBaked();
        return brush;
    }

    protected override void OnPropertyChanged(AdamantiumPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.Property == ForegroundProperty || e.Property == BackgroundProperty)
        {
            Rebake();
        }

        if (e.Property == CharacterStyleProperty || e.Property == Paragraph.ParagraphStyleProperty)
        {
            (e.OldValue as TextFormat)?.Unuse(this);
            (e.NewValue as TextFormat)?.Use(this);
            Rebake();
        }

        if (e.Property == TextOutlineProperty || e.Property == TextShadowProperty || e.Property == TextGlowProperty ||
            e.Property == TextReflectionProperty)
        {
            Watch(e.Property, e.NewValue);
        }

        if (e.Property.GetDefaultMetadata(GetType())?.AffectsMeasure == true)
        {
            RaiseChanged();
        }
    }

    private void Watch(AdamantiumProperty property, object look)
    {
        EventHandler changed = OnGlyphLookChanged;
        switch (look)
        {
            case TextOutline outline:
                outline.Changed += changed;
                Follow(property, () => outline.Changed -= changed);
                break;
            case TextShadow shadow:
                shadow.Changed += changed;
                Follow(property, () => shadow.Changed -= changed);
                break;
            case TextGlow glow:
                glow.Changed += changed;
                Follow(property, () => glow.Changed -= changed);
                break;
            case TextReflection reflection:
                reflection.Changed += changed;
                Follow(property, () => reflection.Changed -= changed);
                break;
            default:
                Follow(property, null);
                break;
        }
    }

    private void OnGlyphLookChanged(object sender, EventArgs e) => RaiseChanged();
}
