using System;
using System.Collections.ObjectModel;
using System.Globalization;
using Adamantium.Core.Commands;
using Adamantium.UI.Controls.Automation;
using Adamantium.UI.Controls.Base;
using Adamantium.UI.Controls.Primitives;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Automation;
using Adamantium.UI.Core.Dispatcher;
using Adamantium.UI.Core.Localization;
using Adamantium.UI.Core.RoutedEvents;

namespace Adamantium.UI.Controls;

/// <summary>A stopwatch: a <see cref="Clock"/> that counts the time passed once started - with hands or in figures, as
/// <see cref="ClockStyle"/> says - and takes laps (<see cref="Laps"/>, listed under it while <see cref="ShowLaps"/>).
/// Started, paused, lapped and reset by the buttons it shows (<see cref="ShowButtons"/>), its commands or its
/// methods.</summary>
public class StopwatchTimer : Control
{
    public static readonly AdamantiumProperty ElapsedProperty = AdamantiumProperty.RegisterReadOnly(nameof(Elapsed),
        typeof(TimeSpan), typeof(StopwatchTimer), new PropertyMetadata(TimeSpan.Zero));

    public static readonly AdamantiumProperty IsRunningProperty = AdamantiumProperty.RegisterReadOnly(nameof(IsRunning),
        typeof(bool), typeof(StopwatchTimer), new PropertyMetadata(false));

    public static readonly AdamantiumProperty ClockStyleProperty = AdamantiumProperty.Register(nameof(ClockStyle),
        typeof(ClockStyle), typeof(StopwatchTimer), new PropertyMetadata(ClockStyle.Classic));

    public static readonly AdamantiumProperty ShowSecondsProperty = AdamantiumProperty.Register(nameof(ShowSeconds),
        typeof(bool), typeof(StopwatchTimer), new PropertyMetadata(true));

    public static readonly AdamantiumProperty ShowButtonsProperty = AdamantiumProperty.Register(nameof(ShowButtons),
        typeof(bool), typeof(StopwatchTimer), new PropertyMetadata(true));

    public static readonly AdamantiumProperty ShowLapsProperty = AdamantiumProperty.Register(nameof(ShowLaps),
        typeof(bool), typeof(StopwatchTimer), new PropertyMetadata(true));

    public static readonly AdamantiumProperty HasLapsProperty = AdamantiumProperty.RegisterReadOnly(nameof(HasLaps),
        typeof(bool), typeof(StopwatchTimer), new PropertyMetadata(false));

    private readonly ClockAction _start;
    private readonly ClockAction _pause;
    private readonly ClockAction _lap;
    private readonly ClockAction _reset;
    private readonly ObservableCollection<StopwatchLap> _laps = new();
    private DispatcherTimer _tick;
    private Clock _clock;
    private TimeSpan _passed;
    private long? _since;
    private bool _attached;
    private string _handTo;
    private ItemsControl _list;

    public StopwatchTimer()
    {
        Laps = new ReadOnlyObservableCollection<StopwatchLap>(_laps);
        _start = new ClockAction(Start, () => !IsRunning);
        _pause = new ClockAction(Pause, () => IsRunning);
        _lap = new ClockAction(Lap, () => IsRunning);
        _reset = new ClockAction(Reset, () => !IsRunning && (Passed() > TimeSpan.Zero || _laps.Count > 0));
    }

    /// <summary>The time passed, laps and pauses aside - told every second while it runs in the tree, again once shown,
    /// and exactly when it stops.</summary>
    public TimeSpan Elapsed
    {
        get => GetValue<TimeSpan>(ElapsedProperty);
        private set => SetValue(ElapsedProperty, value);
    }

    /// <summary>Whether it is counting now.</summary>
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

    /// <summary>Shows the seconds; on by default.</summary>
    public bool ShowSeconds
    {
        get => GetValue<bool>(ShowSecondsProperty);
        set => SetValue(ShowSecondsProperty, value);
    }

    /// <summary>Shows the start, pause, lap (while <see cref="ShowLaps"/>) and reset buttons; on by default. Off, it is
    /// driven by its commands and methods alone.</summary>
    public bool ShowButtons
    {
        get => GetValue<bool>(ShowButtonsProperty);
        set => SetValue(ShowButtonsProperty, value);
    }

    /// <summary>Lists the laps under it and shows the lap button; on by default. Off, laps are taken by
    /// <see cref="LapCommand"/> alone, for whoever shows <see cref="Laps"/>.</summary>
    public bool ShowLaps
    {
        get => GetValue<bool>(ShowLapsProperty);
        set => SetValue(ShowLapsProperty, value);
    }

    /// <summary>Whether a lap has been taken - the list of them is shown only then.</summary>
    public bool HasLaps
    {
        get => GetValue<bool>(HasLapsProperty);
        private set => SetValue(HasLapsProperty, value);
    }

    /// <summary>The laps taken, the latest first.</summary>
    public ReadOnlyObservableCollection<StopwatchLap> Laps { get; }

    public ICommand StartCommand => _start;

    public ICommand PauseCommand => _pause;

    /// <summary>Takes a lap: the time since the last one, while it runs.</summary>
    public ICommand LapCommand => _lap;

    /// <summary>Puts it back to zero and forgets the laps, once it is stopped.</summary>
    public ICommand ResetCommand => _reset;

    /// <summary>The clock it counts on, once its template is applied.</summary>
    public Clock Clock => _clock;

    public void Start()
    {
        if (IsRunning)
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

        _passed = Passed();
        _since = null;
        Switch(false, "PART_Start");
        Show();
    }

    public void Lap()
    {
        if (!IsRunning)
        {
            return;
        }

        var total = Passed();
        var time = total - (_laps.Count > 0 ? _laps[0].Total : TimeSpan.Zero);
        var lap = new StopwatchLap(_laps.Count + 1, time, total);
        lap.Retext(Text);
        _laps.Insert(0, lap);
        HasLaps = true;
        _reset.RaiseCanExecuteChanged();
    }

    public void Reset()
    {
        _passed = TimeSpan.Zero;
        _since = null;
        _laps.Clear();
        HasLaps = false;
        Switch(false, "PART_Start");
        Show();
    }

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        _clock = GetTemplateChild("PART_Clock") as Clock;
        if (GetTemplateChild("PART_Start") is ButtonBase start) start.Command = _start;
        if (GetTemplateChild("PART_Pause") is ButtonBase pause) pause.Command = _pause;
        if (GetTemplateChild("PART_Lap") is ButtonBase lap) lap.Command = _lap;
        if (GetTemplateChild("PART_Reset") is ButtonBase reset) reset.Command = _reset;
        _list = GetTemplateChild("PART_Laps") as ItemsControl;
        if (_list != null) _list.ItemsSource = Laps;
        Show();
    }

    public override void OnRemoveTemplate()
    {
        base.OnRemoveTemplate();
        if (_list != null) _list.ItemsSource = null;
        _list = null;
        _clock = null;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _attached = true;
        if (_tick == null)
        {
            _tick = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _tick.Tick += OnTick;
        }

        Languages.Changed -= OnLanguagesChanged;
        Languages.Changed += OnLanguagesChanged;
        Retext();
        Show();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _attached = false;
        _tick?.Stop();
        Languages.Changed -= OnLanguagesChanged;
    }

    protected override void OnPropertyChanged(AdamantiumPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.Property == LanguageProperty)
        {
            Retext();
        }
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new StopwatchTimerAutomationPeer(this);

    private void OnLanguagesChanged(object sender, EventArgs e) => Retext();

    private void Retext()
    {
        foreach (var lap in _laps)
        {
            lap.Retext(Text);
        }
    }

    private void Switch(bool running, string to)
    {
        var focused = GetTemplateChild("PART_Start") is IInputComponent { IsFocused: true }
                      || GetTemplateChild("PART_Pause") is IInputComponent { IsFocused: true }
                      || GetTemplateChild("PART_Lap") is IInputComponent { IsFocused: true }
                      || GetTemplateChild("PART_Reset") is IInputComponent { IsFocused: true };
        IsRunning = running;
        _handTo = focused ? to : null;
    }

    private void HandOver()
    {
        if (_handTo != null && GetTemplateChild(_handTo) is IInputComponent next)
        {
            next.Focus();
        }

        _handTo = null;
    }

    private TimeSpan Passed() =>
        _since is { } since ? _passed + System.Diagnostics.Stopwatch.GetElapsedTime(since) : _passed;

    private string Text(TimeSpan length)
    {
        var culture = Languages.CultureOf(Language ?? Languages.Current);
        var separator = culture.DateTimeFormat.TimeSeparator;
        return ((int)length.TotalHours).ToString("00", CultureInfo.InvariantCulture) + separator +
               length.Minutes.ToString("00", CultureInfo.InvariantCulture) + separator +
               length.Seconds.ToString("00", CultureInfo.InvariantCulture) + culture.NumberFormat.NumberDecimalSeparator +
               (length.Milliseconds / 10).ToString("00", CultureInfo.InvariantCulture);
    }

    private void Show()
    {
        var passed = Passed();
        Elapsed = IsRunning ? TimeSpan.FromSeconds(Math.Floor(passed.TotalSeconds)) : passed;
        _clock?.Count(_passed, 1, _since, null);
        _tick?.Stop();
        if (IsRunning && _attached && _tick != null && (LoopSignal.IsClaimed || System.Threading.SynchronizationContext.Current != null))
        {
            _tick.Start(TimeSpan.FromTicks(TimeSpan.TicksPerSecond - passed.Ticks % TimeSpan.TicksPerSecond));
        }

        _start.RaiseCanExecuteChanged();
        _pause.RaiseCanExecuteChanged();
        _lap.RaiseCanExecuteChanged();
        _reset.RaiseCanExecuteChanged();
        HandOver();
    }

    private void OnTick(object sender, EventArgs e)
    {
        var passed = Passed();
        Elapsed = TimeSpan.FromSeconds(Math.Floor(passed.TotalSeconds));
        var within = TimeSpan.TicksPerSecond - passed.Ticks % TimeSpan.TicksPerSecond;
        if (within < TimeSpan.TicksPerSecond * 9 / 10)
        {
            _tick.Start(TimeSpan.FromTicks(within));
        }
    }
}
