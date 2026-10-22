using System;
using Adamantium.Graphics.Fonts;
using Adamantium.Mathematics;
using Adamantium.UI.Core.RoutedEvents;

namespace Adamantium.UI.Core.Media;

/// <summary>A soft band of color round the letters of a text, under it: Word's text glow. Its <see cref="Radius"/>
/// reaches about a tenth of an em at most, as far as a glyph's distance field goes.</summary>
public sealed class TextGlow : AdamantiumComponent
{
    public static readonly AdamantiumProperty ColorProperty = AdamantiumProperty.Register(nameof(Color),
        typeof(Color), typeof(TextGlow), new PropertyMetadata(new Color(255, 208, 0, 192), OnChanged));

    public static readonly AdamantiumProperty RadiusProperty = AdamantiumProperty.Register(nameof(Radius),
        typeof(double), typeof(TextGlow), new PropertyMetadata(4.0, OnChanged));

    public Color Color
    {
        get => GetValue<Color>(ColorProperty);
        set => SetValue(ColorProperty, value);
    }

    /// <summary>How far the glow reaches out from the letters, in the text's units.</summary>
    public double Radius
    {
        get => GetValue<double>(RadiusProperty);
        set => SetValue(RadiusProperty, value);
    }

    /// <summary>Raised when any value changes, so the text wearing it draws again.</summary>
    public event EventHandler Changed;

    /// <summary>What the engine draws for it.</summary>
    public GlyphGlow ToGlyphGlow() => new(Color, Radius);

    private static void OnChanged(AdamantiumComponent d, AdamantiumPropertyChangedEventArgs e)
        => (d as TextGlow)?.Changed?.Invoke(d, EventArgs.Empty);
}
