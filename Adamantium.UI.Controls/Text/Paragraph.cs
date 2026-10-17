using Adamantium.Graphics.Fonts;
using Adamantium.Mathematics;
using Adamantium.UI.Core;

namespace Adamantium.UI.Controls.Text;

/// <summary>A paragraph of a <see cref="TextBlock"/>, as HTML's <c>p</c>: it starts on a line of its own, and the
/// inlines outside paragraphs stand as paragraphs of their own between them. It stands only directly in the
/// TextBlock's inlines, never inside a span. What it sets about its lines replaces the block's for them; its inline
/// formatting goes to the inlines in it as a span's does.</summary>
[TrimSurroundingWhitespace]
public class Paragraph : Span
{
    public static readonly AdamantiumProperty HorizontalTextAlignmentProperty = AdamantiumProperty.Register(
        nameof(HorizontalTextAlignment), typeof(HorizontalTextAlignment?), typeof(Paragraph),
        new PropertyMetadata(null, PropertyMetadataOptions.AffectsMeasure));

    public static readonly AdamantiumProperty MarginProperty = AdamantiumProperty.Register(nameof(Margin),
        typeof(Thickness), typeof(Paragraph), new PropertyMetadata(default(Thickness), PropertyMetadataOptions.AffectsMeasure));

    public static readonly AdamantiumProperty TextIndentProperty = AdamantiumProperty.Register(nameof(TextIndent),
        typeof(double), typeof(Paragraph), new PropertyMetadata(0d, PropertyMetadataOptions.AffectsMeasure));

    public static readonly AdamantiumProperty ParagraphStyleProperty = AdamantiumProperty.Register(
        nameof(ParagraphStyle), typeof(ParagraphStyle), typeof(Paragraph),
        new PropertyMetadata(null, PropertyMetadataOptions.AffectsMeasure));

    // NaN = "the TextBlock's LineHeight".
    public static readonly AdamantiumProperty LineHeightProperty = AdamantiumProperty.Register(nameof(LineHeight),
        typeof(double), typeof(Paragraph), new PropertyMetadata(double.NaN, PropertyMetadataOptions.AffectsMeasure));

    /// <summary>The named paragraph style it takes: how it stands and the formatting of its inlines, under what it and
    /// its inlines set themselves. Null sets none.</summary>
    public ParagraphStyle ParagraphStyle
    {
        get => GetValue<ParagraphStyle>(ParagraphStyleProperty);
        set => SetValue(ParagraphStyleProperty, value);
    }

    /// <summary>How its lines stand between its edges; null takes the TextBlock's.</summary>
    public HorizontalTextAlignment? HorizontalTextAlignment
    {
        get => GetValue<HorizontalTextAlignment?>(HorizontalTextAlignmentProperty);
        set => SetValue(HorizontalTextAlignmentProperty, value);
    }

    /// <summary>Left and right keep its lines from the block's edges; top and bottom are the room above and below it.
    /// Between two paragraphs only the larger of the two rooms is kept, none above the first or below the last, and
    /// none at the top of a column. Its lines never start past the block's edges.</summary>
    public Thickness Margin
    {
        get => GetValue<Thickness>(MarginProperty);
        set => SetValue(MarginProperty, value);
    }

    /// <summary>How much further its first line starts than the rest, at the side its lines start from; negative hangs
    /// the first line out into the margin, never past the block's edge. A drop cap takes its place.</summary>
    public double TextIndent
    {
        get => GetValue<double>(TextIndentProperty);
        set => SetValue(TextIndentProperty, value);
    }

    /// <summary>The height of its lines, as the TextBlock's <c>LineHeight</c>: larger text may still grow a line. NaN
    /// (default) takes the TextBlock's.</summary>
    public double LineHeight
    {
        get => GetValue<double>(LineHeightProperty);
        set => SetValue(LineHeightProperty, value);
    }
}
