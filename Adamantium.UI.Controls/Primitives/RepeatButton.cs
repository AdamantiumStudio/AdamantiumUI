using System;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Dispatcher;
using Adamantium.UI.Core.Input;

namespace Adamantium.UI.Controls.Primitives;

/// <summary>
/// A button that raises <see cref="ButtonBase.Click"/> repeatedly while held: once immediately on press, then - after
/// <see cref="Delay"/> ms - every <see cref="Interval"/> ms until release. Used for the ScrollBar line/page buttons
/// (and spinners). Mirrors WPF's RepeatButton.
/// </summary>
public class RepeatButton : ButtonBase
{
    public static readonly AdamantiumProperty DelayProperty = AdamantiumProperty.Register(nameof(Delay),
        typeof(int), typeof(RepeatButton), new PropertyMetadata(0));

    public static readonly AdamantiumProperty IntervalProperty = AdamantiumProperty.Register(nameof(Interval),
        typeof(int), typeof(RepeatButton), new PropertyMetadata(0));

    private DispatcherTimer _timer;

    static RepeatButton()
    {
        // Press-and-hold: the first click fires immediately on press (ClickMode.Press), then the timer drives repeats.
        // A class default via metadata, NOT a constructor set: a set writes Local priority, which outranks and permanently
        // masks a {Binding}/Style/Trigger on ClickMode. ClickMode carries no change callback, so overriding its default
        // loses nothing.
        ClickModeProperty.OverrideMetadata(typeof(RepeatButton), new PropertyMetadata(ClickMode.Press));
    }

    /// <summary>Milliseconds the button must be held before auto-repeat begins; 0, the default, waits as a held key
    /// does in the OS (<see cref="PlatformSettings.KeyRepeatDelay"/>).</summary>
    public int Delay
    {
        get => GetValue<int>(DelayProperty);
        set => SetValue(DelayProperty, value);
    }

    /// <summary>Milliseconds between repeats once auto-repeat has begun; 0, the default, repeats as a held key does in
    /// the OS (<see cref="PlatformSettings.KeyRepeatInterval"/>).</summary>
    public int Interval
    {
        get => GetValue<int>(IntervalProperty);
        set => SetValue(IntervalProperty, value);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (IsPressed)
        {
            StartTimer();
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (!IsPressed)
        {
            StopTimer();
        }
    }

    protected override void OnPointerEntered(PointerEventArgs e)
    {
        base.OnPointerEntered(e);
        if (IsPressed)
        {
            StartTimer();
        }
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        if (!IsPressed)
        {
            StopTimer();
        }
    }

    // Held is what a repeat button is for: it goes on repeating, as it does under a mouse button.
    protected override bool LongPressCancelsClick => false;

    protected override void OnPointerCaptureLost(PointerEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        StopTimer();
    }

    private void StartTimer()
    {
        _timer ??= CreateTimer();
        _timer.Interval = TimeSpan.FromMilliseconds(Math.Max(1, IntervalOrTheUsers(Interval)));
        _timer.Start(TimeSpan.FromMilliseconds(Math.Max(1, DelayOrTheUsers(Delay))));   // first repeat after Delay, then every Interval
    }

    /// <summary><paramref name="delay"/>, or the OS's key repeat delay where it is 0.</summary>
    internal static int DelayOrTheUsers(int delay) => delay > 0 ? delay : (int)PlatformSettings.KeyRepeatDelay;

    /// <summary><paramref name="interval"/>, or the OS's key repeat interval where it is 0. Keys the user stopped from
    /// repeating say nothing of a button made to repeat: it keeps thirty a second.</summary>
    internal static int IntervalOrTheUsers(int interval) => interval > 0
        ? interval
        : PlatformSettings.KeyRepeatInterval is > 0 and var pace ? (int)pace : 33;

    private void StopTimer() => _timer?.Stop();

    private DispatcherTimer CreateTimer()
    {
        var timer = new DispatcherTimer();
        timer.Tick += OnTick;
        return timer;
    }

    private void OnTick(object sender, EventArgs e)
    {
        // Repeat only while genuinely held and usable; otherwise stand down.
        if (IsPressed && IsEnabled) OnClick();
        else StopTimer();
    }
}
