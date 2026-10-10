using Adamantium.Fonts;
using Adamantium.Graphics.Fonts;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Media;
using Adamantium.UI.Core.RoutedEvents;

namespace Adamantium.UI.Controls.Text;

/// <summary>
/// A run of text with its own optional color, size, background, lines, features and language inside a
/// <see cref="TextBlock"/>, whose runs lay out as one text and wrap across each other. Every property is a bindable
/// <see cref="AdamantiumProperty"/> and the run inherits the TextBlock's DataContext, so <c>Text</c> / <c>Foreground</c>
/// bind straight to the view-model (<c>&lt;Run Text="{Binding Name}" Foreground="{Binding Color}"/&gt;</c>). An unset
/// <see cref="Foreground"/> / <see cref="FontSize"/> falls back to the owning TextBlock's.
/// </summary>
public class Run : Inline
{
    public static readonly AdamantiumProperty TextProperty = AdamantiumProperty.Register(nameof(Text),
        typeof(string), typeof(Run), new PropertyMetadata(string.Empty, OnRunPropertyChanged));

    public static readonly AdamantiumProperty ForegroundProperty = AdamantiumProperty.Register(nameof(Foreground),
        typeof(Brush), typeof(Run), new PropertyMetadata(null, OnRunPropertyChanged));

    // NaN = "inherit the TextBlock's FontSize".
    public static readonly AdamantiumProperty FontSizeProperty = AdamantiumProperty.Register(nameof(FontSize),
        typeof(double), typeof(Run), new PropertyMetadata(double.NaN, OnRunPropertyChanged));

    public static readonly AdamantiumProperty BackgroundProperty = AdamantiumProperty.Register(nameof(Background),
        typeof(Brush), typeof(Run), new PropertyMetadata(null, OnRunPropertyChanged));

    public static readonly AdamantiumProperty TextDecorationsProperty = AdamantiumProperty.Register(
        nameof(TextDecorations), typeof(TextDecorations), typeof(Run),
        new PropertyMetadata(TextDecorations.None, OnRunPropertyChanged));

    public static readonly AdamantiumProperty FontWeightProperty = AdamantiumProperty.Register(nameof(FontWeight),
        typeof(FontWeight?), typeof(Run), new PropertyMetadata(null, OnRunPropertyChanged));

    public static readonly AdamantiumProperty FontStyleProperty = AdamantiumProperty.Register(nameof(FontStyle),
        typeof(FontStyle?), typeof(Run), new PropertyMetadata(null, OnRunPropertyChanged));

    public static readonly AdamantiumProperty FontStretchProperty = AdamantiumProperty.Register(nameof(FontStretch),
        typeof(FontStretch?), typeof(Run), new PropertyMetadata(null, OnRunPropertyChanged));

    public static readonly AdamantiumProperty FontSynthesisProperty = AdamantiumProperty.Register(nameof(FontSynthesis),
        typeof(FontSynthesis?), typeof(Run), new PropertyMetadata(null, OnRunPropertyChanged));

    public static readonly AdamantiumProperty FontFeaturesProperty = AdamantiumProperty.Register(nameof(FontFeatures),
        typeof(FontFeatureList), typeof(Run), new PropertyMetadata(null, OnRunPropertyChanged));

    public static readonly AdamantiumProperty FontVariationsProperty = AdamantiumProperty.Register(nameof(FontVariations),
        typeof(FontVariationList), typeof(Run), new PropertyMetadata(null, OnRunPropertyChanged));

    public static readonly AdamantiumProperty LanguageProperty = AdamantiumProperty.Register(nameof(Language),
        typeof(string), typeof(Run), new PropertyMetadata(null, OnRunPropertyChanged));

    public static readonly AdamantiumProperty ColorPaletteProperty = AdamantiumProperty.Register(nameof(ColorPalette),
        typeof(int?), typeof(Run), new PropertyMetadata(null, OnRunPropertyChanged));

    public static readonly AdamantiumProperty TrackingProperty = AdamantiumProperty.Register(nameof(Tracking),
        typeof(double?), typeof(Run), new PropertyMetadata(null, OnRunPropertyChanged));

    public static readonly AdamantiumProperty BaselineShiftProperty = AdamantiumProperty.Register(nameof(BaselineShift),
        typeof(double?), typeof(Run), new PropertyMetadata(null, OnRunPropertyChanged));

    public string Text
    {
        get => GetValue<string>(TextProperty);
        set => SetValue(TextProperty, value);
    }

    /// <summary>This run's text color; null inherits the TextBlock's <c>Foreground</c>.</summary>
    public Brush Foreground
    {
        get => GetValue<Brush>(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    /// <summary>This run's font size; NaN (default) inherits the TextBlock's <c>FontSize</c>.</summary>
    public double FontSize
    {
        get => GetValue<double>(FontSizeProperty);
        set => SetValue(FontSizeProperty, value);
    }

    /// <summary>A fill behind this run's text, as high as the line; null draws none.</summary>
    public Brush Background
    {
        get => GetValue<Brush>(BackgroundProperty);
        set => SetValue(BackgroundProperty, value);
    }

    /// <summary>Lines drawn with this run: <c>Underline</c>, <c>Strikethrough</c>, <c>Squiggle</c>, in its color.</summary>
    public TextDecorations TextDecorations
    {
        get => GetValue<TextDecorations>(TextDecorationsProperty);
        set => SetValue(TextDecorationsProperty, value);
    }

    /// <summary>How heavy this run is; null takes the TextBlock's weight.</summary>
    public FontWeight? FontWeight
    {
        get => GetValue<FontWeight?>(FontWeightProperty);
        set => SetValue(FontWeightProperty, value);
    }

    /// <summary>Upright, italic or oblique; null takes the TextBlock's.</summary>
    public FontStyle? FontStyle
    {
        get => GetValue<FontStyle?>(FontStyleProperty);
        set => SetValue(FontStyleProperty, value);
    }

    /// <summary>How wide this run is; null takes the TextBlock's.</summary>
    public FontStretch? FontStretch
    {
        get => GetValue<FontStretch?>(FontStretchProperty);
        set => SetValue(FontStretchProperty, value);
    }

    /// <summary>What may be drawn for a bold or italic face the family lacks; null takes the TextBlock's.</summary>
    public FontSynthesis? FontSynthesis
    {
        get => GetValue<FontSynthesis?>(FontSynthesisProperty);
        set => SetValue(FontSynthesisProperty, value);
    }

    /// <summary>OpenType features for this run; null takes the TextBlock's.</summary>
    public FontFeatureList FontFeatures
    {
        get => GetValue<FontFeatureList>(FontFeaturesProperty);
        set => SetValue(FontFeaturesProperty, value);
    }

    /// <summary>Axis values of a variable font for this run; null takes the TextBlock's.</summary>
    public FontVariationList FontVariations
    {
        get => GetValue<FontVariationList>(FontVariationsProperty);
        set => SetValue(FontVariationsProperty, value);
    }

    /// <summary>The language of this run (BCP 47); null takes the TextBlock's.</summary>
    public string Language
    {
        get => GetValue<string>(LanguageProperty);
        set => SetValue(LanguageProperty, value);
    }

    /// <summary>The palette this run's color glyphs are drawn in; null takes the TextBlock's.</summary>
    public int? ColorPalette
    {
        get => GetValue<int?>(ColorPaletteProperty);
        set => SetValue(ColorPaletteProperty, value);
    }

    /// <summary>Space added after each character of this run, in thousandths of an em; null takes the TextBlock's
    /// <c>Tracking</c>.</summary>
    public double? Tracking
    {
        get => GetValue<double?>(TrackingProperty);
        set => SetValue(TrackingProperty, value);
    }

    /// <summary>How far this run is raised above the line's baseline, as InDesign's baseline shift; negative lowers
    /// it. Its size and the line's height stay as they are. Null leaves it on the baseline.</summary>
    public double? BaselineShift
    {
        get => GetValue<double?>(BaselineShiftProperty);
        set => SetValue(BaselineShiftProperty, value);
    }

    private static void OnRunPropertyChanged(AdamantiumComponent a, AdamantiumPropertyChangedEventArgs e)
        => (a as Run)?.RaiseChanged();
}
