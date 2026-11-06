using Adamantium.UI.Controls.Base;
using Adamantium.UI.Controls.Automation;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Automation;
using Adamantium.UI.Core.RoutedEvents;

namespace Adamantium.UI.Controls.Canvases;

/// <summary>Actions for the selection in a bar that follows the selection frame; properties stay in the inspector. In a graph
/// only delete applies.</summary>
public class CanvasSelectionBar : Control, ICanvasPart
{
    public static readonly AdamantiumProperty CanvasProperty = AdamantiumProperty.Register(nameof(Canvas),
        typeof(InfiniteCanvas), typeof(CanvasSelectionBar), new PropertyMetadata(null, OnCanvasChanged));

    /// <summary>Whether the canvas is a <see cref="DrawingBoard"/> - which is what the actions that only mean something
    /// to a drawing are shown by.</summary>
    public static readonly AdamantiumProperty IsDrawingProperty = AdamantiumProperty.Register(nameof(IsDrawing),
        typeof(Boolean), typeof(CanvasSelectionBar), new PropertyMetadata(false));

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
        if (component is CanvasSelectionBar bar) bar.IsDrawing = e.NewValue is InfiniteCanvas { IsDrawing: true };
    }

    protected override AutomationPeer OnCreateAutomationPeer() =>
        TemplatedParent == null ? new ToolBarAutomationPeer(this) : null;
}
