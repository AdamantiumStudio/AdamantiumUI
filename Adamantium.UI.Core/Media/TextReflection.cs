using System;
using Adamantium.Graphics.Fonts;
using Adamantium.UI.Core.RoutedEvents;

namespace Adamantium.UI.Core.Media;

/// <summary>The letters of a text again, upside down below their line, fading away from it: Word's text reflection.
/// </summary>
public sealed class TextReflection : AdamantiumComponent
{
    public static readonly AdamantiumProperty OpacityProperty = AdamantiumProperty.Register(nameof(Opacity),
        typeof(double), typeof(TextReflection), new PropertyMetadata(0.5, OnChanged));

    public static readonly AdamantiumProperty DistanceProperty = AdamantiumProperty.Register(nameof(Distance),
        typeof(double), typeof(TextReflection), new PropertyMetadata(0.0, OnChanged));

    public static readonly AdamantiumProperty SizeProperty = AdamantiumProperty.Register(nameof(Size),
        typeof(double), typeof(TextReflection), new PropertyMetadata(0.5, OnChanged));

    /// <summary>How strong the reflection starts, 0 to 1.</summary>
    public double Opacity
    {
        get => GetValue<double>(OpacityProperty);
        set => SetValue(OpacityProperty, value);
    }

    /// <summary>How far below the line it stands, in the text's units.</summary>
    public double Distance
    {
        get => GetValue<double>(DistanceProperty);
        set => SetValue(DistanceProperty, value);
    }

    /// <summary>How much of the letters shows before it has faded, 0 to 1.</summary>
    public double Size
    {
        get => GetValue<double>(SizeProperty);
        set => SetValue(SizeProperty, value);
    }

    /// <summary>Raised when any value changes, so the text wearing it draws again.</summary>
    public event EventHandler Changed;

    /// <summary>What the engine draws for it.</summary>
    public GlyphReflection ToGlyphReflection() => new(Opacity, Distance, Size);

    private static void OnChanged(AdamantiumComponent d, AdamantiumPropertyChangedEventArgs e)
        => (d as TextReflection)?.Changed?.Invoke(d, EventArgs.Empty);
}
