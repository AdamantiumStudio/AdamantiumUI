using Adamantium.Mathematics;
using Adamantium.UI.Controls.Panels;
using Adamantium.UI.Controls.Primitives;
using Adamantium.UI.Core;

namespace Adamantium.UI.WebView;

/// <summary>
/// A <see cref="WebView"/>'s page and its DevTools side by side: its children are the page, the splitter (a
/// <see cref="Thumb"/>, dragged to share the room) and the DevTools. With the DevTools closed the page takes it all.
/// </summary>
public sealed class WebViewDevToolsLayout : Panel
{
    public static readonly AdamantiumProperty IsDevToolsOpenProperty = AdamantiumProperty.Register(
        nameof(IsDevToolsOpen), typeof(bool), typeof(WebViewDevToolsLayout),
        new PropertyMetadata(false, PropertyMetadataOptions.AffectsMeasure | PropertyMetadataOptions.AffectsArrange));

    public static readonly AdamantiumProperty DockProperty = AdamantiumProperty.Register(nameof(Dock),
        typeof(WebViewDevToolsDock), typeof(WebViewDevToolsLayout),
        new PropertyMetadata(WebViewDevToolsDock.Right, PropertyMetadataOptions.AffectsMeasure | PropertyMetadataOptions.AffectsArrange));

    public static readonly AdamantiumProperty DevToolsShareProperty = AdamantiumProperty.Register(nameof(DevToolsShare),
        typeof(double), typeof(WebViewDevToolsLayout),
        new PropertyMetadata(0.4, PropertyMetadataOptions.AffectsMeasure | PropertyMetadataOptions.AffectsArrange));

    private const double MinShare = 0.1;
    private const double MaxShare = 0.9;
    private const double SplitterThickness = 5;

    private double shareAtDragStart;

    public WebViewDevToolsLayout()
    {
        AddHandler(Thumb.DragStartedEvent, new DragStartedEventHandler((_, _) => shareAtDragStart = DevToolsShare));
        AddHandler(Thumb.DragDeltaEvent, new DragEventHandler(OnSplitterDragged));
    }

    /// <summary>Whether the DevTools are shown beside the page.</summary>
    public bool IsDevToolsOpen
    {
        get => GetValue<bool>(IsDevToolsOpenProperty);
        set => SetValue(IsDevToolsOpenProperty, value);
    }

    /// <summary>The side of the page the DevTools stand on.</summary>
    public WebViewDevToolsDock Dock
    {
        get => GetValue<WebViewDevToolsDock>(DockProperty);
        set => SetValue(DockProperty, value);
    }

    /// <summary>How much of the room the DevTools take, from 0.1 to 0.9; dragging the splitter sets it.</summary>
    public double DevToolsShare
    {
        get => GetValue<double>(DevToolsShareProperty);
        set => SetValue(DevToolsShareProperty, value);
    }

    protected override Size<double> MeasureOverride(Size<double> availableSize)
    {
        var (page, splitter, devTools) = Slots(availableSize);
        Measure(0, page.Size);
        Measure(1, splitter.Size);
        Measure(2, devTools.Size);
        return new Size<double>(double.IsInfinity(availableSize.Width) ? 0 : availableSize.Width,
            double.IsInfinity(availableSize.Height) ? 0 : availableSize.Height);
    }

    protected override Size<double> ArrangeOverride(Size<double> finalSize)
    {
        var (page, splitter, devTools) = Slots(finalSize);
        Arrange(0, page);
        Arrange(1, splitter);
        Arrange(2, devTools);
        return finalSize;
    }

    private (Rect Page, Rect Splitter, Rect DevTools) Slots(Size<double> room)
    {
        var width = double.IsInfinity(room.Width) ? 0 : room.Width;
        var height = double.IsInfinity(room.Height) ? 0 : room.Height;
        if (!IsDevToolsOpen)
        {
            return (new Rect(0, 0, width, height), new Rect(0, 0, 0, 0), new Rect(0, 0, 0, 0));
        }

        var share = Math.Clamp(DevToolsShare, MinShare, MaxShare);
        var thickness = Children.Count > 1 ? SplitterThickness : 0;
        if (Dock == WebViewDevToolsDock.Right)
        {
            var tools = Math.Max(0, Math.Round((width - thickness) * share));
            var pageWidth = Math.Max(0, width - thickness - tools);
            return (new Rect(0, 0, pageWidth, height), new Rect(pageWidth, 0, thickness, height),
                new Rect(pageWidth + thickness, 0, tools, height));
        }

        var toolsHeight = Math.Max(0, Math.Round((height - thickness) * share));
        var pageHeight = Math.Max(0, height - thickness - toolsHeight);
        return (new Rect(0, 0, width, pageHeight), new Rect(0, pageHeight, width, thickness),
            new Rect(0, pageHeight + thickness, width, toolsHeight));
    }

    private void Measure(int index, Size<double> size)
    {
        if (index < Children.Count)
        {
            Children[index].Measure(size);
        }
    }

    private void Arrange(int index, Rect slot)
    {
        if (index < Children.Count)
        {
            Children[index].Arrange(slot);
        }
    }

    private void OnSplitterDragged(object sender, DragEventArgs e)
    {
        var extent = Dock == WebViewDevToolsDock.Right ? ActualWidth : ActualHeight;
        if (extent <= 0)
        {
            return;
        }

        var along = Dock == WebViewDevToolsDock.Right ? e.Change.X : e.Change.Y;
        SetCurrentValue(DevToolsShareProperty, Math.Clamp(shareAtDragStart - along / extent, MinShare, MaxShare));
        e.Handled = true;
    }
}
