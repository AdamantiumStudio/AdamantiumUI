using System;
using Adamantium.Core.Events;
using Adamantium.Multiverse;
using Adamantium.Graphics;
using Adamantium.Graphics.Core;
using Adamantium.Graphics.Core.Presentation;
using Adamantium.Imaging;
using Adamantium.UI.Controls.Panels;
using Adamantium.UI.Core.Input;
using Adamantium.UI.Core.RoutedEvents;
using Adamantium.Vulkan.Core;
using Rectangle = Adamantium.Mathematics.Rectangle;

namespace Adamantium.UI.Universes;

/// <summary>
/// An output that presents into a <see cref="RenderTargetPanel"/>: the frame is copied into an exportable
/// <see cref="SharedSurface"/>, which the panel imports. Re-created when the panel resizes.
/// </summary>
public class RenderTargetUniverseOutput : UIUniverseOutput
{
    private RenderTargetPanel nativeWindow;
    private SharedSurface _sharedSurface;
    // Made for a new size, given to the panel once it holds a frame.
    private SharedSurface _pendingSurface;
    private SurfaceFormat _format = SurfaceFormat.B8G8R8A8.UNorm;
    private ulong _lastProduced;

    internal RenderTargetUniverseOutput(IEventAggregator eventAggregator, OutputContext context) : base(eventAggregator)
    {
        Initialize(context);
    }

    internal RenderTargetUniverseOutput(
        IEventAggregator eventAggregator,
        OutputContext context,
        SurfaceFormat pixelFormat,
        DepthFormat depthFormat,
        MSAALevel msaaLevel) : base(eventAggregator)
    {
        _format = pixelFormat;
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
        ClientBounds = new Rectangle(0, 0, (int)Description.Width, (int)Description.Height);
        UpdateViewportAndScissor((uint)ClientBounds.Width, (uint)ClientBounds.Height);
        base.InitializeInternal(context);
    }

    public override void CopyOutput(IGraphicsDevice mainDevice)
    {
        var rt = Presenter as RenderTargetGraphicsPresenter;
        if (rt?.ResolveTexture == null) return;

        HandOverPendingSurface();
        if (StartSurfaceForNewSize(mainDevice, rt.ResolveTexture)) return;
        if (_sharedSurface == null || _pendingSurface != null) return;

        // Not over a frame the consumer has not read yet; a CPU check, so a paused consumer cannot stall the producer.
        if (_sharedSurface.ConsumeValue < _lastProduced) return;

        // Same queue as the resolve, so the copy is ordered after it. The wait is already met; it orders the consumer's
        // read before this write.
        mainDevice.AddWaitSemaphore(_sharedSurface.ConsumeSemaphore, PipelineStageFlagBits2.AllTransferBit, _lastProduced);
        mainDevice.RecordSharedSurfaceCopy(rt.ResolveTexture, _sharedSurface);
        _lastProduced++;
        mainDevice.AddSignalSemaphore(_sharedSurface.ProduceSemaphore, _lastProduced);
    }

    // A surface for a new size gets its first frame before the panel is given it, so the panel never draws an empty one;
    // meanwhile it keeps the old surface.
    private bool StartSurfaceForNewSize(IGraphicsDevice device, ITexture frame)
    {
        // Still waiting for its first frame: handed over first, even if the size moved on meanwhile, or a drag faster than
        // the GPU would never hand one over.
        if (_pendingSurface != null) return true;

        // The presenter's size, not the panel's: until the frame applies a new size the two differ, and the copy must fit.
        var width = Width;
        var height = Height;
        if (width == 0 || height == 0) return false;
        if (_sharedSurface != null && _sharedSurface.Width == width && _sharedSurface.Height == height) return false;

        _pendingSurface = SharedSurface.CreateExportable(device, width, height, _format);
        device.RecordSharedSurfaceCopy(frame, _pendingSurface);
        device.AddSignalSemaphore(_pendingSurface.ProduceSemaphore, 1);
        return true;
    }

    private void HandOverPendingSurface()
    {
        if (_pendingSurface == null || _pendingSurface.ProduceValue == 0) return;

        var replaced = _sharedSurface;
        _sharedSurface = _pendingSurface;
        _pendingSurface = null;
        _lastProduced = 1;
        // Bound before the old surface is retired, so the panel's import of the old one is already started or never will be.
        nativeWindow.SetSource(_sharedSurface.Descriptor);
        RetireSurface(replaced);
    }

    // Retired, not destroyed: submits of every device of this one may still reference its semaphores, and destroying
    // them mid-frame lost the device. The panel's import is its own.
    private static void RetireSurface(SharedSurface surface)
    {
        surface?.GraphicsDevice.AddToDeferDisposeQueue(surface);
    }

    protected override void Dispose(bool disposeManagedResources)
    {
        nativeWindow.SizeChanged -= NativeWindowOnSizeChanged;
        ReleaseSurface(retire: true);
        base.Dispose(disposeManagedResources);
    }

    // The old device is still alive, but goes: nothing of it is drawn again.
    protected override void ReleaseDeviceResources()
    {
        ReleaseSurface(retire: false);
        base.ReleaseDeviceResources();
    }

    // The panel lets go of the surface first: its handles close with it, and a panel back in the tree would import them.
    // Only of this output's surface - another output may have bound the panel since.
    private void ReleaseSurface(bool retire)
    {
        if (_sharedSurface != null) nativeWindow?.ClearSource(_sharedSurface.Descriptor);
        if (retire)
        {
            RetireSurface(_sharedSurface);
            RetireSurface(_pendingSurface);
        }
        else
        {
            _sharedSurface?.Dispose();
            _pendingSurface?.Dispose();
        }
        _sharedSurface = null;
        _pendingSurface = null;
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

    protected override bool SpendsKey(KeyEventArgs e) =>
        e.Key != Key.Tab ||
        nativeWindow.AcceptsTab && (e.Modifiers & (InputModifiers.LeftControl | InputModifiers.RightControl)) == 0;

    protected override void SwitchContext(OutputContext context)
    {
        if (!CanHandle(context)) return;

        nativeWindow.SizeChanged -= NativeWindowOnSizeChanged;
        ReleaseSurface(retire: true);
        Initialize(context);
    }
}
