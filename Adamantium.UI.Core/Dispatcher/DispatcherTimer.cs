using System;
using System.Threading;

namespace Adamantium.UI.Core.Dispatcher;

/// <summary>A timer whose <see cref="Tick"/> is raised on the application's loop once one has claimed it
/// (<see cref="LoopSignal.Claim"/>) - one tick waiting at a time, none from before a <see cref="Stop"/> or
/// <see cref="Start()"/>; otherwise through the <see cref="SynchronizationContext"/> captured at construction, or with none
/// (headless) on the timer thread.</summary>
public sealed class DispatcherTimer : IDisposable
{
    private readonly SynchronizationContext _syncContext;
    private readonly Timer _timer;
    private readonly SendOrPostCallback _post;
    private TimeSpan _interval = TimeSpan.Zero;
    private bool _isEnabled;
    private int _generation;
    private int _queued;
    private long _dueAt;

    public DispatcherTimer()
    {
        _syncContext = SynchronizationContext.Current;
        _post = _ => RaiseTick();
        _timer = new Timer(OnTimerTick, null, Timeout.Infinite, Timeout.Infinite);
    }

    public event EventHandler Tick;

    /// <summary>Arbitrary state, like WPF's DispatcherTimer.Tag (e.g. the control that owns the timer).</summary>
    public object Tag { get; set; }

    public TimeSpan Interval
    {
        get => _interval;
        set
        {
            _interval = value < TimeSpan.Zero ? TimeSpan.Zero : value;
            if (_isEnabled) Schedule(_interval);   // running: apply the new cadence at once
        }
    }

    public bool IsEnabled
    {
        get => _isEnabled;
        set { if (value) Start(); else Stop(); }
    }

    /// <summary>Starts ticking after <see cref="Interval"/>, then every <see cref="Interval"/>.</summary>
    public void Start() => Start(_interval);

    /// <summary>Starts with a custom first delay, then ticks every <see cref="Interval"/> (the press-then-repeat pattern).</summary>
    public void Start(TimeSpan firstDelay)
    {
        Renew();
        _isEnabled = true;
        Schedule(firstDelay);
    }

    public void Stop()
    {
        Renew();
        _isEnabled = false;
        _timer.Change(Timeout.Infinite, Timeout.Infinite);
    }

    private void Renew()
    {
        Interlocked.Increment(ref _generation);
        Volatile.Write(ref _queued, 0);
    }

    private void Schedule(TimeSpan firstDelay)
    {
        var due = firstDelay <= TimeSpan.Zero ? TimeSpan.FromMilliseconds(1) : firstDelay;
        var period = _interval <= TimeSpan.Zero ? TimeSpan.FromMilliseconds(1) : _interval;
        // A millisecond's leeway: the system timer may round its due time down.
        var leeway = due > TimeSpan.FromMilliseconds(1) ? due - TimeSpan.FromMilliseconds(1) : TimeSpan.Zero;
        Volatile.Write(ref _dueAt, System.Diagnostics.Stopwatch.GetTimestamp()
                                   + (long)(leeway.TotalSeconds * System.Diagnostics.Stopwatch.Frequency));
        _timer.Change(due, period);
    }

    // A callback of the old schedule can still be under way, or only starting, when Stop and Start renew the timer: the
    // generation is read before anything else, and a tick before the new start's own delay is that callback's.
    private void OnTimerTick(object state)
    {
        var generation = Volatile.Read(ref _generation);
        if (!_isEnabled || System.Diagnostics.Stopwatch.GetTimestamp() < Volatile.Read(ref _dueAt)) return;
        if (LoopSignal.IsClaimed)
        {
            if (Interlocked.Exchange(ref _queued, 1) == 0)
            {
                if (generation != Volatile.Read(ref _generation))
                {
                    Volatile.Write(ref _queued, 0);
                    return;
                }

                LoopSignal.Post(() => RaiseQueued(generation));
            }
        }
        else if (_syncContext != null && _syncContext != SynchronizationContext.Current)
            _syncContext.Post(_post, null);
        else
            RaiseTick();
    }

    private void RaiseQueued(int generation)
    {
        if (generation != Volatile.Read(ref _generation)) return;
        Volatile.Write(ref _queued, 0);
        RaiseTick();
    }

    private void RaiseTick()
    {
        if (_isEnabled) Tick?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose() => _timer.Dispose();
}
