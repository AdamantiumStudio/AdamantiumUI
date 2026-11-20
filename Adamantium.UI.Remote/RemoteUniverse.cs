using Adamantium.Graphics.Core;
using Adamantium.Multiverse.Input;
using Adamantium.Multiverse.Remote;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Panels;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Input;
using Adamantium.UI.Core.RoutedEvents;
using Adamantium.UI.Universes;

namespace Adamantium.UI.Remote;

/// <summary>A universe host's frames shown in a panel, the frames paced by the ones the panel shows, and the panel's size,
/// pointer, focus and input given to the host.</summary>
internal sealed class RemoteUniverse : IRemoteUniverse, IUniverseInputTarget
{
    private readonly object _sync = new();
    private readonly Guid _deviceUuid;
    private readonly HashSet<Keys> _keysDown = [];
    private readonly TaskCompletionSource _ended = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Action _frameTaken;
    private RenderTargetPanel _panel;
    private UniverseInputListener _input;
    private IWindow _window;
    private UniverseHostConnection _connection;
    private IUniverseHostService _host;
    private RemoteSurface _surface;
    private SharedSurfaceDescriptor _descriptor;
    private uint _width;
    private uint _height;
    private bool _disposed;

    public RemoteUniverse(Guid deviceUuid)
    {
        _deviceUuid = deviceUuid;
        Viewer = new RemoteUniverseViewer(this);
        _frameTaken = OnFrameTaken;
    }

    /// <summary>What the host tells, given to <see cref="UniverseHostConnection.ConnectAsync"/>.</summary>
    public IUniverseViewer Viewer { get; }

    public Task Ended => _ended.Task;

    public RenderTargetPanel Panel => _panel;

    /// <summary>Whether the host has shown a surface not let go of since.</summary>
    internal bool HasSurface
    {
        get
        {
            lock (_sync)
            {
                return _descriptor != null;
            }
        }
    }

    public float PixelsPerPoint => _window is { } window ? (float)window.DpiScale.X : 1;

    public bool TakesKeyboard => HasKeyboard;

    private bool HasKeyboard => _panel is { IsKeyboardFocused: true } && _window is { IsActive: true };

    /// <summary>Takes the connection the viewer was given to; the host is called through it from now on.</summary>
    public void Attach(UniverseHostConnection connection)
    {
        lock (_sync)
        {
            _connection = connection;
            _host = connection.Host;
        }

        connection.Completion.ContinueWith(_ => LoseHost(), TaskScheduler.Default);
    }

    public void ShowIn(RenderTargetPanel panel)
    {
        if (ReferenceEquals(panel, _panel) || _disposed && panel != null)
        {
            return;
        }

        LeavePanel();
        if (panel == null)
        {
            return;
        }

        lock (_sync)
        {
            _panel = panel;
            if (_descriptor != null)
            {
                panel.SetSource(_descriptor);
            }
        }

        _input = new UniverseInputListener(panel, this);
        panel.SizeChanged += OnSizeChanged;
        panel.PointerMoved += OnPointerMoved;
        panel.PointerExited += OnPointerExited;
        panel.GotFocus += OnFocusChanged;
        panel.LostFocus += OnFocusChanged;
        panel.AttachedToVisualTreeEvent += OnAttachedToVisualTree;
        WatchWindow();
        Resize();
        TellFocus();
    }

    public async Task LaunchAsync(UniverseLaunch launch, CancellationToken cancellationToken)
    {
        MeasurePanel(out var width, out var height);
        if (width > 0 && height > 0)
        {
            launch.Width = width;
            launch.Height = height;
        }

        _width = launch.Width;
        _height = launch.Height;
        launch.ViewerDeviceUuid = _deviceUuid;
        await Host.LaunchAsync(launch, cancellationToken);
        Host.Resize(_width, _height, PixelsPerPoint);
        Host.Focus(HasKeyboard);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Host.StopAsync(cancellationToken);

    public Task<string> CaptureSceneAsync(CancellationToken cancellationToken) => Host.CaptureSceneAsync(cancellationToken);

    public Task<byte[]> CaptureFrameAsync(CancellationToken cancellationToken) => Host.CaptureFrameAsync(cancellationToken);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        LeavePanel();
        UniverseHostConnection connection;
        lock (_sync)
        {
            _disposed = true;
            connection = _connection;
            _connection = null;
            _host = null;
            LetGoOfSurface();
        }

        connection?.Dispose();
        _ended.TrySetResult();
    }

    private IUniverseHostService Host
    {
        get
        {
            lock (_sync)
            {
                return _host ?? throw new ObjectDisposedException(nameof(RemoteUniverse));
            }
        }
    }

    // The host called while there is one: input before the connection, or after it went, is dropped.
    private IUniverseHostService LiveHost
    {
        get
        {
            lock (_sync)
            {
                return _host;
            }
        }
    }

    // The panel let go of: its picture, its input and the keyboard it gave the host.
    private void LeavePanel()
    {
        var panel = _panel;
        if (panel == null)
        {
            return;
        }

        _input.Dispose();
        _input = null;
        panel.SizeChanged -= OnSizeChanged;
        panel.PointerMoved -= OnPointerMoved;
        panel.PointerExited -= OnPointerExited;
        panel.GotFocus -= OnFocusChanged;
        panel.LostFocus -= OnFocusChanged;
        panel.AttachedToVisualTreeEvent -= OnAttachedToVisualTree;
        UnwatchWindow();
        lock (_sync)
        {
            if (_descriptor != null)
            {
                panel.ClearSource(_descriptor);
            }

            _panel = null;
        }

        _width = 0;
        _height = 0;
        TellFocus();
        LiveHost?.MovePointer(0, 0, false);
    }

    // The host's process went: nothing is sent to it any more, and its last surface is let go of.
    private void LoseHost()
    {
        lock (_sync)
        {
            _host = null;
            LetGoOfSurface();
        }

        _ended.TrySetResult();
    }

    internal void ShowSurface(RemoteSurface surface)
    {
        lock (_sync)
        {
            if (_disposed)
            {
                surface.CloseHandles();
                return;
            }

            var previous = _surface;
            _surface = surface;
            _descriptor = surface.ToDescriptor();
            // Once bound, the panel imports no more of the previous one, so its handles are done with.
            _panel?.SetSource(_descriptor);
            previous?.CloseHandles();
            AskFrameOnceShown(_descriptor);
        }

        RequestPanelFrame();
    }

    internal void ShowFrame(ulong frame)
    {
        lock (_sync)
        {
            _descriptor?.NoteSubmitted(frame);
        }

        RequestPanelFrame();
    }

    internal void EndUniverse()
    {
        lock (_sync)
        {
            LetGoOfSurface();
        }

        _ended.TrySetResult();
    }

    // Each time the panel takes a frame of the surface, the host is asked for the next: one drawn per frame shown.
    private void AskFrameOnceShown(SharedSurfaceDescriptor descriptor)
    {
        descriptor.Demand.WhenBeyond(descriptor.Demand.Count, _frameTaken);
    }

    // On the panel's render thread.
    private void OnFrameTaken()
    {
        SharedSurfaceDescriptor descriptor;
        IUniverseHostService host;
        lock (_sync)
        {
            descriptor = _descriptor;
            host = _host;
        }

        if (descriptor == null || host == null)
        {
            return;
        }

        host.AskFrame(descriptor.Submitted + 1);
        descriptor.Demand.WhenBeyond(descriptor.Demand.Count, _frameTaken);
    }

    private void LetGoOfSurface()
    {
        if (_descriptor != null)
        {
            _panel?.ClearSource(_descriptor);
        }

        _surface?.CloseHandles();
        _surface = null;
        _descriptor = null;
    }

    private void RequestPanelFrame()
    {
        RenderTargetPanel panel;
        lock (_sync)
        {
            panel = _panel;
        }

        if (panel?.RootVisual?.Window is { } window)
        {
            (window.Renderer ?? window.DefaultRenderer)?.RequestFrame();
        }
    }

    private void MeasurePanel(out uint width, out uint height)
    {
        var scale = PixelsPerPoint;
        width = _panel == null ? 0 : (uint)Math.Round(_panel.ActualWidth * scale);
        height = _panel == null ? 0 : (uint)Math.Round(_panel.ActualHeight * scale);
    }

    private void Resize()
    {
        MeasurePanel(out var width, out var height);
        if (width == 0 || height == 0 || width == _width && height == _height)
        {
            return;
        }

        _width = width;
        _height = height;
        LiveHost?.Resize(width, height, PixelsPerPoint);
    }

    // SizeChanged bubbles up from children, so the size is read from the panel itself.
    private void OnSizeChanged(object sender, SizeChangedEventArgs e) => Resize();

    private void OnPointerMoved(object sender, PointerEventArgs e) =>
        MovePointer(e, _panel.IsPointerDirectlyOver || _panel.IsPointerCaptured);

    // Raised before the panel stops being directly under the pointer, so that is not asked: only a captured one stays.
    private void OnPointerExited(object sender, PointerEventArgs e) => MovePointer(e, _panel.IsPointerCaptured);

    private void MovePointer(PointerEventArgs e, bool isOver)
    {
        var at = e.GetPosition(_panel);
        var scale = PixelsPerPoint;
        LiveHost?.MovePointer((float)(at.X * scale), (float)(at.Y * scale), isOver);
    }

    private void OnFocusChanged(object sender, RoutedEventArgs e) => TellFocus();

    // The host lets go of every key once it loses the keyboard, and so does the viewer: a key let go elsewhere would
    // stay held here, and a modifier held for the next press would not be sent.
    private void TellFocus()
    {
        var hasKeyboard = HasKeyboard;
        if (!hasKeyboard)
        {
            _keysDown.Clear();
        }

        LiveHost?.Focus(hasKeyboard);
    }

    private void OnAttachedToVisualTree(object sender, VisualTreeAttachmentEventArgs e)
    {
        WatchWindow();
        Resize();
    }

    private void WatchWindow()
    {
        var window = _panel.RootVisual?.Window;
        if (ReferenceEquals(window, _window))
        {
            return;
        }

        UnwatchWindow();
        _window = window;
        if (window != null)
        {
            window.DpiChanged += OnDpiChanged;
            window.PropertyChanged += OnWindowPropertyChanged;
        }
    }

    private void UnwatchWindow()
    {
        if (_window == null)
        {
            return;
        }

        _window.DpiChanged -= OnDpiChanged;
        _window.PropertyChanged -= OnWindowPropertyChanged;
        _window = null;
    }

    private void OnDpiChanged(object sender, EventArgs e)
    {
        _width = 0;
        Resize();
    }

    private void OnWindowPropertyChanged(object sender, AdamantiumPropertyChangedEventArgs e)
    {
        if (e.Property == WindowBase.IsActiveProperty)
        {
            TellFocus();
        }
    }

    bool IUniverseInputTarget.IsKeyDown(Keys key) => _keysDown.Contains(key);

    bool IUniverseInputTarget.IsUniverseInput(RoutedEventArgs e) => ReferenceEquals(e.OriginalSource, _panel);

    bool IUniverseInputTarget.SpendsKey(KeyEventArgs e) => _panel.SpendsKey(e);

    void IUniverseInputTarget.TakeKeyboardInput(KeyboardInput input)
    {
        if (input.InputType == InputType.Down)
        {
            _keysDown.Add(input.Key);
        }
        else if (input.InputType == InputType.Up)
        {
            _keysDown.Remove(input.Key);
        }

        LiveHost?.TakeKeyboardInput(input);
    }

    void IUniverseInputTarget.TakeMouseInput(MouseInput input) => LiveHost?.TakeMouseInput(input);

    void IUniverseInputTarget.TakeGestureInput(GestureInput input) => LiveHost?.TakeGestureInput(input);

    void IUniverseInputTarget.TakeText(string text) => LiveHost?.TakeText(text);
}
