using System;
using Adamantium.Graphics.Fonts;
using Adamantium.Mathematics;
using Adamantium.UI.Core.RoutedEvents;

namespace Adamantium.UI.Core.Media;

/// <summary>A line round each letter of a text, outside it: Word's text outline. Its <see cref="Width"/> reaches about a
/// tenth of an em at most, as far as a glyph's distance field goes.</summary>
public sealed class TextOutline : AdamantiumComponent
{
    public static readonly AdamantiumProperty ColorProperty = AdamantiumProperty.Register(nameof(Color),
        typeof(Color), typeof(TextOutline), new PropertyMetadata(Colors.Black, OnChanged));

    public static readonly AdamantiumProperty WidthProperty = AdamantiumProperty.Register(nameof(Width),
        typeof(double), typeof(TextOutline), new PropertyMetadata(1.0, OnChanged));

    public Color Color
    {
        get => GetValue<Color>(ColorProperty);
        set => SetValue(ColorProperty, value);
    }

    /// <summary>How wide the line is, in the text's units.</summary>
    public double Width
    {
        get => GetValue<double>(WidthProperty);
        set => SetValue(WidthProperty, value);
    }

    /// <summary>Raised when any value changes, so the text wearing it draws again.</summary>
    public event EventHandler Changed;

    /// <summary>What the engine draws for it.</summary>
    public GlyphOutline ToGlyphOutline() => new(Color, Width);

    private static void OnChanged(AdamantiumComponent d, AdamantiumPropertyChangedEventArgs e)
        => (d as TextOutline)?.Changed?.Invoke(d, EventArgs.Empty);
}
