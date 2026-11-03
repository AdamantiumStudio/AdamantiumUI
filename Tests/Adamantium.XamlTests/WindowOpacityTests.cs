using System;
using System.Threading;
using System.Threading.Tasks;
using Adamantium.Core;
using Adamantium.Core.DependencyInjection;
using Adamantium.Graphics.Core;
using Adamantium.Graphics.Core.Presentation;
using Adamantium.Mathematics;
using Adamantium.UI.Controls;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Dispatcher;
using Adamantium.UI.Core.Graphics;
using NUnit.Framework;

namespace Adamantium.XamlTests;

// WindowOpacity: the desktop lays the window down with it where the platform can (Win32, Wayland's alpha modifier);
// elsewhere each frame is drawn with it, through a swapchain the desktop composes with alpha.
[TestFixture]
public class WindowOpacityTests
{
    private FakeApp _app;
    private FakeContext _context;
    private IUIApplication _previousApp;
    private IWindowPlatformService _previousPlatform;

    [SetUp]
    public void SetUp()
    {
        var resolver = new AdamantiumDependencyContainer();
        _app = new FakeApp(resolver) { Dispatcher = new InlineDispatcher() };
        _context = new FakeContext(resolver) { UIApplication = _app };
        _previousApp = UIAppContext.Current;
        _previousPlatform = UIAppContext.PlatformService;
        UIAppContext.Replace(_app, _previousPlatform);
    }

    [TearDown]
    public void TearDown() => UIAppContext.Replace(_previousApp, _previousPlatform);

    [Test]
    public void WhereTheDesktopDoesNotApplyIt_TheFrameIsDrawnWithIt()
    {
        var (window, renderer) = Open(appliesOpacity: false);

        window.WindowOpacity = 0.5;

        Assert.That(window.DrawnOpacity, Is.EqualTo(0.5f));
        Assert.That(window.Worker.TraitUpdates, Is.EqualTo(1));
        Assert.That(renderer.Invalidations, Is.EqualTo(1), "the swapchain must be rebuilt to compose with alpha");
        Assert.That(renderer.FrameRequests, Is.EqualTo(1));
    }

    [Test]
    public void WhereTheDesktopAppliesIt_TheFrameIsDrawnOpaque()
    {
        var (window, renderer) = Open(appliesOpacity: true);

        window.WindowOpacity = 0;

        Assert.That(window.DrawnOpacity, Is.EqualTo(1f));
        Assert.That(window.Worker.TraitUpdates, Is.EqualTo(1));
        Assert.That(renderer.Invalidations, Is.Zero);
        Assert.That(renderer.FrameRequests, Is.Zero);
    }

    [Test]
    public void AChangeBelowOne_RedrawsWithoutARebuild()
    {
        var (window, renderer) = Open(appliesOpacity: false);
        window.WindowOpacity = 0.5;

        window.WindowOpacity = 0;

        Assert.That(window.DrawnOpacity, Is.Zero);
        Assert.That(renderer.Invalidations, Is.EqualTo(1));
        Assert.That(renderer.FrameRequests, Is.EqualTo(2));
    }

    [Test]
    public void BackToOne_RebuildsTheSwapchainOpaque()
    {
        var (window, renderer) = Open(appliesOpacity: false);
        window.WindowOpacity = 0;

        window.WindowOpacity = 1;

        Assert.That(window.DrawnOpacity, Is.EqualTo(1f));
        Assert.That(renderer.Invalidations, Is.EqualTo(2));
    }

    [Test]
    public void SetBeforeTheWindowOpens_ItIsDrawnFromTheFirstFrame()
    {
        var window = new TestWindow(new RecordingWorker(appliesOpacity: false)) { ActivateOnShow = false };
        window.WindowOpacity = 0.25;

        window.AttachContextAndInitialize(_context);

        Assert.That(window.DrawnOpacity, Is.EqualTo(0.25f));
    }

    [Test]
    public void OutOfRange_IsDrawnClamped()
    {
        var (window, _) = Open(appliesOpacity: false);

        window.WindowOpacity = -1;

        Assert.That(window.DrawnOpacity, Is.Zero);
    }

    private (TestWindow Window, RecordingRenderer Renderer) Open(bool appliesOpacity)
    {
        var window = new TestWindow(new RecordingWorker(appliesOpacity)) { ActivateOnShow = false };
        window.AttachContextAndInitialize(_context);
        var renderer = new RecordingRenderer();
        window.Renderer = renderer;
        return (window, renderer);
    }

    private sealed class TestWindow(RecordingWorker worker) : Window
    {
        public RecordingWorker Worker { get; } = worker;

        protected override IWindowWorkerService CreateWindowWorker(IUIContext context) => Worker;
    }

    private sealed class RecordingRenderer : IWindowRenderer
    {
        public int Invalidations { get; private set; }

        public long FrameRequests { get; private set; }

        public void InvalidatePresenter() => Invalidations++;

        public void RequestFrame() => FrameRequests++;

        public IDrawingContext DrawingContext => null;
        public bool IsRendererUpToDate => true;
        public bool FirstFrameProcessed => true;
        public long PresentedFrames => 0;
        public bool HasPresentedContent => true;
        public int RecordedFrames => 0;
        public int ChangedFrames => 0;
        public double RenderScale { get; set; } = 1.0;
        public GraphicsPresenter Presenter => null;

        public void SetWindow(IWindow window) => throw new NotSupportedException();
        public void Retarget(IWindow window) => throw new NotSupportedException();
        public void Render(AppTime appTime) => throw new NotSupportedException();
        public void PreRender() => throw new NotSupportedException();
        public void PrepareData() => throw new NotSupportedException();
        public void RecordData() => throw new NotSupportedException();
        public void ApplyData() => throw new NotSupportedException();
        public void ResetCache() => throw new NotSupportedException();
        public void OnFrameEnded() => throw new NotSupportedException();
        public void Present() => throw new NotSupportedException();
        public void ResizePresenter(PresentationParameters parameters) => throw new NotSupportedException();
        public void ResizePresenter(uint width, uint height) => throw new NotSupportedException();
        public void ResizePresenterToWindow() => throw new NotSupportedException();
        public void Dispose() { }
    }

    private sealed class InlineDispatcher : IDispatcher
    {
        public bool IsRunning => true;
        public void Run(CancellationToken token) { }
        public bool CheckAccess() => true;
        public void VerifyAccess() { }
        public Thread MainThread => Thread.CurrentThread;
        public Thread UIThread { get; set; } = Thread.CurrentThread;
        public void Invoke(Action action, DispatcherPriority priority = DispatcherPriority.Normal) => action();
        public void Invoke(Delegate action, object args) => action.DynamicInvoke(args);
        public Task InvokeAsync(Action action, DispatcherPriority priority = DispatcherPriority.Normal)
        {
            action();
            return Task.CompletedTask;
        }
        public Task InvokeAsync(Delegate action, object args)
        {
            action.DynamicInvoke(args);
            return Task.CompletedTask;
        }
        public void Post(Action action) => action();
    }

    private sealed class RecordingWorker(bool appliesOpacity) : IWindowWorkerService
    {
        public int TraitUpdates { get; private set; }

        public bool AppliesWindowOpacity => appliesOpacity;

        public void UpdateOverlayTraits() => TraitUpdates++;

        public void SetWindow(IWindow window) => ((WindowBase)window).SetHandle(new IntPtr(1));

        public void SetTitle(string title) { }
        public void SetPosition(double left, double top) { }
        public void SetSize(double clientWidth, double clientHeight) { }
        public Rect RestoreBounds => default;
        public void ShowWindow(WindowState windowState) { }
        public void HideWindow() { }
        public void Activate() { }
        public void RaiseWithoutActivation() { }
        public void SetMouseCapture(bool capture) { }
        public PixelPoint ScreenToClient(PixelPoint screen) => screen;
        public PixelPoint ClientToScreen(PixelPoint client) => client;
        public void SetRelativeMouseMode(bool enabled, PixelPoint restoreScreen) { }
        public void SetInputMethodEnabled(bool enabled) { }
        public void BeginMoveDrag() { }
        public IUIContext UIContext => null;
    }
}
