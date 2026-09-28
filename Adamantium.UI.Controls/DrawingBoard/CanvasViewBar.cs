using System;
using System.Globalization;
using Adamantium.UI.Controls.Base;
using Adamantium.UI.Core;
using Adamantium.UI.Core.RoutedEvents;

namespace Adamantium.UI.Controls.DrawingBoard;

/// <summary>The view bar of an <see cref="InfiniteCanvas"/>: undo and redo, zoom and home, each one of the canvas's own
/// commands, which enable and disable themselves.</summary>
public class CanvasViewBar : Control, ICanvasPart
{
    public static readonly AdamantiumProperty CanvasProperty = AdamantiumProperty.Register(nameof(Canvas),
        typeof(InfiniteCanvas), typeof(CanvasViewBar), new PropertyMetadata(null, OnCanvasChanged));

    /// <summary>The zoom in words, as the bar shows it. A MULTIPLIER from one and not a percentage: everything else
    /// about the camera is stated that way, and at the far end a percentage stops reading at all - 0.01 shown as "1%"
    /// looks like a hundredth of a percent rather than a hundredth of full size.</summary>
    public static readonly AdamantiumProperty ZoomTextProperty = AdamantiumProperty.Register(nameof(ZoomText),
        typeof(String), typeof(CanvasViewBar), new PropertyMetadata("1x"));

    /// <summary>The plane this bar steers.</summary>
    public InfiniteCanvas Canvas
    {
        get => GetValue<InfiniteCanvas>(CanvasProperty);
        set => SetValue(CanvasProperty, value);
    }

    public String ZoomText
    {
        get => GetValue<String>(ZoomTextProperty);
        private set => SetCurrentValue(ZoomTextProperty, value);
    }

    private static void OnCanvasChanged(AdamantiumComponent component, AdamantiumPropertyChangedEventArgs e)
    {
        if (component is not CanvasViewBar bar) return;

        if (e.OldValue is InfiniteCanvas was) was.CameraChanged -= bar.OnCameraChanged;
        if (e.NewValue is InfiniteCanvas now) now.CameraChanged += bar.OnCameraChanged;

        bar.Read();
    }

    private void OnCameraChanged(object sender, EventArgs e) => Read();

    private void Read()
    {
        ZoomText = Canvas is not { } canvas
            ? "1x"
            : String.Format(CultureInfo.InvariantCulture, "{0:0.###}x", canvas.Scale);
    }
}
