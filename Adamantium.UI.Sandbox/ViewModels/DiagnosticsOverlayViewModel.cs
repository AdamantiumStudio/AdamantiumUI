using System;
using System.Threading;
using System.Threading.Tasks;
using Adamantium.MVVM;
using Adamantium.Navigation;
using Adamantium.UI.Core.Diagnostics;
using Adamantium.UI.Core.Dispatcher;
using Adamantium.UI.Diagnostics;
using Adamantium.UI.Sandbox.Behaviors;
using Adamantium.UI.Sandbox.Localization;

namespace Adamantium.UI.Sandbox.ViewModels;

/// <summary>The live diagnostics in a window of their own over the page: moved where they do not cover what is watched,
/// collapsed to their title when not read. Opened by a Diagnostics switch, closed by it - the token it is shown with - or
/// by the window's own close button. A window with a presentation of its own - the ribbon's swapchain - passes itself as
/// "presentation", and its choice shows above the statistics. Opens collapsed; its title carries the render and loop
/// rates, so they read with the statistics put away.</summary>
[ViewModel]
public partial class DiagnosticsOverlayViewModel : AdamantiumViewModel, IOverlayAware, IDisposable
{
    private const double RatesSeconds = 0.5;

    private readonly IApplicationDiagnostics _diagnostics;
    private readonly DispatcherTimer _rates = new() { Interval = TimeSpan.FromSeconds(RatesSeconds) };
    private CancellationTokenRegistration _closeWhenCanceled;
    private long _loopFrames;
    private long _lastLoopFrames, _lastPresented, _lastSample;
    private long _lastOwn;

    // Opened at the window's bottom-left, out of the way of what is watched: the room from its left and bottom edges.
    [Bindable] private double left = 16;
    [Bindable] private double top = 16;
    [Bindable] private bool isCollapsed = true;

    // One width collapsed and expanded: sized to its title, the window would follow the rates as they change.
    [Bindable] private double width = 300;
    [Bindable] private string title = MainStrings.Diagnostics;

    public DiagnosticsOverlayViewModel(IApplicationDiagnostics diagnostics)
    {
        _diagnostics = diagnostics;
    }

    public bool CanCollapse => true;

    // The title carries the rates, so automation finds the window by this.
    public string AutomationId => "Diagnostics";

    public OverlayStartupLocation StartupLocation => OverlayStartupLocation.BottomLeft;

    /// <summary>The window whose presentation is chosen here - its PresentPolicies and PresentPolicy - if it has its own.</summary>
    public object Presentation { get; private set; }

    public event Action<object> RequestClose;

    public Task OnOverlayOpenedAsync(NavigationParameters parameters, CancellationToken cancellationToken = default)
    {
        if (parameters != null && parameters.TryGetValue<object>("presentation", out var presentation))
        {
            Presentation = presentation;
        }

        _closeWhenCanceled = cancellationToken.Register(() => RequestClose?.Invoke(null));
        if (!cancellationToken.IsCancellationRequested) StartRates();
        return Task.CompletedTask;
    }

    private void StartRates()
    {
        _lastPresented = Interlocked.Read(ref RuntimeStats.PresentedFrames);
        _lastSample = System.Diagnostics.Stopwatch.GetTimestamp();
        _lastOwn = DiagnosticsOverlayBehavior.OwnFrames;
        _diagnostics.CycleFinished += OnCycleFinished;
        _rates.Tick += OnRatesTick;
        _rates.Start();
    }

    private void OnCycleFinished(object sender, EventArgs e) => Interlocked.Increment(ref _loopFrames);

    private void OnRatesTick(object sender, EventArgs e)
    {
        var now = System.Diagnostics.Stopwatch.GetTimestamp();
        var seconds = System.Diagnostics.Stopwatch.GetElapsedTime(_lastSample, now).TotalSeconds;
        var loop = Interlocked.Read(ref _loopFrames);
        var presented = Interlocked.Read(ref RuntimeStats.PresentedFrames);

        // The frames the diagnostics draw for themselves - this title, the plate - are not the application's: counted, an
        // idle application would read one frame, which changes the title, which draws one more. Nothing else drawn, the
        // application rests: the loop's idle wakes are no rate of its own either.
        var own = DiagnosticsOverlayBehavior.OwnFrames;
        var drawn = Math.Max(0, presented - _lastPresented - (own - _lastOwn));
        var render = (int)Math.Round(drawn / seconds);
        var cycles = drawn > 0 ? (int)Math.Round((loop - _lastLoopFrames) / seconds) : 0;
        _lastSample = now;
        _lastLoopFrames = loop;
        _lastPresented = presented;
        _lastOwn = own;

        var rates = MainStrings.DiagnosticsRates(render, cycles);
        if (rates != Title) DiagnosticsOverlayBehavior.CountOwnFrame();
        Title = rates;
    }

    public void Dispose()
    {
        _rates.Stop();
        _rates.Tick -= OnRatesTick;
        _diagnostics.CycleFinished -= OnCycleFinished;
        _closeWhenCanceled.Dispose();
    }
}
