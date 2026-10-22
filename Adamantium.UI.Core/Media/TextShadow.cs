using System;
using Adamantium.Graphics.Fonts;
using Adamantium.Mathematics;
using Adamantium.UI.Core.RoutedEvents;

namespace Adamantium.UI.Core.Media;

/// <summary>A shadow of the letters of a text, as CSS <c>text-shadow</c>: each letter again, moved and softened, under
/// the text. Its <see cref="Blur"/> reaches about a tenth of an em at most, as far as a glyph's distance field goes.
/// </summary>
public sealed class TextShadow : AdamantiumComponent
{
    public static readonly AdamantiumProperty ColorProperty = AdamantiumProperty.Register(nameof(Color),
        typeof(Color), typeof(TextShadow), new PropertyMetadata(new Color(0, 0, 0, 128), OnChanged));

    public static readonly AdamantiumProperty OffsetXProperty = AdamantiumProperty.Register(nameof(OffsetX),
        typeof(double), typeof(TextShadow), new PropertyMetadata(2.0, OnChanged));

    public static readonly AdamantiumProperty OffsetYProperty = AdamantiumProperty.Register(nameof(OffsetY),
        typeof(double), typeof(TextShadow), new PropertyMetadata(2.0, OnChanged));

    public static readonly AdamantiumProperty BlurProperty = AdamantiumProperty.Register(nameof(Blur),
        typeof(double), typeof(TextShadow), new PropertyMetadata(2.0, OnChanged));

    public Color Color
    {
        get => GetValue<Color>(ColorProperty);
        set => SetValue(ColorProperty, value);
    }

    /// <summary>How far the shadow is thrown right, in the text's units.</summary>
    public double OffsetX
    {
        get => GetValue<double>(OffsetXProperty);
        set => SetValue(OffsetXProperty, value);
    }

    /// <summary>How far the shadow is thrown down, in the text's units.</summary>
    public double OffsetY
    {
        get => GetValue<double>(OffsetYProperty);
        set => SetValue(OffsetYProperty, value);
    }

    /// <summary>How soft its edge is: the width, in the text's units, it fades over; 0 for a sharp one.</summary>
    public double Blur
    {
        get => GetValue<double>(BlurProperty);
        set => SetValue(BlurProperty, value);
    }

    /// <summary>Raised when any value changes, so the text wearing it draws again.</summary>
    public event EventHandler Changed;

    /// <summary>What the engine draws for it.</summary>
    public GlyphShadow ToGlyphShadow() => new(Color, OffsetX, OffsetY, Blur);

    private static void OnChanged(AdamantiumComponent d, AdamantiumPropertyChangedEventArgs e)
        => (d as TextShadow)?.Changed?.Invoke(d, EventArgs.Empty);
}
