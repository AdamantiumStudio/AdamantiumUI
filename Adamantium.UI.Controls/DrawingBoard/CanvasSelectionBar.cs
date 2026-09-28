using Adamantium.UI.Controls.Base;
using Adamantium.UI.Core;
using Adamantium.UI.Core.RoutedEvents;

namespace Adamantium.UI.Controls.DrawingBoard;

/// <summary>Actions for the selection in a bar that follows the selection frame; properties stay in the inspector. In a graph
/// only delete applies.</summary>
public class CanvasSelectionBar : Control, ICanvasPart
{
    public static readonly AdamantiumProperty CanvasProperty = AdamantiumProperty.Register(nameof(Canvas),
        typeof(InfiniteCanvas), typeof(CanvasSelectionBar), new PropertyMetadata(null, OnCanvasChanged));

    /// <summary>Whether the canvas is being used as a DRAWING - which is what the actions that only mean something to
    /// one are shown by.</summary>
    public static readonly AdamantiumProperty IsDrawingProperty = AdamantiumProperty.Register(nameof(IsDrawing),
        typeof(Boolean), typeof(CanvasSelectionBar), new PropertyMetadata(true));

    /// <summary>The plane whose selection this bar acts on.</summary>
    public InfiniteCanvas Canvas
    {
        get => GetValue<InfiniteCanvas>(CanvasProperty);
        set => SetValue(CanvasProperty, value);
    }

    public Boolean IsDrawing
    {
        get => GetValue<Boolean>(IsDrawingProperty);
        private set => SetCurrentValue(IsDrawingProperty, value);
    }

    private static void OnCanvasChanged(AdamantiumComponent component, AdamantiumPropertyChangedEventArgs e)
    {
        if (component is not CanvasSelectionBar bar) return;

        if (e.OldValue is InfiniteCanvas was) was.ModeChanged -= bar.OnModeChanged;
        if (e.NewValue is InfiniteCanvas now) now.ModeChanged += bar.OnModeChanged;

        bar.ReadMode();
    }

    private void OnModeChanged(object sender, EventArgs e) => ReadMode();

    private void ReadMode() => IsDrawing = Canvas is not { } canvas || canvas.Mode == CanvasMode.Drawing;
}
