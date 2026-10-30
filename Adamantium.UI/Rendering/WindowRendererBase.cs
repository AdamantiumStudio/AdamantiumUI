using System;
using System.Threading;
using Adamantium.Core;
using Adamantium.Graphics.Core;
using Adamantium.Graphics.Core.Presentation;
using Adamantium.Mathematics;
using Adamantium.UI.Controls;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Graphics;
using Adamantium.UI.FX;
using Adamantium.Vulkan.Core;
using Adamantium.Win32;

namespace Adamantium.UI.Rendering;

public abstract class WindowRendererBase : IWindowRenderer
{
    private long _presentedFrames;
    private bool _presentedContent;
    private bool _swapchainKeepsNoAlpha;
    private long _frameRequests;
    private volatile bool _isRendererUpToDate;
    private double _renderScale = 1.0;

    protected Viewport Viewport { get; set; }
    protected Rect2D Scissor { get; set; }
    protected Rect2D ClipRect { get; set; }
    protected Matrix4x4F ProjectionMatrix { get; set; }
    protected IGraphicsDevice GraphicsDevice { get; }
    
    protected IRenderUnitFactory RenderUnitFactory { get; } 
    protected PresentationParameters Parameters { get; set; }
    
    protected UIBasicEffect UiEffect { get; set; }

    public virtual void PrepareData()
    {

    }

    // Record/apply split. Default no-ops; ForwardWindowRenderer implements them.
    public virtual void RecordData()
    {

    }

    public virtual void ApplyData()
    {

    }

    // No-op by default: the on-screen renderer keeps its cache across frames (attachment-based reconciliation). A
    // renderer that rebuilds a fresh tree every frame (headless designer) overrides this to drop its units.
    public virtual void ResetCache()
    {

    }

    public GraphicsPresenter Presenter { get; private set; }
    
    protected WindowRendererBase(IGraphicsDevice device, IRenderUnitFactory renderUnitFactory)
    {
        Viewport = new Viewport();
        Scissor = new Rect2D();
        ClipRect = new Rect2D();
        ClipRect.Offset = new Offset2D();
        ClipRect.Extent = new Extent2D();
        GraphicsDevice = device;
        DrawingContext = new DrawingContext();
        RenderUnitFactory = renderUnitFactory;
    }
    
    protected IWindow Window { get; set; }

    public IDrawingContext DrawingContext { get; }

    public bool IsRendererUpToDate
    {
        get => _isRendererUpToDate;
        protected set => _isRendererUpToDate = value;
    }

    public bool FirstFrameProcessed { get; private set; }

    public long PresentedFrames => Interlocked.Read(ref _presentedFrames);

    public bool HasPresentedContent => Volatile.Read(ref _presentedContent);

    /// <summary>Whether the frame being drawn shows what the window recorded; a renderer that draws only what it is
    /// recorded says so until it has applied a recording.</summary>
    protected virtual bool DrawsRecordedContent => true;

    public virtual int RecordedFrames => 0;

    public virtual int ChangedFrames => 0;

    public long FrameRequests => Interlocked.Read(ref _frameRequests);

    public void RequestFrame()
    {
        Interlocked.Increment(ref _frameRequests);
        RenderSignal.Wake();
    }

    // Render resolution multiplier (1 = on-screen 1:1). The presenter + viewport are sized ClientSize x RenderScale,
    // the projection stays ClientSize - so the designer renders crisply at design x scale (zoom) without changing layout.
    public double RenderScale
    {
        get => _renderScale;
        set
        {
            if (_renderScale == value) 
                return;
            
            _renderScale = value;
            OnRenderScaleChanged();
        }
    }

    protected virtual void OnRenderScaleChanged()
    {
    }

    protected virtual void UnsubscribeFromEvents()
    {
        
    }

    protected virtual void SubscribeToEvents()
    {
        
    }
    
    public virtual void SetWindow(IWindow window)
    {
        if (window == null) return;

        UnsubscribeFromEvents();
        Window = window;
        Window.Renderer = this;
        FillParameters();
        SubscribeToEvents();
        InitializeWindowResources();
    }

    // Re-points the renderer at a different window REUSING the existing presenter (just resized to the new window's
    // size) instead of recreating it like SetWindow. The designer switches the previewed window this way - one
    // presenter for the whole session, no render-target churn. First call (no presenter yet) falls back to SetWindow.
    public virtual void Retarget(IWindow window)
    {
        if (window == null) return;
        if (Presenter == null) { SetWindow(window); return; }

        UnsubscribeFromEvents();
        Window = window;
        Window.Renderer = this;
        SubscribeToEvents();
        ResizePresenterToWindow();
        InitializeWindowResources();
    }

    /// <summary>
    /// The presenter kind this renderer builds. Defaults to the on-screen swapchain; a see-through window whose swapchain
    /// the surface composes without its alpha - on Windows, a device that offers no premultiplied alpha there - is read
    /// back (<see cref="PresenterType.Readback"/>) into the layered window the desktop composes from its pixels. A
    /// headless renderer overrides it to <see cref="PresenterType.Headless"/>, in which case the surface is window-less.
    /// </summary>
    protected virtual PresenterType PresenterKind =>
        _swapchainKeepsNoAlpha && CanComposeFromPixels() ? PresenterType.Readback : PresenterType.Swapchain;

    private void FillParameters()
    {
        Parameters = new PresentationParameters(
            PresenterKind,
            (uint)Math.Round(Window.ClientWidth * RenderScale),
            (uint)Math.Round(Window.ClientHeight * RenderScale),
            Window.SurfaceHandle,
            Window.MSAALevel
        )
        {
            HInstanceHandle = OperatingSystem.IsWindows() ? Win32Interop.GetModuleHandle(null) : IntPtr.Zero,
            DisplayHandle = Window.SurfaceDisplayHandle,
            // A see-through window is a property of the WINDOW, so it travels from there to the surface that composes it.
            TransparentComposition = Window.UseTransparentComposition,
            PresentPolicy = ResolvePresentPolicy(),
            FrameSink = (Window as WindowBase)?.FrameSink
        };

        Presenter = GraphicsPresenter.Create(GraphicsDevice, Parameters, "Window_presenter");
        if (Presenter is SwapChainGraphicsPresenter { ComposesWithAlpha: false } && Parameters.TransparentComposition
            && CanComposeFromPixels())
        {
            _swapchainKeepsNoAlpha = true;
            Presenter.Dispose();
            FillParameters();
        }
    }

    private bool CanComposeFromPixels() =>
        OperatingSystem.IsWindows() && Window is WindowBase { UseTransparentComposition: true, FrameSink: not null };

    /// <summary>The window's own choice, or the application's when it made none. Resolved HERE, in the one place that
    /// builds the parameters, so Inherit never reaches a presenter and no other code has to know the rule.</summary>
    private PresentPolicy ResolvePresentPolicy()
    {
        if (UIApplication.RunsUnlimited(Window))
        {
            return PresentPolicy.Immediate;
        }

        if (Window.PresentPolicy != PresentPolicy.Inherit) return Window.PresentPolicy;

        var app = UIApplication.Current?.PresentPolicy ?? PresentPolicy.Adaptive;
        return app == PresentPolicy.Inherit ? PresentPolicy.Adaptive : app;
    }

    protected virtual void InitializeWindowResources()
    {
        
    }

    public abstract void Render(AppTime appTime);

    public abstract void PreRender();


    public void ResizePresenter(PresentationParameters parameters)
    {
        Presenter.Resize(parameters);
        IsRendererUpToDate = true;
    }

    public void ResizePresenter(uint width, uint height)
    {
        Resize(width, height);
        IsRendererUpToDate = true;
    }

    public void ResizePresenterToWindow()
    {
        IsRendererUpToDate = true;
        Thread.MemoryBarrier();
        Resize((uint)Math.Round(Window.ClientWidth * RenderScale), (uint)Math.Round(Window.ClientHeight * RenderScale));
    }

    private void Resize(uint width, uint height)
    {
        if (Window != null && Presenter != null && Parameters.PresenterType != PresenterKind)
        {
            GraphicsDevice.DeviceWaitIdle();
            Presenter.Dispose();
            FillParameters();
            return;
        }

        // Re-read transparency and present policy on every rebuild, the only moment a swapchain can pick them up.
        if (Window != null && Presenter != null)
        {
            Presenter.Description.TransparentComposition = Window.UseTransparentComposition;
            Parameters.TransparentComposition = Window.UseTransparentComposition;

            var policy = ResolvePresentPolicy();
            Presenter.Description.PresentPolicy = policy;
            Parameters.PresentPolicy = policy;
        }

        Presenter.Resize(width, height);
    }

    /// <summary>The presenter no longer matches its window: rebuild it at the next frame boundary. The frame loop
    /// already recreates a stale presenter in BeginDraw, before the frame draws and on the render thread - so a toggle
    /// simply joins the queue a resize is in, instead of destroying a swapchain someone may be drawing into.</summary>
    public void InvalidatePresenter()
    {
        IsRendererUpToDate = false;
    }

    public virtual void OnFrameEnded()
    {
        FirstFrameProcessed = true;
    }

    public virtual void Present()
    {
        // Present ONLY when this frame actually acquired + submitted. GraphicsDevice.CanPresent is reset false at
        // BeginDraw start and set true only after a successful Submit, so a frame skipped/aborted during resize churn
        // (no acquire/submit) does NOT re-present a stale, not-reacquired swapchain image with an unsignaled semaphore
        // - the pair of VUIDs that lost the device on drag-resize.
        if (GraphicsDevice.CanPresent && Presenter is { CanPresent: true })
        {
            Presenter.Present();
            Interlocked.Increment(ref _presentedFrames);
            if (DrawsRecordedContent)
            {
                Volatile.Write(ref _presentedContent, true);
            }
        }

        if (Window.ShouldDisplayWindow && HasPresentedContent)
        {
            Window.UIContext.Dispatcher.Invoke(() => Window.Show());
        }
    }
    
    public virtual void Dispose()
    {
        UnsubscribeFromEvents();
        Presenter?.Dispose();
        Presenter = null;
        // Last: the units built from it are gone by now.
        (RenderUnitFactory as IDisposable)?.Dispose();
    }
}
