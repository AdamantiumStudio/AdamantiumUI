using System;
using Adamantium.Core.Commands;
using Adamantium.UI.Controls.Automation;
using Adamantium.UI.Controls.Base;
using Adamantium.UI.Controls.Primitives;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Automation;
using Adamantium.UI.Core.Dispatcher;
using Adamantium.UI.Core.RoutedEvents;

namespace Adamantium.UI.Controls;

/// <summary>A countdown: a <see cref="Clock"/> that counts <see cref="Duration"/> down to zero once started - with hands
/// going back round a face ringed at its rim as far as is still left of the whole, with no second hand; or in figures,
/// as <see cref="ClockStyle"/> says. Standing at its whole length it shows a field to set the length in instead
/// (<see cref="ShowDurationField"/>). Started, paused and reset by the buttons it shows (<see cref="ShowButtons"/>), its commands or its methods. It keeps
/// counting while it is out of the tree - a countdown on a hidden page still ends on time. At zero it stops and runs
/// <see cref="CompletedCommand"/>.</summary>
public class CountdownTimer : Control
{
    public static readonly AdamantiumProperty DurationProperty = AdamantiumProperty.Register(nameof(Duration),
        typeof(TimeSpan), typeof(CountdownTimer),
        new PropertyMetadata(TimeSpan.FromMinutes(5), PropertyMetadataOptions.BindsTwoWayByDefault, OnDurationChanged));

    public static readonly AdamantiumProperty RemainingProperty = AdamantiumProperty.RegisterReadOnly(nameof(Remaining),
        typeof(TimeSpan), typeof(CountdownTimer), new PropertyMetadata(TimeSpan.FromMinutes(5)));

    public static readonly AdamantiumProperty IsRunningProperty = AdamantiumProperty.RegisterReadOnly(nameof(IsRunning),
        typeof(bool), typeof(CountdownTimer), new PropertyMetadata(false));

    public static readonly AdamantiumProperty ClockStyleProperty = AdamantiumProperty.Register(nameof(ClockStyle),
        typeof(ClockStyle), typeof(CountdownTimer), new PropertyMetadata(ClockStyle.Classic,
            (a, e) => ((CountdownTimer)a).IsAnalog = (ClockStyle)e.NewValue is ClockStyle.Classic or ClockStyle.Minimal
                or ClockStyle.Roman or ClockStyle.Dots));

    public static readonly AdamantiumProperty IsAnalogProperty = AdamantiumProperty.RegisterReadOnly(nameof(IsAnalog),
        typeof(bool), typeof(CountdownTimer), new PropertyMetadata(true));

    public static readonly AdamantiumProperty ShowSecondsProperty = AdamantiumProperty.Register(nameof(ShowSeconds),
        typeof(bool), typeof(CountdownTimer), new PropertyMetadata(true));

    public static readonly AdamantiumProperty ShowButtonsProperty = AdamantiumProperty.Register(nameof(ShowButtons),
        typeof(bool), typeof(CountdownTimer), new PropertyMetadata(true));

    public static readonly AdamantiumProperty ShowDurationFieldProperty = AdamantiumProperty.Register(
        nameof(ShowDurationField), typeof(bool), typeof(CountdownTimer), new PropertyMetadata(true));

    public static readonly AdamantiumProperty IsAtStartProperty = AdamantiumProperty.RegisterReadOnly(nameof(IsAtStart),
        typeof(bool), typeof(CountdownTimer), new PropertyMetadata(true));

    public static readonly AdamantiumProperty CompletedCommandProperty = AdamantiumProperty.Register(
        nameof(CompletedCommand), typeof(ICommand), typeof(CountdownTimer), new PropertyMetadata(null));

    private readonly ClockAction _start;
    private readonly ClockAction _pause;
    private readonly ClockAction _reset;
    private DispatcherTimer _tick;
    private Clock _clock;
    private DurationField _length;
    private TimeSpan _left = TimeSpan.FromMinutes(5);
    private string _handTo;
    private IUIComponent _handFrom;
    private bool _attached;
    private long? _since;

    public CountdownTimer()
    {
        _start = new ClockAction(Start, () => !IsRunning && Left() > TimeSpan.Zero);
        _pause = new ClockAction(Pause, () => IsRunning);
        _reset = new ClockAction(Reset, () => IsRunning || Left() != Allowed(Duration));
    }

    /// <summary>The length counted down; five minutes by default, below zero counted as zero and at most a hundred years.
    /// Two-way by default. A new one while it stands starts it afresh; while it runs, the ring shows its part of it.</summary>
    public TimeSpan Duration
    {
        get => GetValue<TimeSpan>(DurationProperty);
        set => SetValue(DurationProperty, value);
    }

    /// <summary>What is left - <see cref="Duration"/> until it is started, zero once it has run out. Told every second
    /// while it runs in the tree, and again once shown.</summary>
    public TimeSpan Remaining
    {
        get => GetValue<TimeSpan>(RemainingProperty);
        private set => SetValue(RemainingProperty, value);
    }

    /// <summary>Whether it is counting down now.</summary>
    public bool IsRunning
    {
        get => GetValue<bool>(IsRunningProperty);
        private set => SetValue(IsRunningProperty, value);
    }

    /// <summary>With hands or in figures, as <see cref="Clock.ClockStyle"/>.</summary>
    public ClockStyle ClockStyle
    {
        get => GetValue<ClockStyle>(ClockStyleProperty);
        set => SetValue(ClockStyleProperty, value);
    }

    /// <summary>Shows the seconds in figures, and counts down to the second rather than the minute; on by default.
    /// A face with hands has no second hand.</summary>
    public bool ShowSeconds
    {
        get => GetValue<bool>(ShowSecondsProperty);
        set => SetValue(ShowSecondsProperty, value);
    }

    /// <summary>Shows the start, pause and reset buttons; on by default. Off, it is driven by its commands and methods
    /// alone.</summary>
    public bool ShowButtons
    {
        get => GetValue<bool>(ShowButtonsProperty);
        set => SetValue(ShowButtonsProperty, value);
    }

    /// <summary>Whether its <see cref="ClockStyle"/> has hands rather than figures.</summary>
    public bool IsAnalog
    {
        get => GetValue<bool>(IsAnalogProperty);
        private set => SetValue(IsAnalogProperty, value);
    }

    /// <summary>Lets its <see cref="Duration"/> be set while it stands at its whole length (<see cref="IsAtStart"/>):
    /// a face with hands gives way to the length in large figures, figures take what is typed into them, the part being
    /// set lit in the accent; on by default. Off, the clock only shows it.</summary>
    public bool ShowDurationField
    {
        get => GetValue<bool>(ShowDurationFieldProperty);
        set => SetValue(ShowDurationFieldProperty, value);
    }

    /// <summary>Whether it stands at its whole <see cref="Duration"/>: not started yet, or reset.</summary>
    public bool IsAtStart
    {
        get => GetValue<bool>(IsAtStartProperty);
        private set => SetValue(IsAtStartProperty, value);
    }

    /// <summary>What runs when it reaches zero.</summary>
    public ICommand CompletedCommand
    {
        get => GetValue<ICommand>(CompletedCommandProperty);
        set => SetValue(CompletedCommandProperty, value);
    }

    /// <summary>Starts or resumes the countdown; what the start button runs.</summary>
    public ICommand StartCommand => _start;

    /// <summary>Pauses it where it is.</summary>
    public ICommand PauseCommand => _pause;

    /// <summary>Stops it and puts <see cref="Duration"/> back.</summary>
    public ICommand ResetCommand => _reset;

    /// <summary>The clock it counts on, once its template is applied.</summary>
    public Clock Clock => _clock;

    /// <summary>Raised when it reaches zero, before <see cref="CompletedCommand"/> runs.</summary>
    public event EventHandler Completed;

    public void Start()
    {
        _length?.FinishTyping();
        if (IsRunning || Left() <= TimeSpan.Zero)
        {
            return;
        }

        _since = System.Diagnostics.Stopwatch.GetTimestamp();
        Switch(true, "PART_Pause");
        Show();
    }

    public void Pause()
    {
        if (!IsRunning)
        {
            return;
        }

        if (Left() <= TimeSpan.Zero)
        {
            Complete();
            return;
        }

        _left = Left();
        _since = null;
        Switch(false, "PART_Start");
        Show();
    }

    public void Reset()
    {
        _left = Allowed(Duration);
        _since = null;
        Switch(false, "PART_Start");
        Show();
    }

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        _clock = GetTemplateChild("PART_Clock") as Clock;
        _length = GetTemplateChild("PART_Duration") as DurationField;
        if (_length != null) _length.PropertyChanged += OnLengthChanged;
        if (GetTemplateChild("PART_Start") is ButtonBase start) start.Command = _start;
        if (GetTemplateChild("PART_Pause") is ButtonBase pause) pause.Command = _pause;
        if (GetTemplateChild("PART_Reset") is ButtonBase reset) reset.Command = _reset;
        Show();
    }

    public override void OnRemoveTemplate()
    {
        base.OnRemoveTemplate();
        if (_length != null) _length.PropertyChanged -= OnLengthChanged;
        _length = null;
        _clock = null;
    }

    private void OnLengthChanged(object sender, AdamantiumPropertyChangedEventArgs e)
    {
        if (e.Property == DurationField.ShownValueProperty && IsAtStart && _length.ShownValue is { } shown)
        {
            var typed = Allowed(shown);
            _clock?.Count(typed, -1, null, typed);
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _attached = true;
        Show();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _attached = false;
        Show();
    }

    protected override void OnDiscarded()
    {
        base.OnDiscarded();
        _tick?.Stop();
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new CountdownTimerAutomationPeer(this);

    private static readonly TimeSpan Longest = TimeSpan.FromDays(36500);

    private static TimeSpan Allowed(TimeSpan duration) =>
        duration < TimeSpan.Zero ? TimeSpan.Zero : duration > Longest ? Longest : duration;

    private static void OnDurationChanged(AdamantiumComponent a, AdamantiumPropertyChangedEventArgs e)
    {
        var timer = (CountdownTimer)a;
        if (timer.IsRunning)
        {
            timer.Show();
        }
        else
        {
            timer.Reset();
        }
    }

    private static TimeSpan Unseen(TimeSpan left) => left > TimeSpan.FromDays(1) ? TimeSpan.FromDays(1) : left;

    private TimeSpan Left()
    {
        var left = _since is { } since ? _left - System.Diagnostics.Stopwatch.GetElapsedTime(since) : _left;
        return left < TimeSpan.Zero ? TimeSpan.Zero : left;
    }

    private void Switch(bool running, string to)
    {
        _handFrom = null;
        foreach (var name in Buttons)
        {
            if (GetTemplateChild(name) is IInputComponent { IsFocused: true } focused)
            {
                _handFrom = focused as IUIComponent;
            }
        }

        IsRunning = running;
        _handTo = _handFrom != null ? to : null;
    }

    private static readonly string[] Buttons = ["PART_Start", "PART_Pause", "PART_Reset", "PART_Duration"];

    private void HandOver()
    {
        if (_handTo != null && _handFrom is { } from &&
            (from.Visibility != Visibility.Visible || !from.IsEnabled || from is IInputComponent { IsFocused: false }) &&
            GetTemplateChild(_handTo) is IInputComponent next)
        {
            next.Focus();
        }

        _handTo = null;
        _handFrom = null;
    }

    private void Show()
    {
        if (IsRunning && Left() <= TimeSpan.Zero)
        {
            Complete();
            return;
        }

        var left = Left();
        Remaining = TimeSpan.FromSeconds(Math.Ceiling(left.TotalSeconds));
        IsAtStart = !IsRunning && _left == Allowed(Duration);
        _clock?.Count(_left, -1, _since, Allowed(Duration));
        _tick?.Stop();
        if (IsRunning && (LoopSignal.IsClaimed || System.Threading.SynchronizationContext.Current != null))
        {
            if (_tick == null)
            {
                _tick = new DispatcherTimer();
                _tick.Tick += OnTick;
            }

            var within = left.Ticks % TimeSpan.TicksPerSecond;
            _tick.Interval = TimeSpan.FromSeconds(1);
            _tick.Start(!_attached ? Unseen(left) : TimeSpan.FromTicks(within == 0 ? TimeSpan.TicksPerSecond : within));
        }

        _start.RaiseCanExecuteChanged();
        _pause.RaiseCanExecuteChanged();
        _reset.RaiseCanExecuteChanged();
        HandOver();
    }

    private void OnTick(object sender, EventArgs e)
    {
        var left = Left();
        if (left > TimeSpan.Zero && !_attached)
        {
            _tick.Start(Unseen(left));
            return;
        }

        if (left > TimeSpan.Zero)
        {
            Remaining = TimeSpan.FromSeconds(Math.Ceiling(left.TotalSeconds));
            var within = left.Ticks % TimeSpan.TicksPerSecond;
            if (within > 0 && within < TimeSpan.TicksPerSecond * 9 / 10)
            {
                _tick.Start(TimeSpan.FromTicks(within));
            }

            return;
        }

        Complete();
    }

    private void Complete()
    {
        _tick?.Stop();
        _left = TimeSpan.Zero;
        _since = null;
        Switch(false, "PART_Reset");
        Show();
        Completed?.Invoke(this, EventArgs.Empty);
        if (CompletedCommand is { } command && command.CanExecute(null))
        {
            command.Execute(null);
        }
    }
}
