using System;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Threading;
using Adamantium.Core;
using Adamantium.Graphics.Core;
using Adamantium.Graphics.Core.Extensions;
using Adamantium.Graphics.Core.Presentation;
using Adamantium.Mathematics;
using Adamantium.UI.Controls;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Graphics;
using Adamantium.UI.FX;
using Adamantium.Vulkan.Core;
using Adamantium.UI.Core.Media.Imaging;
using Adamantium.UI.Core.RoutedEvents;
using Adamantium.Win32;
using Serilog;

namespace Adamantium.UI.Rendering;

public abstract class WindowRendererBase : IWindowRenderer
{
    private long _presentedFrames;
    private bool _presentedContent;
    private SurfaceKHR _surface;
    private IntPtr _surfaceHandle;
    private bool _surfaceComposesWithAlpha;
    private long _frameRequests;
    private volatile bool _isRendererUpToDate;
    private double _renderScale = 1.0;
    private WindowOpacityEffect _opacityEffect;
    private readonly ConcurrentQueue<ScreenshotRequest> _screenshots = new();
    private long _recordsTaken;
    private long _recordsApplied;

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

    public void RequestScreenshot(Action<BitmapSource> onReady)
    {
        if (onReady == null)
        {
            return;
        }

        // A window that draws nothing gets no frame to read; it is answered at once rather than left waiting.
        if (Window == null || Window.State == WindowState.Minimized || !(Window.IsDisplayed || Window.ShouldDisplayWindow))
        {
            Answer(onReady, null);
            return;
        }

        // Answered by a frame that applied a record taken after this ask, so it shows what the window held when asked.
        _screenshots.Enqueue(new ScreenshotRequest(onReady, Interlocked.Read(ref _recordsTaken) + 1));
        LoopSignal.Request();
        RequestFrame();
    }

    /// <summary>Counts a record of the window's tree taken on the loop: the record a screenshot asked now waits for.
    /// </summary>
    protected void RecordTaken() => Interlocked.Increment(ref _recordsTaken);

    /// <summary>The records taken so far; read before a frame applies them, and passed to <see cref="RecordsApplied"/>.
    /// </summary>
    protected long RecordsTaken => Interlocked.Read(ref _recordsTaken);

    /// <summary>The frame being drawn shows every record up to <paramref name="taken"/>.</summary>
    protected void RecordsApplied(long taken) => Interlocked.Exchange(ref _recordsApplied, taken);

    // Render thread, the frame just ended: what the window drew is read back from its own target, which the swapchain
    // image is only a copy of. A frame that applied no record taken since an ask leaves it for a later one.
    private void DeliverScreenshots()
    {
        if (_screenshots.IsEmpty)
        {
            return;
        }

        var applied = Interlocked.Read(ref _recordsApplied);
        if (!_screenshots.TryPeek(out var first) || first.After > applied)
        {
            RequestFrame();
            return;
        }

        BitmapSource picture = null;
        try
        {
            picture = ReadFrame();
        }
        catch (Exception ex)
        {
            Log.Logger.Error($"Reading the window's frame back failed: {ex}");
        }

        // Each asker gets a picture of its own to keep or dispose of.
        var handed = false;
        while (_screenshots.TryPeek(out var request) && request.After <= applied && _screenshots.TryDequeue(out request))
        {
            Answer(request.OnReady, handed ? Copy(picture) : picture);
            handed = true;
        }

        if (!_screenshots.IsEmpty)
        {
            RequestFrame();
        }
    }

    // A window minimized or hidden while asked draws no frame to answer from: what it was asked is answered with nothing.
    private void Watch()
    {
        Window.StateChanged += OnWindowStateChanged;
        Window.Hidden += OnWindowHidden;
    }

    private void Unwatch()
    {
        if (Window == null)
        {
            return;
        }

        Window.StateChanged -= OnWindowStateChanged;
        Window.Hidden -= OnWindowHidden;
    }

    private void OnWindowStateChanged(object sender, StateChangedEventArgs e)
    {
        if (Window?.State == WindowState.Minimized)
        {
            AnswerAllWithNothing();
        }
    }

    private void OnWindowHidden(object sender, EventArgs e) => AnswerAllWithNothing();

    private void AnswerAllWithNothing()
    {
        while (_screenshots.TryDequeue(out var request))
        {
            Answer(request.OnReady, null);
        }
    }

    private void Answer(Action<BitmapSource> onReady, BitmapSource picture)
    {
        if (Window?.UIContext?.Dispatcher is { } dispatcher)
        {
            dispatcher.Post(() => onReady(picture));
        }
        else
        {
            onReady(picture);
        }
    }

    private static BitmapSource Copy(BitmapSource picture) => picture?.PixelBytes == null
        ? null
        : new BitmapSource(picture.PixelWidth, picture.PixelHeight, picture.DpiXScale, picture.DpiYScale,
            picture.SurfaceLayout, (byte[])picture.PixelBytes.Clone());

    private BitmapSource ReadFrame()
    {
        if (GraphicsDevice.CurrentRenderTarget?.ResolveTexture is not { } drawn || Presenter == null)
        {
            return null;
        }

        GraphicsDevice.DeviceWaitIdle();
        using var image = drawn.ToImage();
        var pixels = new byte[(int)image.TotalSizeInBytes];
        Marshal.Copy(image.DataPointer, pixels, 0, pixels.Length);
        var density = RenderScale > 0 ? 1.0 / RenderScale : 1.0;
        return new BitmapSource(drawn.Width, drawn.Height, density, density, Presenter.SurfaceFormat, pixels);
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
        Unwatch();
        Window = window;
        Window.Renderer = this;
        FillParameters();
        SubscribeToEvents();
        Watch();
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
        Unwatch();
        Window = window;
        Window.Renderer = this;
        SubscribeToEvents();
        Watch();
        ResizePresenterToWindow();
        InitializeWindowResources();
    }

    /// <summary>
    /// The presenter kind this renderer builds. Defaults to the on-screen swapchain; only a see-through window whose
    /// surface cannot be composed with its alpha, and that can take frames as pixels, is presented through the system's
    /// compositor (<see cref="PresenterType.Compositor"/>) instead. A headless renderer overrides it to
    /// <see cref="PresenterType.Headless"/>, in which case the surface is window-less.
    /// </summary>
    protected virtual PresenterType PresenterKind =>
        Window is { UseTransparentComposition: true, ComposesFromPixels: true } && !SurfaceComposesWithAlpha()
            ? PresenterType.Compositor
            : PresenterType.Swapchain;

    private void FillParameters()
    {
        Parameters = NewParameters(PresenterKind);
        Presenter = GraphicsPresenter.Create(GraphicsDevice, Parameters, "Window_presenter");
    }

    private PresentationParameters NewParameters(PresenterType kind) => NewParameters(kind, WantsAlpha(kind));

    private PresentationParameters NewParameters(PresenterType kind, bool alpha)
    {
        return new PresentationParameters(
            kind,
            (uint)Math.Round(Window.ClientWidth * RenderScale),
            (uint)Math.Round(Window.ClientHeight * RenderScale),
            Window.SurfaceHandle,
            Window.MSAALevel
        )
        {
            HInstanceHandle = OperatingSystem.IsWindows() ? Win32Interop.GetModuleHandle(null) : IntPtr.Zero,
            DisplayHandle = Window.SurfaceDisplayHandle,
            // A see-through window is a property of the WINDOW, so it travels from there to the surface that composes it.
            TransparentComposition = alpha,
            PresentPolicy = ResolvePresentPolicy()
        };
    }

    private bool WantsAlpha(PresenterType kind) => Window.UseTransparentComposition ||
                                                   kind == PresenterType.Swapchain &&
                                                   Window.DrawnOpacity < 1f &&
                                                   SurfaceComposesWithAlpha();

    public void DrawWindowOpacity()
    {
        var opacity = Window?.DrawnOpacity ?? 1f;
        if (opacity >= 1f || Presenter is not SwapChainGraphicsPresenter { ComposesWithAlpha: true } ||
            GraphicsDevice is not { } gd || gd.CurrentViewports.Length == 0)
        {
            return;
        }

        _opacityEffect ??= new WindowOpacityEffect(gd);
        var viewport = gd.CurrentViewports[0];
        var scissor = gd.CurrentScissors.Length > 0 ? gd.CurrentScissors[0] : default;
        var blend = gd.ColorBlendEnabled;
        var equation = gd.ColorBlendEquation;
        var depthTest = gd.DepthTestEnabled;
        var depthWrite = gd.DepthWriteEnable;

        gd.SetScissors(new Rect2D
        {
            Offset = new Offset2D { X = (int)viewport.X, Y = (int)viewport.Y },
            Extent = new Extent2D { Width = (uint)viewport.Width, Height = (uint)viewport.Height }
        });
        gd.ColorComponentFlags = ColorComponentFlagBits.RBit | ColorComponentFlagBits.GBit |
                                 ColorComponentFlagBits.BBit | ColorComponentFlagBits.ABit;
        gd.ColorBlendEnabled = true;
        gd.ColorBlendEquation = ColorBlendEquations.TimesSourceAlpha;
        gd.DepthTestEnabled = false;
        gd.DepthWriteEnable = false;
        gd.StencilTestEnabled = false;
        gd.CullMode = CullModeFlagBits.None;
        gd.PolygonMode = PolygonMode.Fill;
        gd.AlphaToCoverageEnable = false;
        gd.RasterizerDiscardEnabled = false;
        gd.LogicOperationsEnabled = false;
        gd.PrimitiveTopology = PrimitiveTopology.TriangleList;
        gd.VertexType = null;
        _opacityEffect.WindowAlpha.SetValue(opacity);
        _opacityEffect.WindowOpacityDrawPass.Apply();
        gd.Draw(3, 1);

        gd.SetScissors(scissor);
        gd.ColorBlendEnabled = blend;
        gd.ColorBlendEquation = equation;
        gd.DepthTestEnabled = depthTest;
        gd.DepthWriteEnable = depthWrite;
    }

    private bool SurfaceComposesWithAlpha()
    {
        if (_surface == null || _surfaceHandle != Window.SurfaceHandle)
        {
            ReleaseSurface();
            _surface = GraphicsDevice.GetOrCreateSurface(NewParameters(PresenterType.Swapchain, false));
            _surfaceHandle = Window.SurfaceHandle;
            _surfaceComposesWithAlpha = _surface.ComposesWithAlpha(GraphicsDevice.MainDevice);
        }

        return _surfaceComposesWithAlpha;
    }

    private void ReleaseSurface()
    {
        if (_surface == null)
        {
            return;
        }

        GraphicsDevice.ReleaseSurface(_surfaceHandle);
        _surface = null;
    }

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
            var alpha = WantsAlpha(Parameters.PresenterType);
            Presenter.Description.TransparentComposition = alpha;
            Parameters.TransparentComposition = alpha;

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
        if (GraphicsDevice.CanPresent && DrawsRecordedContent)
        {
            DeliverScreenshots();
        }
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
            if (Presenter is CompositorPresenter { Frame: { } frame })
            {
                Window.Compose(frame);
            }

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
        AnswerAllWithNothing();
        Unwatch();
        UnsubscribeFromEvents();
        Presenter?.Dispose();
        Presenter = null;
        _opacityEffect?.Dispose();
        _opacityEffect = null;
        ReleaseSurface();
        // Last: the units built from it are gone by now.
        (RenderUnitFactory as IDisposable)?.Dispose();
    }
}
