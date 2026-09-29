using System;
using Adamantium.Mathematics;
using Adamantium.UI.Core.RoutedEvents;
namespace Adamantium.UI.Core.Media;

/// <summary>A directional shadow, as CSS <c>box-shadow</c>, drawn outside the bounds (inside with <see cref="Inner"/>).
/// Layout does not grow to fit it; leave room with <c>Margin</c> or a clipping ancestor cuts it.</summary>
public sealed class Shadow : AdamantiumComponent
{
    public static readonly AdamantiumProperty IsEnabledProperty = AdamantiumProperty.Register(nameof(IsEnabled),
        typeof(bool), typeof(Shadow), new PropertyMetadata(true, OnChanged));

    public static readonly AdamantiumProperty OffsetXProperty = AdamantiumProperty.Register(nameof(OffsetX),
        typeof(double), typeof(Shadow), new PropertyMetadata(0.0, OnChanged));

    public static readonly AdamantiumProperty OffsetYProperty = AdamantiumProperty.Register(nameof(OffsetY),
        typeof(double), typeof(Shadow), new PropertyMetadata(4.0, OnChanged));

    public static readonly AdamantiumProperty BlurRadiusProperty = AdamantiumProperty.Register(nameof(BlurRadius),
        typeof(double), typeof(Shadow), new PropertyMetadata(12.0, OnChanged));

    public static readonly AdamantiumProperty SpreadProperty = AdamantiumProperty.Register(nameof(Spread),
        typeof(double), typeof(Shadow), new PropertyMetadata(0.0, OnChanged));

    public static readonly AdamantiumProperty ColorProperty = AdamantiumProperty.Register(nameof(Color),
        typeof(Color), typeof(Shadow), new PropertyMetadata(Colors.Black, OnChanged));

    public static readonly AdamantiumProperty OpacityProperty = AdamantiumProperty.Register(nameof(Opacity),
        typeof(double), typeof(Shadow), new PropertyMetadata(0.35, OnChanged));

    public static readonly AdamantiumProperty InnerProperty = AdamantiumProperty.Register(nameof(Inner),
        typeof(bool), typeof(Shadow), new PropertyMetadata(false, OnChanged));

    /// <summary>Switch the shadow off without losing its settings - what a trigger or a binding wants (an element that
    /// lifts only while dragged), and what zeroing the blur or the opacity would only fake.</summary>
    public bool IsEnabled
    {
        get => GetValue<bool>(IsEnabledProperty);
        set => SetValue(IsEnabledProperty, value);
    }

    /// <summary>How far the shadow is thrown sideways, in pixels - i.e. where the light is.</summary>
    public double OffsetX
    {
        get => GetValue<double>(OffsetXProperty);
        set => SetValue(OffsetXProperty, value);
    }

    /// <summary>How far the shadow is thrown down, in pixels. Positive is downward, as a light from above gives.</summary>
    public double OffsetY
    {
        get => GetValue<double>(OffsetYProperty);
        set => SetValue(OffsetYProperty, value);
    }

    /// <summary>How SOFT the edge is: the width, in pixels, over which the shadow fades out.</summary>
    public double BlurRadius
    {
        get => GetValue<double>(BlurRadiusProperty);
        set => SetValue(BlurRadiusProperty, value);
    }

    /// <summary>How far the shadow is INFLATED past the shape before it starts to fade - what reads as height above the
    /// surface. Negative shrinks it, for a shadow that only peeks out from under an edge.</summary>
    public double Spread
    {
        get => GetValue<double>(SpreadProperty);
        set => SetValue(SpreadProperty, value);
    }

    public Color Color
    {
        get => GetValue<Color>(ColorProperty);
        set => SetValue(ColorProperty, value);
    }

    /// <summary>Multiplies the color's own alpha. A real shadow is never opaque - the default is deliberately low.</summary>
    public double Opacity
    {
        get => GetValue<double>(OpacityProperty);
        set => SetValue(OpacityProperty, value);
    }

    /// <summary>Cast the shadow INSIDE the shape instead of behind it (CSS <c>inset</c>): the pressed / recessed look.</summary>
    public bool Inner
    {
        get => GetValue<bool>(InnerProperty);
        set => SetValue(InnerProperty, value);
    }

    /// <summary>Raised when any value changes, so the element wearing it can re-record.</summary>
    public event EventHandler Changed;

    private static void OnChanged(AdamantiumComponent d, AdamantiumPropertyChangedEventArgs e)
        => (d as Shadow)?.Changed?.Invoke(d, EventArgs.Empty);
}
