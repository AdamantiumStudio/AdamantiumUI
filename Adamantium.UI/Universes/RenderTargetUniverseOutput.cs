using System;
using Adamantium.Core.Events;
using Adamantium.Multiverse;
using Adamantium.Graphics;
using Adamantium.Graphics.Core;
using Adamantium.Graphics.Core.Presentation;
using Adamantium.Imaging;
using Adamantium.Mathematics;
using Adamantium.UI.Controls.Panels;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Input;
using Adamantium.UI.Core.RoutedEvents;

namespace Adamantium.UI.Universes;

/// <summary>
/// An output that presents into a <see cref="RenderTargetPanel"/>: its <see cref="SharedSurfaceProducer"/> copies the
/// frames into shared surfaces the panel takes - of the logical device the panel draws with, when the universe renders on
/// it and the panel leaves it to choose (<see cref="RenderTargetPanel.FrameSharing"/>), else exported ones, imported
/// through OS handles. Made again when the panel resizes.
/// </summary>
public class RenderTargetUniverseOutput : UIUniverseOutput, ISharedSurfaceConsumer
{
    private readonly IGraphicsDeviceService _uiDevices;
    private readonly ISharedSurfaceProducer _producer;
    private RenderTargetPanel nativeWindow;

    // Without the UI's devices the frames always go through handles.
    internal RenderTargetUniverseOutput(IEventAggregator eventAggregator, OutputContext context,
        IGraphicsDeviceService uiDevices = null) : base(eventAggregator)
    {
        _uiDevices = uiDevices;
        _producer = new SharedSurfaceProducer(this, SurfaceFormat.B8G8R8A8.UNorm);
        Initialize(context);
    }

    internal RenderTargetUniverseOutput(
        IEventAggregator eventAggregator,
        OutputContext context,
        IGraphicsDeviceService uiDevices,
        SurfaceFormat pixelFormat,
        DepthFormat depthFormat,
        MSAALevel msaaLevel) : base(eventAggregator)
    {
        _uiDevices = uiDevices;
        _producer = new SharedSurfaceProducer(this, pixelFormat);
        Initialize(context, pixelFormat, depthFormat, msaaLevel);
    }

    protected override void InitializeInternal(OutputContext context)
    {
        if (OutputContext.Context is not RenderTargetPanel)
        {
            throw new ArgumentException($"{nameof(context.Context)} should be of type RenderTargetPanel");
        }

        OutputContext = context;
        nativeWindow = (RenderTargetPanel)OutputContext.Context;
        InputComponent = nativeWindow;
        nativeWindow.SizeChanged += NativeWindowOnSizeChanged;
        Description = new UniverseOutputDescription(PresenterType.RenderTarget);

        PixelsPerPoint = HostScale;
        Width = InPixels(nativeWindow.ActualWidth);
        Height = InPixels(nativeWindow.ActualHeight);
        ClientBounds = new Rectangle<int>(0, 0, (int)Description.Width, (int)Description.Height);
        UpdateViewportAndScissor((uint)ClientBounds.Width, (uint)ClientBounds.Height);
        base.InitializeInternal(context);
    }

    // The presenter's size, not the panel's: until the frame applies a new size the two differ, and the copy must fit.
    public override void CopyOutput(IGraphicsDevice mainDevice)
    {
        var rt = Presenter as RenderTargetGraphicsPresenter;
        if (rt?.ResolveTexture == null) return;

        _producer.Produce(mainDevice, rt.ResolveTexture, Width, Height, SharesDeviceWithPanel(mainDevice));
    }

    public override void DisplayContent()
    {
        base.DisplayContent();
        _producer.NoteSubmitted();
    }

    /// <summary>Waits until the panel asks for a frame, so one is drawn per frame it shows, ready by its next ask; then
    /// until the older surface is given back for it. Gives whether it was; if not, asks for a frame again on the panel's
    /// next ask.</summary>
    public override bool WaitUntilShown(TimeSpan timeout) => _producer.WaitUntilShown(timeout);

    void ISharedSurfaceConsumer.Bind(SharedSurfaceDescriptor descriptor) => nativeWindow.SetSource(descriptor);

    // Only of this output's surface - another output may have bound the panel since.
    void ISharedSurfaceConsumer.Unbind(SharedSurfaceDescriptor descriptor) => nativeWindow?.ClearSource(descriptor);

    void ISharedSurfaceConsumer.NoteFrameSubmitted()
    {
        if (nativeWindow.RootVisual?.Window is { } window)
        {
            (window.Renderer ?? window.DefaultRenderer)?.RequestFrame();
        }
    }

    void ISharedSurfaceConsumer.AskFrame() => Invalidate();

    // The panel takes the frames within the device when it leaves it to choose and draws with the device's own logical
    // device - one main device, one VkDevice.
    private bool SharesDeviceWithPanel(IGraphicsDevice device) =>
        nativeWindow.FrameSharing == FrameSharing.Auto
        && _uiDevices?.MainGraphicsDevice is { } uiDevice
        && ReferenceEquals(uiDevice, device.MainDevice);

    protected override void Dispose(bool disposeManagedResources)
    {
        nativeWindow.SizeChanged -= NativeWindowOnSizeChanged;
        _producer.Release(retire: true);
        base.Dispose(disposeManagedResources);
    }

    // The old device is still alive, but goes: nothing of it is drawn again.
    protected override void ReleaseDeviceResources()
    {
        _producer.Release(retire: false);
        base.ReleaseDeviceResources();
    }

    private void NativeWindowOnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        // SizeChanged bubbles up from children, so the size is read from the panel itself, not from e.NewSize.
        var width = InPixels(nativeWindow.ActualWidth);
        var height = InPixels(nativeWindow.ActualHeight);
        if (width == Width && height == Height)
        {
            return;
        }

        RequestResize(width, height);
    }

    protected override void OnHostScaleChanged()
    {
        PixelsPerPoint = HostScale;
        RequestResize(InPixels(nativeWindow.ActualWidth), InPixels(nativeWindow.ActualHeight));
    }

    private uint InPixels(double points)
    {
        return (uint)Math.Round(points * PixelsPerPoint);
    }

    public override UniverseOutputDescription Description { get; protected set; }

    /// <summary>
    /// Underlying control for rendering
    /// </summary>
    public override object NativeWindow => nativeWindow;

    protected override bool CanHandle(OutputContext context)
    {
        return context.Context is RenderTargetPanel && nativeWindow != null;
    }

    protected override bool IsUniverseInput(RoutedEventArgs e) => ReferenceEquals(e.OriginalSource, InputComponent);

    protected override bool SpendsKey(KeyEventArgs e) => nativeWindow.SpendsKey(e);

    protected override void SwitchContext(OutputContext context)
    {
        if (!CanHandle(context)) return;

        nativeWindow.SizeChanged -= NativeWindowOnSizeChanged;
        _producer.Release(retire: true);
        Initialize(context);
    }
}
