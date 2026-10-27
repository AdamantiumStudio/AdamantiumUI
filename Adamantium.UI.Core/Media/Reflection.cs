using System;
using Adamantium.UI.Core.RoutedEvents;

namespace Adamantium.UI.Core.Media;

/// <summary>An element's mirror image under all it draws - its own glow and shadow too - as a picture standing on glass.
/// It mirrors what of the element is in view. Layout does not grow to fit it; leave room with <c>Margin</c> or a
/// clipping ancestor cuts it.</summary>
public sealed class Reflection : AdamantiumComponent
{
    public static readonly AdamantiumProperty IsEnabledProperty = AdamantiumProperty.Register(nameof(IsEnabled),
        typeof(bool), typeof(Reflection), new PropertyMetadata(true, OnChanged));

    public static readonly AdamantiumProperty SizeProperty = AdamantiumProperty.Register(nameof(Size),
        typeof(double), typeof(Reflection), new PropertyMetadata(0.5, OnChanged));

    public static readonly AdamantiumProperty FadeProperty = AdamantiumProperty.Register(nameof(Fade),
        typeof(double), typeof(Reflection), new PropertyMetadata(1.0, OnChanged));

    public static readonly AdamantiumProperty OffsetXProperty = AdamantiumProperty.Register(nameof(OffsetX),
        typeof(double), typeof(Reflection), new PropertyMetadata(0.0, OnChanged));

    public static readonly AdamantiumProperty OffsetYProperty = AdamantiumProperty.Register(nameof(OffsetY),
        typeof(double), typeof(Reflection), new PropertyMetadata(0.0, OnChanged));

    public static readonly AdamantiumProperty OpacityProperty = AdamantiumProperty.Register(nameof(Opacity),
        typeof(double), typeof(Reflection), new PropertyMetadata(0.5, OnChanged));

    /// <summary>Switch the reflection off without losing its settings.</summary>
    public bool IsEnabled
    {
        get => GetValue<bool>(IsEnabledProperty);
        set => SetValue(IsEnabledProperty, value);
    }

    /// <summary>How much the reflection shows, as a share of the height of all the element draws, its glow and shadow
    /// included: 0.5 shows the lower half, mirrored; at most 1, the whole of it.</summary>
    public double Size
    {
        get => GetValue<double>(SizeProperty);
        set => SetValue(SizeProperty, value);
    }

    /// <summary>The far end of what it shows that fades away, as a share of it: 1 fades all the way from the edge, 0 ends
    /// it sharply.</summary>
    public double Fade
    {
        get => GetValue<double>(FadeProperty);
        set => SetValue(FadeProperty, value);
    }

    /// <summary>How far the reflection moves sideways, in pixels.</summary>
    public double OffsetX
    {
        get => GetValue<double>(OffsetXProperty);
        set => SetValue(OffsetXProperty, value);
    }

    /// <summary>The gap between the lowest of what the element draws - its edge, or its glow or shadow below it - and
    /// the reflection, in pixels.</summary>
    public double OffsetY
    {
        get => GetValue<double>(OffsetYProperty);
        set => SetValue(OffsetYProperty, value);
    }

    /// <summary>How strong the reflection is where it starts, under the edge.</summary>
    public double Opacity
    {
        get => GetValue<double>(OpacityProperty);
        set => SetValue(OpacityProperty, value);
    }

    /// <summary>Whether it draws anything at all.</summary>
    public bool Shows => IsEnabled && Size > 0 && Opacity > 0 && double.IsFinite(Size) && double.IsFinite(Fade)
                         && double.IsFinite(OffsetX) && double.IsFinite(OffsetY);

    /// <summary>Raised when any value changes, so the element wearing it can draw again.</summary>
    public event EventHandler Changed;

    private static void OnChanged(AdamantiumComponent d, AdamantiumPropertyChangedEventArgs e)
        => (d as Reflection)?.Changed?.Invoke(d, EventArgs.Empty);
}
