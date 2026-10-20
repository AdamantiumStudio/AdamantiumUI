using System;
using System.Linq;
using System.Threading;
using Adamantium.Core.DependencyInjection;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Automation;
using Adamantium.UI.Controls.Base;
using Adamantium.UI.Controls.Buttons;
using Adamantium.UI.Controls.Decorators;
using Adamantium.UI.Controls.Shapes;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Automation;
using Adamantium.UI.Core.Input;
using Adamantium.UI.Core.Resources;
using Adamantium.UI.Extensions;
using NUnit.Framework;

namespace Adamantium.XamlTests;

/// <summary>A countdown and a stopwatch on the clock: what they show standing, running, paused and reset, a countdown
/// reaching zero, the ring on a face as long as what is left, laps, their buttons and automation.</summary>
[TestFixture]
public class TimerTests
{
    private FakeApp _app;
    private Window _window;

    [OneTimeSetUp]
    public void EnsureAppContext()
    {
        _app = new FakeApp(new AdamantiumDependencyContainer()) { ResourceManager = new ResourceManager() };
        UIAppContext.Initialize(_app, null);
    }

    [SetUp]
    public void UseFluent()
    {
        var theme = new Adamantium.UI.Themes.FluentTheme.Fluent();
        _app.ResourceManager = new ResourceManager();
        typeof(UIAppContext).GetProperty(nameof(UIAppContext.Current)).SetValue(null, _app);
        var themes = new ThemeManager(new AdamantiumDependencyContainer());
        _app.ThemeManager = themes;
        ((FakeContext)_app.UIContext).ThemeEngine = themes;
        themes.AddTheme(theme.Name, theme);
        themes.SetTheme(theme);
        LoopSignal.Drain();
    }

    [TearDown]
    public void LetGo()
    {
        if (_window != null) _window.Content = null;
        LoopSignal.Drain();
    }

    private T Shown<T>(T content) where T : MeasurableUIComponent
    {
        content.Width = 260;
        content.Height = 320;
        _window = new Window { Width = 800, Height = 600, ClientWidth = 800, ClientHeight = 600, Content = content };
        _window.ApplyCurrentTheme();
        Settle();
        return content;
    }

    private void Settle()
    {
        for (var i = 0; i < 4; i++)
        {
            WindowExtension.UpdateTree(_window);
            Adamantium.UI.Core.Data.BindingUpdateQueue.Flush();
        }
    }

    private void Run(Func<bool> until, int milliseconds = 3000)
    {
        var deadline = Environment.TickCount64 + milliseconds;
        while (!until() && Environment.TickCount64 < deadline)
        {
            Thread.Sleep(20);
            LoopSignal.Drain();
        }

        Settle();
    }

    private static Button Part(Control control, string name) => (Button)control.GetTemplateChild(name);

    private static System.Collections.Generic.IEnumerable<T> Within<T>(IUIComponent root)
    {
        foreach (var child in root.VisualChildren)
        {
            if (child is T found) yield return found;
            foreach (var deep in Within<T>(child)) yield return deep;
        }
    }

    [Test]
    public void ACountdownStands_AtItsDuration_UntilStarted()
    {
        var timer = Shown(new CountdownTimer { Duration = TimeSpan.FromMinutes(2), ClockStyle = ClockStyle.Text });

        Assert.Multiple(() =>
        {
            Assert.That(timer.Remaining, Is.EqualTo(TimeSpan.FromMinutes(2)));
            Assert.That(timer.IsRunning, Is.False);
            Assert.That(timer.Clock.ShownText, Is.EqualTo("00:02:00").Or.EqualTo("00.02.00"));
            Assert.That(timer.StartCommand.CanExecute(null), Is.True);
            Assert.That(timer.PauseCommand.CanExecute(null), Is.False);
            Assert.That(timer.ResetCommand.CanExecute(null), Is.False, "nothing to reset yet");
            Assert.That(Part(timer, "PART_Start").Command, Is.SameAs(timer.StartCommand));
        });
    }

    [Test]
    public void ACountdown_RunsDown_Pauses_AndResets()
    {
        LoopSignal.Claim();
        try
        {
            var timer = Shown(new CountdownTimer { Duration = TimeSpan.FromSeconds(30) });
            timer.Start();
            Settle();
            Assert.Multiple(() =>
            {
                Assert.That(timer.IsRunning, Is.True);
                Assert.That(Part(timer, "PART_Start").Visibility, Is.EqualTo(Visibility.Collapsed), "start gives way to pause");
                Assert.That(Part(timer, "PART_Pause").Visibility, Is.EqualTo(Visibility.Visible));
            });

            Thread.Sleep(1200);
            LoopSignal.Drain();
            timer.Pause();
            var paused = timer.Remaining;
            Thread.Sleep(300);

            Assert.Multiple(() =>
            {
                Assert.That(paused, Is.LessThan(TimeSpan.FromSeconds(30)).And.GreaterThan(TimeSpan.FromSeconds(25)));
                Assert.That(timer.Remaining, Is.EqualTo(paused), "a paused countdown stands");
                Assert.That(timer.ResetCommand.CanExecute(null), Is.True);
            });

            timer.Reset();
            Assert.That(timer.Remaining, Is.EqualTo(TimeSpan.FromSeconds(30)));
        }
        finally
        {
            LoopSignal.Unclaim();
        }
    }

    [Test]
    public void ACountdown_StopsAtZero_AndSaysSo()
    {
        LoopSignal.Claim();
        try
        {
            var timer = Shown(new CountdownTimer { Duration = TimeSpan.FromMilliseconds(400) });
            var completed = 0;
            timer.Completed += (_, _) => completed++;

            timer.Start();
            Run(() => !timer.IsRunning);

            Assert.Multiple(() =>
            {
                Assert.That(timer.IsRunning, Is.False);
                Assert.That(timer.Remaining, Is.EqualTo(TimeSpan.Zero));
                Assert.That(completed, Is.EqualTo(1));
                Assert.That(timer.StartCommand.CanExecute(null), Is.False, "nothing left to count");
            });
        }
        finally
        {
            LoopSignal.Unclaim();
        }
    }

    [Test]
    public void AnAnalogCountdown_RingsWhatIsLeft_AndTurnsItsHandsBack()
    {
        var timer = Shown(new CountdownTimer { Duration = TimeSpan.FromMinutes(1), ShowDurationField = false });
        var left = Within<Ellipse>(timer.Clock).Single(e => e.Classes.Contains("ClockRemaining"));

        Assert.That(left.StopAngle, Is.EqualTo(360).Within(0.01), "all of it is left");
        Assert.That(left.RingThickness, Is.GreaterThan(0), "a ring round the rim, not a disc under the hands");
        Assert.That(left.RenderSize.Width, Is.EqualTo(timer.Clock.Face.RenderSize.Width).Within(0.5), "at the rim");
        Assert.That(left.StartAngle, Is.EqualTo(270), "from twelve");
        Assert.That(left.RenderTransform?.RotationAngle ?? 0, Is.EqualTo(0), "not turned, so the batch draws it");
        Assert.That(timer.Clock.SecondTurn, Is.Null, "no second hand over the ring");
        Assert.That(timer.Clock.MinuteTurn.RotationAngle, Is.EqualTo(6).Within(0.01), "a minute left");
    }

    [Test]
    public void WithLapsHidden_LapsAreStillTakenByTheCommand()
    {
        LoopSignal.Claim();
        try
        {
            var stopwatch = Shown(new StopwatchTimer { ShowLaps = false });
            stopwatch.Start();
            stopwatch.LapCommand.Execute(null);

            Assert.That(stopwatch.Laps, Has.Count.EqualTo(1));
            stopwatch.Pause();
        }
        finally
        {
            LoopSignal.Unclaim();
        }
    }

    [Test]
    public void AStopwatch_Counts_TakesLaps_AndResetsToNothing()
    {
        LoopSignal.Claim();
        try
        {
            var stopwatch = Shown(new StopwatchTimer { ClockStyle = ClockStyle.SevenSegment });
            Assert.That(stopwatch.LapCommand.CanExecute(null), Is.False, "no lap while it stands");

            stopwatch.Start();
            Thread.Sleep(150);
            stopwatch.Lap();
            Thread.Sleep(150);
            stopwatch.Lap();
            stopwatch.Pause();

            Assert.Multiple(() =>
            {
                Assert.That(stopwatch.Laps, Has.Count.EqualTo(2));
                Assert.That(stopwatch.Laps[0].Number, Is.EqualTo(2), "the latest first");
                Assert.That(stopwatch.Laps[0].Total, Is.EqualTo(stopwatch.Laps[1].Total + stopwatch.Laps[0].Time));
                Assert.That(stopwatch.Elapsed, Is.GreaterThanOrEqualTo(stopwatch.Laps[0].Total));
                Assert.That(stopwatch.Laps[1].TimeText, Does.StartWith("00:00:00"));
                Assert.That(stopwatch.HasLaps);
                Assert.That(((UIComponent)stopwatch.GetTemplateChild("PART_Laps")).Visibility, Is.EqualTo(Visibility.Visible));
                Assert.That(((ItemsControl)stopwatch.GetTemplateChild("PART_Laps")).ItemsSource, Is.SameAs(stopwatch.Laps));
            });

            stopwatch.Reset();
            Settle();
            Assert.Multiple(() =>
            {
                Assert.That(stopwatch.Elapsed, Is.EqualTo(TimeSpan.Zero));
                Assert.That(stopwatch.Laps, Is.Empty);
                Assert.That(((UIComponent)stopwatch.GetTemplateChild("PART_Laps")).Visibility, Is.EqualTo(Visibility.Collapsed),
                    "an empty list is not shown");
                Assert.That(stopwatch.Clock.ShownText, Is.EqualTo("00:00:00"));
            });
        }
        finally
        {
            LoopSignal.Unclaim();
        }
    }

    [Test]
    public void APauseAfterZero_StillCompletes()
    {
        LoopSignal.Claim();
        try
        {
            var timer = Shown(new CountdownTimer { Duration = TimeSpan.FromMilliseconds(100) });
            var completed = 0;
            timer.Completed += (_, _) => completed++;
            timer.Start();
            Thread.Sleep(250);

            timer.Pause();

            Assert.Multiple(() =>
            {
                Assert.That(completed, Is.EqualTo(1));
                Assert.That(timer.IsRunning, Is.False);
                Assert.That(timer.Remaining, Is.EqualTo(TimeSpan.Zero));
            });
        }
        finally
        {
            LoopSignal.Unclaim();
        }
    }

    [Test]
    public void ALengthBelowZero_StandsAtZero_AndAHugeOneDoesNotThrow()
    {
        var timer = Shown(new CountdownTimer { Duration = TimeSpan.FromSeconds(-5) });
        Assert.That(timer.Remaining, Is.EqualTo(TimeSpan.Zero));

        Assert.DoesNotThrow(() => timer.Duration = TimeSpan.MaxValue);
        Assert.That(timer.Remaining, Is.GreaterThan(TimeSpan.FromDays(1000)));
    }

    [Test]
    public void ACountdownWithoutSeconds_RoundsUpToTheMinute()
    {
        var timer = Shown(new CountdownTimer { Duration = TimeSpan.FromSeconds(30), ShowSeconds = false, ClockStyle = ClockStyle.Text });

        Assert.That(timer.Clock.ShownText, Is.EqualTo("00:01"), "half a minute left is not nothing left");
    }

    [Test]
    public void FiguresPastAHundredHours_GetAnotherDigit()
    {
        var timer = Shown(new CountdownTimer { Duration = TimeSpan.FromHours(120), ClockStyle = ClockStyle.SevenSegment });

        Assert.That(timer.Clock.Digits, Has.Count.EqualTo(7), "three for the hours, two for the minutes and the seconds");
    }

    [Test]
    public void TheKeyboard_PassesToTheButtonThatTakesThePressedOnesPlace()
    {
        var timer = Shown(new CountdownTimer { Duration = TimeSpan.FromMinutes(1) });
        Adamantium.UI.Core.Input.FocusManager.Focus(Part(timer, "PART_Start"), Adamantium.UI.Core.Input.NavigationMethod.Tab);

        timer.Start();
        Settle();
        Assert.That(Part(timer, "PART_Pause").IsFocused, Is.True, "start to pause");

        timer.Pause();
        Settle();
        Assert.That(Part(timer, "PART_Start").IsFocused, Is.True, "and back");
        Adamantium.UI.Core.Input.FocusManager.ResetFocus();
    }

    [Test]
    public void AFocusedButtonThatStaysShown_KeepsTheKeyboard()
    {
        var timer = Shown(new CountdownTimer { Duration = TimeSpan.FromMinutes(1) });
        timer.Start();
        timer.Pause();
        Settle();
        Adamantium.UI.Core.Input.FocusManager.Focus(Part(timer, "PART_Reset"), Adamantium.UI.Core.Input.NavigationMethod.Tab);

        timer.StartCommand.Execute(null);
        Settle();

        Assert.That(Part(timer, "PART_Reset").IsFocused, Is.True, "reset is still there to press");
        timer.Pause();
        Adamantium.UI.Core.Input.FocusManager.ResetFocus();
    }

    [Test]
    public void ALongCountdownOffTheTree_KeepsCounting()
    {
        LoopSignal.Claim();
        try
        {
            var timer = Shown(new CountdownTimer { Duration = TimeSpan.FromDays(60) });
            timer.Start();

            Assert.DoesNotThrow(() => _window.Content = null, "sixty days is past what a system timer waits at once");
            Assert.That(timer.IsRunning, Is.True);
            timer.Pause();
        }
        finally
        {
            LoopSignal.Unclaim();
        }
    }

    [Test]
    public void HoursOverAHundred_GiveTheirDigitBackOnceStanding()
    {
        var timer = Shown(new CountdownTimer { Duration = TimeSpan.FromHours(120), ClockStyle = ClockStyle.SevenSegment });
        timer.Duration = TimeSpan.FromMinutes(5);

        Assert.That(timer.Clock.Digits, Has.Count.EqualTo(6));
    }

    [Test]
    public void TheButtonsAndTheLaps_GoWhenAskedTo()
    {
        var stopwatch = Shown(new StopwatchTimer { ShowButtons = false, ShowLaps = false });

        Assert.Multiple(() =>
        {
            Assert.That(((UIComponent)stopwatch.GetTemplateChild("Buttons")).Visibility, Is.EqualTo(Visibility.Collapsed));
            Assert.That(((UIComponent)stopwatch.GetTemplateChild("PART_Laps")).Visibility, Is.EqualTo(Visibility.Collapsed));
        });
    }

    [Test]
    public void StandingAtItsWholeLength_ACountdownShowsAFieldForIt_InPlaceOfItsClock()
    {
        LoopSignal.Claim();
        try
        {
            var timer = Shown(new CountdownTimer { Duration = TimeSpan.FromMinutes(3) });
            var field = (DurationField)timer.GetTemplateChild("PART_Duration");
            var length = (UIComponent)timer.GetTemplateChild("Length");

            Assert.Multiple(() =>
            {
                Assert.That(timer.IsAtStart);
                Assert.That(length.Visibility, Is.EqualTo(Visibility.Visible));
                Assert.That(timer.Clock.Visibility, Is.EqualTo(Visibility.Hidden), "keeping its room");
                Assert.That(field.Value, Is.EqualTo(TimeSpan.FromMinutes(3)));
                Assert.That(field.RenderSize.Width * field.RenderTransform.ScaleX, Is.GreaterThan(150),
                    "figures as large as the clock's room takes");
            });

            FocusManager.Focus(field, NavigationMethod.Tab);
            Settle();
            var chrome = (Border)field.ActiveSegment.GetTemplateChild("Chrome");
            Assert.That(chrome.Background is null or Adamantium.UI.Core.Media.SolidColorBrush { Color.A: 0 },
                "the part being typed is marked by its figures, not filled");
            Assert.That(FieldSegment.GetMarking(field.ActiveSegment), Is.EqualTo(FieldSegmentMarking.Figures));

            field.SetCurrentValue(DurationField.ValueProperty, TimeSpan.FromMinutes(10));
            Settle();
            Assert.That(timer.Duration, Is.EqualTo(TimeSpan.FromMinutes(10)), "the field sets the length");

            Assert.DoesNotThrow(() => field.RaiseEvent(new KeyEventArgs(KeyboardDevice.CurrentDevice, Key.Delete,
                InputModifiers.None, 0) { RoutedEvent = Keyboard.KeyDownEvent }));
            Settle();
            Assert.That(timer.Duration, Is.EqualTo(TimeSpan.FromMinutes(10)), "an emptied part is not an empty length");
            Assert.That(field.Value, Is.EqualTo(TimeSpan.FromMinutes(10)), "the field is given back what is held");

            field.RaiseEvent(new TextInputEventArgs("1") { RoutedEvent = Keyboard.TextInputEvent });
            timer.Start();
            Settle();
            Assert.That(timer.Duration, Is.EqualTo(new TimeSpan(1, 10, 0)), "a part half typed counts on start");
            Assert.That(timer.Remaining, Is.GreaterThan(TimeSpan.FromHours(1)));
            Assert.That(((IInputComponent)timer.GetTemplateChild("PART_Pause")).IsFocused, "the keyboard goes on");

            Assert.That(length.Visibility, Is.EqualTo(Visibility.Collapsed), "running, the clock");
            Assert.That(timer.Clock.Visibility, Is.EqualTo(Visibility.Visible));

            timer.Pause();
            Settle();
            Assert.That(timer.Clock.Visibility, Is.EqualTo(Visibility.Visible), "paused part way, still the clock");

            timer.Reset();
            Settle();
            Assert.That(length.Visibility, Is.EqualTo(Visibility.Visible), "reset, the field again");
        }
        finally
        {
            FocusManager.ResetFocus();
            LoopSignal.Unclaim();
        }
    }

    [Test]
    public void WithoutTheDurationField_TheClockShowsWhileItStands()
    {
        var timer = Shown(new CountdownTimer { ShowDurationField = false });

        Assert.Multiple(() =>
        {
            Assert.That(timer.IsAtStart);
            Assert.That(((UIComponent)timer.GetTemplateChild("Length")).Visibility, Is.EqualTo(Visibility.Collapsed));
            Assert.That(timer.Clock.Visibility, Is.EqualTo(Visibility.Visible));
        });
    }

    [Test]
    public void WithoutTheLapsShown_TheLapButtonGoesToo()
    {
        var stopwatch = Shown(new StopwatchTimer { ShowLaps = false });

        Assert.Multiple(() =>
        {
            Assert.That(Part(stopwatch, "PART_Lap").Visibility, Is.EqualTo(Visibility.Collapsed));
            Assert.That(Part(stopwatch, "PART_Start").Visibility, Is.EqualTo(Visibility.Visible));
            Assert.That(Part(stopwatch, "PART_Reset").Visibility, Is.EqualTo(Visibility.Visible));
        });
    }

    [Test]
    public void Automation_ReadsWhatIsLeft_AndWhatHasPassed()
    {
        var timer = Shown(new CountdownTimer { Duration = TimeSpan.FromSeconds(90) });
        var peer = (CountdownTimerAutomationPeer)timer.GetAutomationPeer();
        var stopwatch = new StopwatchTimer();
        _window.Content = stopwatch;
        Settle();
        var watch = (StopwatchTimerAutomationPeer)stopwatch.GetAutomationPeer();

        Assert.Multiple(() =>
        {
            Assert.That(peer.ControlType, Is.EqualTo(AutomationControlType.Group));
            Assert.That(peer.Value, Is.EqualTo("00:01:30"));
            Assert.That(peer.IsReadOnly);
            Assert.That(watch.Value, Is.EqualTo("00:00:00"));
            Assert.Throws<InvalidOperationException>(() => watch.SetValue("00:01:00"));
        });
    }
}
