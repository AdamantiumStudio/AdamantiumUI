using System;
using System.Diagnostics;
using System.Threading;
using Adamantium.Core;
using Adamantium.ECS;
using Adamantium.Imaging;
using Adamantium.UI.Core.Diagnostics;
using Adamantium.Graphics.Core;
using Adamantium.Graphics.Core.Extensions;
using Adamantium.Graphics.Core.Presentation;
using Adamantium.Mathematics;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Graphics;
using Adamantium.UI.Core.Media;
using Adamantium.UI.Core.Media.Animation;
using Adamantium.UI.Core.Resources;
using Adamantium.UI.Core.RoutedEvents;
using Adamantium.UI.Extensions;
using Adamantium.UI.Rendering;

namespace Adamantium.UI.EntityServices;

public class WindowRenderService : UiRenderService
{
    private const int MaxRecordedFrames = 64;
    private const double UnknownDisplayRate = 60;

    private IWindowRenderer windowRenderer;
    private IWindowRenderer _pendingRenderer;
    private IThemeManager _themeManager;
    private readonly IGraphicsDevice _injectedDevice;
    private long _seenRequests = -1;
    private long _claimedRequests;
    private bool _drawsThisFrame;
    private bool _canDraw;
    private bool _trailing;
    private volatile bool _unlimited;

    public IWindow Window { get; private set; }

    /// <summary>The zoom the content is rendered at; 1 is on-screen 1:1.</summary>
    public double RenderScale => windowRenderer?.RenderScale ?? 1.0;

    /// <summary>A constructor of its own: Activator.CreateInstance matches the argument count exactly and ignores
    /// optional parameters.</summary>
    public WindowRenderService(EntityWorld world, IWindow window) : this(world, window, null)
    {
    }

    /// <summary>Designer path: the whole session shares one injected render device.</summary>
    public WindowRenderService(EntityWorld world, IWindow window, IGraphicsDevice renderDevice)
        : base(world)
    {
        Window = window;
        _injectedDevice = renderDevice;
        _themeManager = DependencyResolver.Resolve<IThemeManager>();
        CreateResources();
        ResolveRefreshRates(UIApplication.Current);
    }

    private void CreateResources()
    {
        GraphicsDevice = _injectedDevice ?? GraphicsDeviceService.CreateRenderDevice();

        windowRenderer = Window.Renderer ?? CreateRenderer();
        windowRenderer.SetWindow(Window);
        Window.DefaultRenderer = windowRenderer;
        Window.RendererChanged += WindowOnRendererChanged;

        AttachProcessor(new AdornerRenderProcessor());
        AttachProcessor(new PopupRenderProcessor());
    }

    /// <summary>The renderer this service drives; the headless designer renders into a texture instead of a swapchain.</summary>
    protected virtual IWindowRenderer CreateRenderer() =>
        new ForwardWindowRenderer(GraphicsDevice, new RenderUnitFactory(GraphicsDevice, DependencyResolver.Resolve<IResourceFactory>()));

    private void WindowOnRendererChanged(object sender, WindowRendererChangedEventArgs e)
    {
        _pendingRenderer = e.NewRenderer;
    }

    public override void Present()
    {
        var t0 = Stopwatch.GetTimestamp();
        windowRenderer?.Present();
        RuntimeStats.LastPresentMs = Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
    }

    public override void UnloadContent()
    {
        GraphicsDevice?.DeviceWaitIdle();
        // A renderer belongs to one device: left on the window, the next service would build on a dead one after a swap.
        Window.RendererChanged -= WindowOnRendererChanged;
        if (ReferenceEquals(Window.Renderer, windowRenderer))
        {
            Window.Renderer = null;
        }
        if (ReferenceEquals(Window.DefaultRenderer, windowRenderer))
        {
            Window.DefaultRenderer = null;
        }
        windowRenderer?.Dispose();
        // The stages go with the window: each frees its GPU resources, and a detached stage forgets its marks.
        base.UnloadContent();
        for (var i = Processors.Count - 1; i >= 0; i--)
        {
            DetachProcessor(Processors[i]);
        }

        // The designer's injected device belongs to its session.
        if (_injectedDevice == null && GraphicsDevice != null)
            GraphicsDeviceService.MainGraphicsDevice.RemoveDevice(GraphicsDevice);
    }

    public override bool IsUpdateService => true;
    public override EntityServiceType ServiceType => EntityServiceType.Update | EntityServiceType.Render;

    public override void Update(AppTime appTime)
    {
        Window.Update(_themeManager, appTime);
        base.Update(appTime);
    }

    private bool _recordedAtLoopLevel;

    /// <summary>Records this window's packet on the loop thread. False when it could not: the caller must then keep
    /// RenderDirty, or the marks it never recorded are lost.</summary>
    public bool RecordFrame()
    {
        if (windowRenderer is not { IsRendererUpToDate: true })
        {
            _recordedAtLoopLevel = false;
            return false;
        }
        windowRenderer.RecordData();
        RecordStages();
        _recordedAtLoopLevel = true;
        return true;
    }

    private void RecordStages()
    {
        foreach (var processor in Processors)
        {
            if (processor is IRecordingStage stage)
            {
                stage.Record();
            }
        }
    }

    private bool StageHasRecordedFrame()
    {
        foreach (var processor in Processors)
        {
            if (processor is IRecordingStage { HasRecordedFrame: true })
            {
                return true;
            }
        }

        return false;
    }

    private bool StageAwaitsFrame()
    {
        foreach (var processor in Processors)
        {
            if (processor is IRecordingStage { AwaitsFrame: true })
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Hidden by its platform: nothing is drawn into the window until it is shown again.</summary>
    internal bool Hidden { get; set; }

    /// <summary>When this window's frames are due.</summary>
    internal FramePacer Pacer { get; } = new();

    /// <summary>Loop thread: hands the pacer the window's rates; gives the fastest it takes frames at, 0 unlimited.</summary>
    internal double ResolveRefreshRates(UIApplication application)
    {
        _unlimited = UIApplication.RunsUnlimited(Window);
        if (_unlimited)
        {
            return 0;
        }

        var display = Window.DisplayRefreshRate > 0 ? Window.DisplayRefreshRate : UnknownDisplayRate;
        double steady = Window.RefreshRate ?? application?.RefreshRate ?? 0;
        double minimum = Window.MinimumRefreshRate ?? application?.MinimumRefreshRate ?? 0;
        var maximum = (double?)(Window.MaximumRefreshRate ?? application?.MaximumRefreshRate) ?? display;
        Pacer.SetRates(steady, minimum, maximum);
        return steady > 0 ? steady : maximum;
    }

    /// <summary>The window draws in the frame last claimed.</summary>
    internal bool DrawsThisFrame => _drawsThisFrame;

    /// <summary>The window could draw when its frame was last claimed: not minimized, hidden or without a presenter.</summary>
    internal bool CanDraw => _canDraw;

    /// <summary>A frame recorded for the window waits to show something new.</summary>
    internal bool HasRecordedChange => windowRenderer?.ChangedFrames > 0 || StageHasRecordedFrame();

    /// <summary>Its swapchain lagged its surface when its frame was last claimed: it is being resized.</summary>
    internal bool IsTrailing => _trailing;

    /// <summary>Render thread: decides whether the window draws in the coming frame; gives when its frame is due.</summary>
    internal long ClaimFrame(long now)
    {
        _drawsThisFrame = false;
        _trailing = false;
        _canDraw = windowRenderer?.Presenter != null && Window.State != WindowState.Minimized && !Hidden;
        if (!_canDraw)
        {
            return FramePacer.Never;
        }

        var requests = windowRenderer.FrameRequests;
        var presenter = windowRenderer.Presenter;
        var recorded = windowRenderer.RecordedFrames;
        _trailing = presenter.NeedsRebuild;
        var rebuild = !windowRenderer.IsRendererUpToDate || _trailing || _pendingRenderer != null
                      || recorded >= MaxRecordedFrames;
        var news = requests != _seenRequests || HasRecordedChange || Compositor.IsPlayingIn(Window)
                   || presenter.LastPresenterState is PresenterState.OutOfDate or PresenterState.Suboptimal
                   || Graphics.Fonts.FontAtlasStore.HasPendingGlyphs;
        var due = rebuild || _unlimited ? now : Pacer.Due(now, news);
        if (due > now)
        {
            return due;
        }

        _drawsThisFrame = true;
        _claimedRequests = requests;
        Pacer.Drew(now, due);
        return due;
    }

    public override bool BeginDraw()
    {
        // Unloaded, but still in the draw list until its removal propagates.
        if (windowRenderer?.Presenter == null) return false;

        if (Window.State == WindowState.Minimized || Hidden)
        {
            return false;
        }

        if (Thread.CurrentThread == RenderThreadOptions.RenderThread && !_drawsThisFrame)
        {
            return false;
        }

        // App-global, but windows render one after another.
        Rendering.RenderUnits.AnalyticAa.Enabled = Window?.AnalyticAntialiasing ?? true;
        GraphicsDevice.ClearColor = (Window?.Background as SolidColorBrush)?.Color ?? Colors.Black;
        var trailing = windowRenderer.Presenter?.NeedsRebuild ?? false;
        if (RenderThreadOptions.RenderThread == null)
        {
            UIApplication.SwapchainTrailing = trailing;
        }

        if (trailing)
        {
            Core.LoopSignal.Request();
        }

        // Rebuilt here, on this thread, so nothing is submitted against a stale swapchain. Suboptimal counts too: that is
        // how the driver reports a stale image once the swapchain says what it does on a mismatch.
        if (windowRenderer.Presenter != null &&
            (!windowRenderer.IsRendererUpToDate
             || windowRenderer.Presenter.LastPresenterState is PresenterState.OutOfDate or PresenterState.Suboptimal
             || trailing))
        {
            windowRenderer.ResizePresenterToWindow();
        }

        GraphicsDevice.SetRenderTargets(windowRenderer.Presenter.RenderTarget);
        GraphicsDevice.SetDepthBuffer(windowRenderer.Presenter.DepthBuffer);
        GraphicsDevice.MSAALevel = windowRenderer.Presenter.MSAALevel;
        GraphicsDevice.Presenter = windowRenderer.Presenter;
        var beginStart = Stopwatch.GetTimestamp();
        try
        {
            return GraphicsDevice.BeginDraw(beforeRenderPass: _ =>
            {
                // After the fence wait and before the pass, so this frame draws this frame's tree. Only the loop thread
                // records: the render thread would race the loop's own record on the same cache.
                var onRenderThread = Thread.CurrentThread == RenderThreadOptions.RenderThread;
                if (RenderThreadOptions.SingleThreaded || (!_recordedAtLoopLevel && !onRenderThread))
                {
                    windowRenderer.PrepareData();
                    RecordStages();
                }
                else
                {
                    windowRenderer.ApplyData();
                }
                _seenRequests = _claimedRequests;
                windowRenderer.PreRender();
                PreRenderProcessors();
                if (StageAwaitsFrame())
                {
                    windowRenderer.RequestFrame();
                }
            });
        }
        finally
        {
            RuntimeStats.LastBeginDrawMs = Stopwatch.GetElapsedTime(beginStart).TotalMilliseconds;
        }
    }

    public override void Draw(AppTime appTime)
    {
        windowRenderer?.Render(appTime);
        var t0 = Stopwatch.GetTimestamp();
        DrawProcessors(appTime);
        RuntimeStats.LastProcessorsMs = Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
        if (StageMoves())
        {
            windowRenderer?.RequestFrame();
        }
    }

    private bool StageMoves()
    {
        foreach (var processor in Processors)
        {
            if (processor is IRecordingStage { Moves: true })
            {
                return true;
            }
        }

        return false;
    }

    public override void EndDraw()
    {
        var t0 = Stopwatch.GetTimestamp();
        RuntimeStats.LastWindowGpuMs = GraphicsDevice.LastGpuFrameMs;
        GraphicsDevice.EndDraw();
        // A failed acquire leaves nothing to copy into and nothing to present.
        if (GraphicsDevice.HasSwapchainImage)
        {
            GraphicsDevice.BlitImage(GraphicsDevice.CurrentCommandBuffer,
                GraphicsDevice.CurrentRenderTarget.ResolveTexture,
                windowRenderer.Presenter.GetCurrentImage());
        }

        RuntimeStats.LastEndDrawMs = Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
    }

    public override void FrameEnded()
    {
        base.FrameEnded();
        GraphicsDevice.FrameEnded();
        windowRenderer.OnFrameEnded();
        if (!windowRenderer.IsRendererUpToDate)
        {
            windowRenderer.ResizePresenterToWindow();
        }

        if (_pendingRenderer == null) return;

        windowRenderer = _pendingRenderer;
        _pendingRenderer = null;
    }

    /// <summary>Designer one-shot frame: the loop's renderer and processors, then a wait for GPU idle so the result can
    /// be read back. Window.Update is the caller's.</summary>
    public bool RenderHeadlessFrame(IWindow window, double renderScale, AppTime time)
    {
        if (!ReferenceEquals(Window, window) || System.Math.Abs(windowRenderer.RenderScale - renderScale) > 1e-9)
        {
            windowRenderer.RenderScale = renderScale;
            RebindWindow(window);
        }

        UpdateProcessors(time);

        // A frame held back for want of room is drawn again at once, its buffers grown: nobody asks for the next one here.
        for (var attempt = 0; attempt < 2; attempt++)
        {
            if (!BeginDraw())
                throw new System.InvalidOperationException(
                    $"GraphicsDevice.BeginDraw failed: {GraphicsDevice.LastFrameError ?? "unknown device error"}");
            windowRenderer.Render(time);
            DrawProcessors(time);

            GraphicsDevice.EndDraw();          // not this.EndDraw(): no swapchain blit
            GraphicsDevice.Submit();
            GraphicsDevice.DeviceWaitIdle();
            GraphicsDevice.FrameEnded();
            if (!GraphicsDevice.FrameWithheld)
            {
                break;
            }
        }

        // What changed is recorded now, as the loop clears after its draw; left, the marks would hold every element they
        // name for the whole designer session.
        RenderDirty.Clear();
        return true;
    }

    /// <summary>Drops the renderer's cached units; the designer calls this on a full (non-reconcile) rebuild.</summary>
    public void ResetFrameCache() => windowRenderer.ResetCache();

    /// <summary>Designer: previews another window on the same device, renderer and presenter.</summary>
    public void RebindWindow(IWindow window)
    {
        Window = window;
        windowRenderer.Retarget(window);
    }

    /// <summary>Writes the last rendered frame as raw B8G8R8A8 (no encode) - the designer's per-frame transport.</summary>
    public void SaveFrameRaw(string path) => windowRenderer.Presenter.RenderTarget.ResolveTexture.SaveRaw(path);

    /// <summary>Saves the last rendered frame to an image file.</summary>
    public void SaveFrame(string path, ImageFileType fileType) => windowRenderer.Presenter.RenderTarget.ResolveTexture.Save(path, fileType);
}
