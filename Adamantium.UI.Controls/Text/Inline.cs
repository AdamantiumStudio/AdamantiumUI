using System;
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
public abstract class Inline : FundamentalUIComponent
{
    public static readonly AdamantiumProperty ForegroundProperty = AdamantiumProperty.Register(nameof(Foreground),
        typeof(Brush), typeof(Inline), new PropertyMetadata(null, PropertyMetadataOptions.AffectsMeasure));

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

    public static readonly AdamantiumProperty BaselineShiftProperty = AdamantiumProperty.Register(nameof(BaselineShift),
        typeof(double?), typeof(Inline), new PropertyMetadata(null, PropertyMetadataOptions.AffectsMeasure));

    /// <summary>Raised when a property that affects this inline's rendered text changes (so the TextBlock re-lays-out).</summary>
    internal event EventHandler Changed;

    /// <summary>This inline's text color; null takes the enclosing span's, then the TextBlock's <c>Foreground</c>.</summary>
    public Brush Foreground
    {
        get => GetValue<Brush>(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
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

    /// <summary>Space added after each character of this inline, in thousandths of an em; null takes the enclosing
    /// span's, then the TextBlock's <c>Tracking</c>.</summary>
    public double? Tracking
    {
        get => GetValue<double?>(TrackingProperty);
        set => SetValue(TrackingProperty, value);
    }

    /// <summary>How far this inline is raised above the line's baseline, as InDesign's baseline shift; negative lowers
    /// it. Its size and the line's height stay as they are. Null takes the enclosing span's shift, or none.</summary>
    public double? BaselineShift
    {
        get => GetValue<double?>(BaselineShiftProperty);
        set => SetValue(BaselineShiftProperty, value);
    }

    protected void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);

    protected override void OnPropertyChanged(AdamantiumPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.Property.GetDefaultMetadata(GetType())?.AffectsMeasure == true)
        {
            RaiseChanged();
        }
    }
}
