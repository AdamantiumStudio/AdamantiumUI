using Adamantium.Graphics.Fonts;
using Adamantium.UI.Core;

namespace Adamantium.UI.Controls.Text;

/// <summary>A <see cref="Span"/> with a ruby annotation, as HTML's &lt;ruby&gt;: small text centered over its
/// inlines - furigana over kanji, a reading, a gloss. Inlines narrower than the annotation are spread to its width and
/// stay on one line; the line grows to hold it unless its height is set. A ruby inside a ruby on the other side of the
/// base (<see cref="RubyPosition"/>) is a double-sided ruby: both annotations are shown. Of rubies one inside another on
/// the same side, the outer one's annotation is the one shown.</summary>
public class Ruby : Span
{
    public static readonly AdamantiumProperty AnnotationProperty = AdamantiumProperty.Register(nameof(Annotation),
        typeof(string), typeof(Ruby), new PropertyMetadata(null, PropertyMetadataOptions.AffectsMeasure));

    public static readonly AdamantiumProperty RubyPositionProperty = AdamantiumProperty.Register(nameof(RubyPosition),
        typeof(RubyPosition), typeof(Ruby),
        new PropertyMetadata(RubyPosition.Over, PropertyMetadataOptions.AffectsMeasure));

    public static readonly AdamantiumProperty AnnotationFontSizeProperty = AdamantiumProperty.Register(
        nameof(AnnotationFontSize), typeof(double), typeof(Ruby),
        new PropertyMetadata(double.NaN, PropertyMetadataOptions.AffectsMeasure));

    /// <summary>What the annotation says; none, null or empty.</summary>
    public string Annotation
    {
        get => GetValue<string>(AnnotationProperty);
        set => SetValue(AnnotationProperty, value);
    }

    /// <summary>Which side of its inlines the annotation stands on: over them (the default) or under the line.</summary>
    public RubyPosition RubyPosition
    {
        get => GetValue<RubyPosition>(RubyPositionProperty);
        set => SetValue(RubyPositionProperty, value);
    }

    /// <summary>The annotation's size; NaN (the default) is half the size of its inlines.</summary>
    public double AnnotationFontSize
    {
        get => GetValue<double>(AnnotationFontSizeProperty);
        set => SetValue(AnnotationFontSizeProperty, value);
    }
}
