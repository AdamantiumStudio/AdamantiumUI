using System;
using System.Collections.ObjectModel;
using System.Linq;
using Adamantium.Core.DependencyInjection;
using Adamantium.Mathematics;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Buttons;
using Adamantium.UI.Controls.Panels;
using Adamantium.UI.Controls.Primitives;
using Adamantium.UI.Controls.Text;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Automation;
using Adamantium.UI.Core.Input;
using Adamantium.UI.Core.Localization;
using Adamantium.UI.Core.Media.Animation;
using Adamantium.UI.Core.Resources;
using Adamantium.UI.Core.RoutedEvents;
using Adamantium.UI.Extensions;
using NUnit.Framework;

namespace Adamantium.XamlTests;

/// <summary>A calendar as WPF's, in the theme's own template: the days of a month - or of several side by side - laid out
/// from the language's first day of the week and counted by its calendar, selected one, a run or several runs at a time,
/// struck-through and out-of-range days refused, marked days dressed, a level up to the months and years, pages sliding
/// and zooming between them, the keyboard, automation - and cells that share whatever room the calendar is given.</summary>
[TestFixture]
public class CalendarTests
{
    private static readonly DateTime October = new(2026, 10, 18);

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
    }

    [TearDown]
    public void LetGo()
    {
        FocusManager.ResetFocus();
        AnimationManager.Reset();
    }

    private Calendar Shown(Calendar calendar, double width = double.NaN, double height = double.NaN)
    {
        calendar.Width = width;
        calendar.Height = height;
        calendar.HorizontalAlignment = HorizontalAlignment.Left;
        calendar.VerticalAlignment = VerticalAlignment.Top;
        _window = new Window { Width = 1400, Height = 900, Content = calendar };
        _window.ApplyCurrentTheme();
        Settle();
        return calendar;
    }

    private void Settle()
    {
        for (var i = 0; i < 5; i++)
        {
            WindowExtension.UpdateTree(_window);
            Adamantium.UI.Core.Data.BindingUpdateQueue.Flush();
        }
    }

    private static void Press(Calendar calendar, Key key, InputModifiers modifiers = InputModifiers.None) =>
        calendar.RaiseEvent(new KeyEventArgs(KeyboardDevice.CurrentDevice, key, modifiers, 0)
        {
            RoutedEvent = Keyboard.KeyDownEvent,
        });

    private static string[] Texts(CalendarMonth month, string part) =>
        [.. ((UniformGrid)month.GetTemplateChild(part)).Children.OfType<TextBlock>().Select(text => text.Text)];

    private static CalendarPage[] Pages(Calendar calendar) =>
        [.. ((Panel)calendar.GetTemplateChild("PART_Pages")).Children.OfType<CalendarPage>()];

    private void Drag(Calendar calendar, InputModifiers modifiers, params DateTime[] path)
    {
        var days = path.Select(date => calendar.DayButtons.First(day => day.Date == date && day.Content != null)).ToList();
        var held = modifiers | InputModifiers.LeftMouseButton;
        PointTo(days[0]);
        var press = new PointerPressedEventArgs(Mouse.Pointer, MouseButtons.Left, held, 0)
        {
            RoutedEvent = PointerEvents.PreviewPointerPressedEvent,
            Properties = PointerPointProperties.FromMouse(held),
        };
        ((IObservableComponent)days[0]).RaiseEvent(press);
        press.RoutedEvent = PointerEvents.PointerPressedEvent;
        ((IObservableComponent)days[0]).RaiseEvent(press);

        foreach (var day in days.Skip(1))
        {
            PointTo(day);
            ((IObservableComponent)day).RaiseEvent(new PointerEventArgs(Mouse.Pointer, held, 0)
            {
                RoutedEvent = PointerEvents.PointerMovedEvent,
                Properties = PointerPointProperties.FromMouse(held),
            });
        }

        var release = new PointerReleasedEventArgs(Mouse.Pointer, MouseButtons.Left, modifiers, 0)
        {
            RoutedEvent = PointerEvents.PointerReleasedEvent,
            Properties = PointerPointProperties.FromMouse(modifiers),
        };
        ((IObservableComponent)days[^1]).RaiseEvent(release);
        Mouse.Pointer.Capture(null);
    }

    private void PointTo(CalendarDayButton day)
    {
        var point = day.TranslatePoint(new Vector2(day.RenderSize.Width / 2, day.RenderSize.Height / 2), _window);
        PointerDevice.Current.SetExternalMousePosition(_window, new PixelPoint(point.X, point.Y));
    }

    [TestCase("ru-RU", 28, "пн")]
    [TestCase("en-US", 27, "Sun")]
    public void TheDaysOfAMonth_StartOnTheLanguagesFirstDayOfTheWeek(string language, int firstShown, string firstName)
    {
        var calendar = Shown(new Calendar { Language = language, DisplayDate = October });

        Assert.Multiple(() =>
        {
            Assert.That(calendar.DayButtons, Has.Count.EqualTo(42));
            Assert.That(calendar.DayButtons[0].Date, Is.EqualTo(new DateTime(2026, 9, firstShown)));
            Assert.That(calendar.DayButtons[0].IsInactive, Is.True, "September's");
            Assert.That(calendar.DayButtons.Single(day => day.Date == new DateTime(2026, 10, 1)).IsInactive, Is.False);
            Assert.That(calendar.HeaderText, Does.Contain("2026"));
            Assert.That(Texts(calendar.Months[0], "PART_DayNames")[0], Is.EqualTo(firstName));
        });
    }

    [Test]
    public void FirstDayOfWeek_OverridesTheLanguages()
    {
        var calendar = Shown(new Calendar { Language = "ru-RU", DisplayDate = October, FirstDayOfWeek = DayOfWeek.Wednesday });

        Assert.That(calendar.DayButtons[0].Date.DayOfWeek, Is.EqualTo(DayOfWeek.Wednesday));
        Assert.That(calendar.DayButtons[0].Date, Is.EqualTo(new DateTime(2026, 9, 30)));
    }

    [Test]
    public void WeekNumbers_AreTheLanguages()
    {
        var calendar = Shown(new Calendar { Language = "ru-RU", DisplayDate = October, ShowWeekNumbers = true });

        string[] first = ["40", "41"];
        Assert.That(Texts(calendar.Months[0], "PART_WeekNumbers").Take(2), Is.EqualTo(first));
    }

    [TestCase("ru-RU")]
    [TestCase("en-US")]
    [TestCase("de-DE")]
    public void TheWeekAYearStartsIn_IsItsFirst(string language)
    {
        var calendar = Shown(new Calendar { Language = language, DisplayDate = new DateTime(2026, 1, 15), ShowWeekNumbers = true });

        string[] first = ["1", "2"];
        Assert.That(Texts(calendar.Months[0], "PART_WeekNumbers").Take(2), Is.EqualTo(first));
    }

    [Test]
    public void SelectedDate_AndSelectedDates_FollowEachOther()
    {
        var calendar = Shown(new Calendar { DisplayDate = October });
        var changes = 0;
        calendar.SelectedDatesChanged += (_, _) => changes++;

        calendar.SelectedDate = new DateTime(2026, 10, 5, 13, 30, 0);

        DateTime[] day = [new(2026, 10, 5)];
        Assert.Multiple(() =>
        {
            Assert.That(calendar.SelectedDates, Is.EqualTo(day), "the day, without its time");
            Assert.That(calendar.DayButtons.Single(d => d.IsSelected).Date, Is.EqualTo(new DateTime(2026, 10, 5)));
            Assert.That(changes, Is.EqualTo(1));
        });

        calendar.SelectedDates.Clear();

        Assert.That(calendar.SelectedDate, Is.Null);
    }

    [Test]
    public void ASelectedDateInAnotherMonth_IsShown()
    {
        var calendar = Shown(new Calendar { DisplayDate = October });

        calendar.SelectedDate = new DateTime(2027, 2, 14);

        Assert.That((calendar.DisplayDate.Year, calendar.DisplayDate.Month), Is.EqualTo((2027, 2)));
    }

    [Test]
    public void ABlackedOutDay_CannotBeSelected_AndASelectedOneStruckOutIsLetGo()
    {
        var calendar = Shown(new Calendar { DisplayDate = October, SelectionMode = CalendarSelectionMode.MultipleRange });
        calendar.SelectedDates.AddRange(new DateTime(2026, 10, 10), new DateTime(2026, 10, 12));

        calendar.BlackoutDates.Add(new CalendarDateRange(new DateTime(2026, 10, 11)));

        DateTime[] kept = [new(2026, 10, 10), new(2026, 10, 12)];
        Assert.Multiple(() =>
        {
            Assert.That(calendar.SelectedDates, Is.EqualTo(kept));
            Assert.That(() => calendar.SelectedDates.Add(new DateTime(2026, 10, 11)), Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(calendar.DayButtons.Single(day => day.Date == new DateTime(2026, 10, 11)).IsBlackedOut, Is.True);
        });

        calendar.SelectedDate = new DateTime(2026, 10, 11);

        Assert.That(calendar.SelectedDate, Is.EqualTo(new DateTime(2026, 10, 10)), "a struck-through day is not taken");
    }

    [Test]
    public void ARefusedSelectedDate_WithATime_GoesBack()
    {
        var calendar = Shown(new Calendar { DisplayDate = October, SelectionMode = CalendarSelectionMode.None });

        calendar.SelectedDate = new DateTime(2026, 10, 5, 14, 0, 0);

        Assert.That(calendar.SelectedDate, Is.Null);
    }

    [Test]
    public void TakingADayOut_KeepsTheMonthShown()
    {
        var calendar = Shown(new Calendar
        {
            Language = "en-US", DisplayDate = new DateTime(2026, 1, 15), SelectionMode = CalendarSelectionMode.MultipleRange,
        });
        calendar.SelectedDates.Add(new DateTime(2026, 1, 10));
        calendar.SelectedDates.Add(new DateTime(2026, 2, 3));

        Drag(calendar, InputModifiers.LeftControl, new DateTime(2026, 1, 10));

        DateTime[] left = [new(2026, 2, 3)];
        Assert.That(calendar.SelectedDates, Is.EqualTo(left));
        Assert.That(calendar.DisplayDate.Month, Is.EqualTo(1));
    }

    [Test]
    public void SingleRange_CannotBeSplit()
    {
        var calendar = Shown(new Calendar { DisplayDate = October, SelectionMode = CalendarSelectionMode.SingleRange });
        calendar.SelectedDates.AddRange(new DateTime(2026, 10, 1), new DateTime(2026, 10, 5));

        Assert.Multiple(() =>
        {
            Assert.That(() => calendar.SelectedDates.Remove(new DateTime(2026, 10, 3)), Throws.InvalidOperationException);
            Assert.That(() => calendar.SelectedDates[1] = new DateTime(2026, 10, 20), Throws.InvalidOperationException);
        });

        calendar.SelectedDates.Remove(new DateTime(2026, 10, 5));

        Assert.That(calendar.SelectedDates, Has.Count.EqualTo(4));
    }

    [Test]
    public void ClearingNothing_ChangesNothing()
    {
        var calendar = Shown(new Calendar { DisplayDate = October });
        var changes = 0;
        calendar.SelectedDatesChanged += (_, _) => changes++;

        calendar.SelectedDates.Clear();

        Assert.That(changes, Is.Zero);
    }

    [Test]
    public void ADragStep_IsOneChangeOfTheDays()
    {
        var calendar = Shown(new Calendar { DisplayDate = October, SelectionMode = CalendarSelectionMode.SingleRange });
        calendar.SelectedDates.AddRange(new DateTime(2026, 10, 1), new DateTime(2026, 10, 3));
        var changes = 0;
        calendar.SelectedDates.CollectionChanged += (_, _) => changes++;

        Drag(calendar, InputModifiers.LeftShift, new DateTime(2026, 10, 4));

        Assert.That(changes, Is.EqualTo(1));
    }

    [Test]
    public void EnterWithNothingToSelect_IsLeftToOthers()
    {
        var calendar = Shown(new Calendar { DisplayDate = October, SelectionMode = CalendarSelectionMode.None });
        FocusManager.Focus(calendar, NavigationMethod.Tab);
        var enter = new KeyEventArgs(KeyboardDevice.CurrentDevice, Key.Enter, InputModifiers.None, 0)
        {
            RoutedEvent = Keyboard.KeyDownEvent,
        };

        calendar.RaiseEvent(enter);

        Assert.That(enter.Handled, Is.False);
    }

    [Test]
    public void CtrlDown_AfterAClick_OpensAtOnce()
    {
        var calendar = Shown(new Calendar { DisplayDate = October, DisplayMode = CalendarMode.Year });
        FocusManager.Focus(calendar, NavigationMethod.Mouse);

        Press(calendar, Key.DownArrow, InputModifiers.LeftControl);

        Assert.That(calendar.DisplayMode, Is.EqualTo(CalendarMode.Month));
    }

    [Test]
    public void AShiftClick_SelectsFromTheAnchor_First()
    {
        var calendar = Shown(new Calendar { DisplayDate = October, SelectionMode = CalendarSelectionMode.MultipleRange });
        Drag(calendar, InputModifiers.LeftControl, new DateTime(2026, 10, 12));
        Drag(calendar, InputModifiers.LeftControl, new DateTime(2026, 10, 10));

        Drag(calendar, InputModifiers.LeftShift, new DateTime(2026, 10, 14));

        DateTime[] run = [new(2026, 10, 10), new(2026, 10, 11), new(2026, 10, 12), new(2026, 10, 13), new(2026, 10, 14)];
        Assert.That(calendar.SelectedDates, Is.EqualTo(run));
        Assert.That(calendar.SelectedDate, Is.EqualTo(new DateTime(2026, 10, 10)));
    }

    [TestCase(CalendarSelectionMode.SingleRange)]
    [TestCase(CalendarSelectionMode.MultipleRange)]
    public void ASelectionMadeInCode_MovesTheAnchor(CalendarSelectionMode mode)
    {
        var calendar = Shown(new Calendar { DisplayDate = October, SelectionMode = mode });
        Drag(calendar, InputModifiers.None, new DateTime(2026, 10, 5));
        calendar.SelectedDates.Clear();
        calendar.SelectedDates.Add(new DateTime(2026, 10, 20));

        Drag(calendar, InputModifiers.LeftShift, new DateTime(2026, 10, 22));

        DateTime[] run = [new(2026, 10, 20), new(2026, 10, 21), new(2026, 10, 22)];
        Assert.That(calendar.SelectedDates, Is.EquivalentTo(run));
    }

    [Test]
    public void AStruckDayInsideTheOneRun_KeepsOneRun()
    {
        var calendar = Shown(new Calendar { DisplayDate = October, SelectionMode = CalendarSelectionMode.SingleRange });
        calendar.SelectedDates.AddRange(new DateTime(2026, 10, 10), new DateTime(2026, 10, 15));

        calendar.BlackoutDates.Add(new CalendarDateRange(new DateTime(2026, 10, 12)));

        DateTime[] run = [new(2026, 10, 10), new(2026, 10, 11)];
        Assert.That(calendar.SelectedDates, Is.EqualTo(run));
    }

    [Test]
    public void SingleDate_TakesOneDay()
    {
        var calendar = Shown(new Calendar { DisplayDate = October });
        calendar.SelectedDates.Add(new DateTime(2026, 10, 3));

        Assert.That(() => calendar.SelectedDates.Add(new DateTime(2026, 10, 4)), Throws.InvalidOperationException);
    }

    [Test]
    public void TheRange_ClampsTheDisplayDate_AndDisablesTheDaysOutsideIt()
    {
        var calendar = Shown(new Calendar
        {
            DisplayDate = October, DisplayDateStart = new DateTime(2026, 10, 5), DisplayDateEnd = new DateTime(2026, 11, 20),
        });

        calendar.DisplayDate = new DateTime(2027, 1, 1);

        Assert.Multiple(() =>
        {
            Assert.That(calendar.DisplayDate, Is.EqualTo(new DateTime(2026, 11, 20)));
            Assert.That(calendar.DayButtons.Single(day => day.Date == new DateTime(2026, 11, 21)).IsEnabled, Is.False);
            Assert.That(calendar.DayButtons.Single(day => day.Date == new DateTime(2026, 11, 20)).IsEnabled, Is.True);
        });
    }

    [Test]
    public void TheKeyboard_MovesAndSelects_AndGoesUpALevel()
    {
        var calendar = Shown(new Calendar { DisplayDate = October });
        calendar.SelectedDate = new DateTime(2026, 10, 30);
        FocusManager.Focus(calendar, NavigationMethod.Tab);

        Press(calendar, Key.RightArrow);
        Press(calendar, Key.RightArrow);

        Assert.That(calendar.SelectedDate, Is.EqualTo(new DateTime(2026, 11, 1)));
        Assert.That(calendar.DisplayDate.Month, Is.EqualTo(11), "into the next month");

        Press(calendar, Key.PageUp);
        Assert.That(calendar.SelectedDate, Is.EqualTo(new DateTime(2026, 10, 1)));

        Press(calendar, Key.UpArrow, InputModifiers.LeftControl);
        Assert.That(calendar.DisplayMode, Is.EqualTo(CalendarMode.Year));
    }

    [Test]
    public void ShiftAndTheArrows_SelectARun()
    {
        var calendar = Shown(new Calendar { DisplayDate = October, SelectionMode = CalendarSelectionMode.SingleRange });
        calendar.SelectedDate = new DateTime(2026, 10, 10);
        FocusManager.Focus(calendar, NavigationMethod.Tab);

        Press(calendar, Key.DownArrow, InputModifiers.LeftShift);

        Assert.That(calendar.SelectedDates, Has.Count.EqualTo(8));
        Assert.That((calendar.SelectedDates.Min(), calendar.SelectedDates.Max()),
            Is.EqualTo((new DateTime(2026, 10, 10), new DateTime(2026, 10, 17))));
    }

    [Test]
    public void ADrag_SelectsTheRunItCrosses()
    {
        var calendar = Shown(new Calendar { DisplayDate = October, SelectionMode = CalendarSelectionMode.SingleRange });

        Drag(calendar, InputModifiers.None, new DateTime(2026, 10, 19), new DateTime(2026, 10, 22));

        DateTime[] run = [new(2026, 10, 19), new(2026, 10, 20), new(2026, 10, 21), new(2026, 10, 22)];
        Assert.That(calendar.SelectedDates, Is.EquivalentTo(run));
    }

    [Test]
    public void AFocusTheMouseGave_HidesTheCursor_UntilTheFirstArrowShowsIt()
    {
        var calendar = Shown(new Calendar { DisplayDate = October });
        calendar.SelectedDate = new DateTime(2026, 10, 20);
        FocusManager.Focus(calendar, NavigationMethod.Mouse);
        var day = calendar.DayButtons.Single(d => d.Date == new DateTime(2026, 10, 20));
        Assert.That(calendar.DayButtons.Any(d => d.IsCursor), Is.False, "a click already says where you are");

        Press(calendar, Key.RightArrow);

        Assert.Multiple(() =>
        {
            Assert.That(day.IsCursor, Is.True);
            Assert.That(calendar.SelectedDate, Is.EqualTo(new DateTime(2026, 10, 20)), "the first arrow only shows it");
        });

        Press(calendar, Key.RightArrow);

        Assert.That(calendar.SelectedDate, Is.EqualTo(new DateTime(2026, 10, 21)));
    }

    [Test]
    public void ALevelUp_ShowsTheMonths_ThenTheYears_AndAMonthOpensItsDays()
    {
        var calendar = Shown(new Calendar { Language = "en-US", DisplayDate = October, DisplayMode = CalendarMode.Year });

        string[] months = ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];
        Assert.Multiple(() =>
        {
            Assert.That(calendar.HeaderText, Is.EqualTo("2026"));
            Assert.That(calendar.CalendarButtons.Select(cell => (string)cell.Content), Is.EqualTo(months));
        });

        calendar.DisplayMode = CalendarMode.Decade;

        Assert.Multiple(() =>
        {
            Assert.That(calendar.HeaderText, Is.EqualTo("2020 – 2029"));
            Assert.That(calendar.CalendarButtons[0].Content, Is.EqualTo("2019"));
            Assert.That(calendar.CalendarButtons[0].IsInactive, Is.True);
        });

        calendar.CalendarButtons[3].PerformClick();
        Assert.That((calendar.DisplayMode, calendar.DisplayDate.Year), Is.EqualTo((CalendarMode.Year, 2022)));

        calendar.CalendarButtons[4].PerformClick();
        Assert.That((calendar.DisplayMode, calendar.DisplayDate.Year, calendar.DisplayDate.Month),
            Is.EqualTo((CalendarMode.Month, 2022, 5)));
    }

    [Test]
    public void TheCells_ShareTheRoomTheCalendarIsGiven()
    {
        var calendar = Shown(new Calendar { DisplayDate = October }, 700, 600);
        var day = calendar.DayButtons[0];

        Assert.Multiple(() =>
        {
            Assert.That(calendar.RenderSize, Is.EqualTo(new Size(700, 600)));
            Assert.That(day.RenderSize.Width, Is.GreaterThan(90), "a seventh of the width");
            Assert.That(day.RenderSize.Height, Is.GreaterThan(80), "a sixth of what the header leaves");
        });
    }

    [Test]
    public void ASmallCalendar_SharesItsRoomToo()
    {
        var calendar = Shown(new Calendar { DisplayDate = October }, 140, 140);

        Assert.Multiple(() =>
        {
            Assert.That(calendar.RenderSize, Is.EqualTo(new Size(140, 140)));
            Assert.That(calendar.DayButtons.Max(d => d.RenderSize.Width), Is.LessThanOrEqualTo(140 / 7.0));
        });
    }

    [TestCase(CalendarMode.Year)]
    [TestCase(CalendarMode.Decade)]
    public void ALevelUp_KeepsTheCalendarsSize(CalendarMode level)
    {
        var calendar = Shown(new Calendar { Language = "ru-RU", DisplayDate = October, FontSize = 24, ShowWeekNumbers = true });
        var size = calendar.RenderSize;

        calendar.DisplayMode = level;
        Settle();

        Assert.That(calendar.RenderSize, Is.EqualTo(size));
    }

    [Test]
    public void Automation_SeesACalendar_AGridOfDays_AndSelectsThem()
    {
        var calendar = Shown(new Calendar { Language = "en-US", DisplayDate = October });
        var peer = calendar.GetAutomationPeer();
        var grid = (IGridProvider)peer.GetPattern(PatternId.Grid);
        var day = grid.GetItem(3, 2);

        Assert.Multiple(() =>
        {
            Assert.That(peer.ControlType, Is.EqualTo(AutomationControlType.Calendar));
            Assert.That((grid.RowCount, grid.ColumnCount), Is.EqualTo((6, 7)));
            Assert.That(day.Name, Is.EqualTo("Tuesday, October 20, 2026"));
        });

        ((ISelectionItemProvider)day.GetPattern(PatternId.SelectionItem)).Select();

        Assert.That(calendar.SelectedDate, Is.EqualTo(new DateTime(2026, 10, 20)));
        Assert.That(((ISelectionProvider)peer.GetPattern(PatternId.Selection)).GetSelection().Single(), Is.SameAs(day));
    }

    [Test]
    public void Automation_SelectsAsTheSelectionModeAllows()
    {
        var calendar = Shown(new Calendar { DisplayDate = October, SelectionMode = CalendarSelectionMode.SingleRange });
        ISelectionItemProvider Day(int day) => (ISelectionItemProvider)calendar.DayButtons
            .Single(d => d.Date == new DateTime(2026, 10, day)).GetAutomationPeer().GetPattern(PatternId.SelectionItem);

        Day(10).AddToSelection();
        Day(11).AddToSelection();

        Assert.That(calendar.SelectedDates, Has.Count.EqualTo(2));
        Assert.That(() => Day(20).AddToSelection(), Throws.InvalidOperationException, "not next to the run");

        calendar.SelectionMode = CalendarSelectionMode.None;

        Assert.That(() => Day(20).Select(), Throws.InvalidOperationException);
        Assert.That(calendar.SelectedDates, Is.Empty);
    }

    [Test]
    public void AStartAfterTheEnd_CountsTheEndAsTheStart()
    {
        var calendar = Shown(new Calendar { DisplayDate = October, DisplayDateEnd = new DateTime(2026, 10, 31) });

        calendar.DisplayDateStart = new DateTime(2026, 11, 15);

        Assert.That(calendar.DisplayDate, Is.EqualTo(new DateTime(2026, 11, 15)));
    }

    [TestCase(9999, 12, 31, CalendarMode.Month)]
    [TestCase(9999, 12, 1, CalendarMode.Year)]
    [TestCase(1, 1, 1, CalendarMode.Month)]
    [TestCase(5, 1, 1, CalendarMode.Decade)]
    public void TheFirstAndLastDaysThereAre_AreShown_WithBlanksBeyond(int year, int month, int day, CalendarMode mode)
    {
        var calendar = Shown(new Calendar { Language = "en-US", DisplayDate = new DateTime(year, month, day), DisplayMode = mode });

        var shown = mode == CalendarMode.Month
            ? calendar.DayButtons.Where(d => d.Content != null).Select(d => d.Date.ToString("O")).ToList()
            : calendar.CalendarButtons.Where(c => c.Content != null).Select(c => (string)c.Content).ToList();
        Assert.That(shown, Is.Unique);
        if (year == 1 && mode == CalendarMode.Month)
        {
            Assert.That(calendar.DayButtons.First(d => d.Content != null).Date.DayOfWeek, Is.EqualTo(DayOfWeek.Monday));
            Assert.That(calendar.DayButtons.TakeWhile(d => d.Content == null).Count(), Is.EqualTo(1), "Sunday is blank");
        }
    }

    [TestCase(1, 1, 1, Key.LeftArrow, CalendarMode.Month)]
    [TestCase(9999, 12, 31, Key.DownArrow, CalendarMode.Month)]
    [TestCase(9998, 1, 1, Key.DownArrow, CalendarMode.Decade)]
    [TestCase(9999, 6, 1, Key.PageDown, CalendarMode.Year)]
    public void TheKeyboard_StopsAtTheFirstAndLastDaysThereAre(int year, int month, int day, Key key, CalendarMode mode)
    {
        var date = new DateTime(year, month, day);
        var calendar = Shown(new Calendar { DisplayDate = date, DisplayMode = mode });
        calendar.SelectedDate = mode == CalendarMode.Month ? date : null;
        calendar.DisplayMode = mode;
        FocusManager.Focus(calendar, NavigationMethod.Tab);

        Assert.That(() => Press(calendar, key), Throws.Nothing);
    }

    [Test]
    public void ALanguageOfAnotherCalendar_CountsMonthsByIt()
    {
        Assume.That(System.Globalization.CultureInfo.GetCultureInfo("ar-SA").Calendar,
            Is.Not.InstanceOf<System.Globalization.GregorianCalendar>(), "ar-SA counts by Umm al-Qura here");
        var calendar = Shown(new Calendar { Language = "ar-SA", DisplayDate = October });
        var first = calendar.DayButtons.First(d => !d.IsInactive);

        Assert.Multiple(() =>
        {
            Assert.That(first.Content, Is.EqualTo("1"), "the first of a Hijri month");
            Assert.That(first.Date, Is.Not.EqualTo(new DateTime(2026, 10, 1)));
            Assert.That(calendar.HeaderText, Does.Contain("1448"));
        });
        Assert.That(() => calendar.DisplayDate = new DateTime(2100, 1, 1), Throws.Nothing);
        Assert.That(calendar.DisplayDate.Year, Is.LessThan(2100), "held within what the calendar counts");
    }

    [TestCase("ar-SA")]
    [TestCase("fa-IR")]
    public void TheGregorianCalendar_CanBeAskedFor(string language)
    {
        var calendar = Shown(new Calendar { Language = language, DisplayDate = October, CalendarSystem = CalendarSystem.Gregorian });

        Assert.Multiple(() =>
        {
            Assert.That(calendar.DayButtons.Single(d => d.Date == new DateTime(2026, 10, 1)).Content, Is.EqualTo("1"));
            Assert.That(calendar.HeaderText, Does.Contain("2026"));
        });
    }

    [Test]
    public void AHebrewLeapYear_HasThirteenMonths()
    {
        var calendar = Shown(new Calendar
        {
            Language = "he-IL", CalendarSystem = CalendarSystem.Hebrew, DisplayDate = new DateTime(2024, 3, 1),
            DisplayMode = CalendarMode.Year,
        });

        Assert.That(calendar.CalendarButtons, Has.Count.EqualTo(13));
        Assert.That(calendar.CalendarButtons.Select(c => c.Content), Is.Unique);
    }

    [Test]
    public void AnEra_IsNamedWithItsYears()
    {
        var calendar = Shown(new Calendar
        {
            Language = "ja-JP", CalendarSystem = CalendarSystem.Japanese, DisplayDate = October, DisplayMode = CalendarMode.Year,
        });

        Assert.That(calendar.HeaderText, Does.Contain("令和"));
    }

    [Test]
    public void Thai_KeepsItsBuddhistYears()
    {
        var calendar = Shown(new Calendar { Language = "th-TH", DisplayDate = October });

        Assert.That(calendar.HeaderText, Does.Contain("2569"));
    }

    [Test]
    public void SeveralMonths_StandSideBySide_EachNamed()
    {
        var calendar = Shown(new Calendar { Language = "en-US", DisplayDate = October, MonthsShown = 2 });

        string[] titles = ["October 2026", "November 2026"];
        Assert.Multiple(() =>
        {
            Assert.That(calendar.Months, Has.Count.EqualTo(2));
            Assert.That(calendar.DayButtons, Has.Count.EqualTo(84));
            Assert.That(calendar.Months.Select(m => m.Title), Is.EqualTo(titles));
            Assert.That(calendar.Months.All(m => m.IsTitleShown), Is.True);
            Assert.That(calendar.HeaderText, Is.EqualTo("October 2026 – November 2026"));
            Assert.That(calendar.Months[1].RenderSize.Width, Is.GreaterThan(0));
            Assert.That(calendar.DayButtons.Count(d => d.Date == new DateTime(2026, 11, 1) && d.Content != null),
                Is.EqualTo(1), "a day the first month runs into is shown in its own month only");
        });
    }

    [Test]
    public void SeveralMonths_TheKeyboard_RunsOnIntoTheNextAndBringsItIn()
    {
        var calendar = Shown(new Calendar { DisplayDate = October, MonthsShown = 2 });
        calendar.SelectedDate = new DateTime(2026, 11, 30);
        FocusManager.Focus(calendar, NavigationMethod.Tab);

        Press(calendar, Key.RightArrow);

        Assert.Multiple(() =>
        {
            Assert.That(calendar.SelectedDate, Is.EqualTo(new DateTime(2026, 12, 1)));
            Assert.That(calendar.DisplayDate.Month, Is.EqualTo(11), "one month on, December the last shown");
        });
    }

    [Test]
    public void SeveralMonths_ADragRunsAcrossThem()
    {
        var calendar = Shown(new Calendar
        {
            DisplayDate = October, MonthsShown = 2, SelectionMode = CalendarSelectionMode.SingleRange,
        });

        Drag(calendar, InputModifiers.None, new DateTime(2026, 10, 30), new DateTime(2026, 11, 2));

        Assert.That(calendar.SelectedDates, Has.Count.EqualTo(4));
    }

    [Test]
    public void SeveralMonths_AutomationSeesOneGridAcrossThem()
    {
        var calendar = Shown(new Calendar { Language = "en-US", DisplayDate = October, MonthsShown = 2 });
        var grid = (IGridProvider)calendar.GetAutomationPeer().GetPattern(PatternId.Grid);

        Assert.That((grid.RowCount, grid.ColumnCount), Is.EqualTo((6, 14)));
        Assert.That(grid.GetItem(0, 7).Name, Is.EqualTo("Sunday, November 1, 2026"));
    }

    [Test]
    public void MarkedDays_WearTheirClasses()
    {
        ObservableCollection<CalendarDateMark> marks =
        [
            new() { Start = new DateTime(2026, 10, 5), End = new DateTime(2026, 10, 6), Classes = "Holiday" },
        ];
        var calendar = Shown(new Calendar { DisplayDate = October, MarkedDates = marks });
        CalendarDayButton Day(int day) => calendar.DayButtons.Single(d => d.Date == new DateTime(2026, 10, day));

        Assert.Multiple(() =>
        {
            Assert.That((Day(5).IsMarked, Day(6).IsMarked, Day(7).IsMarked), Is.EqualTo((true, true, false)));
            Assert.That(Day(5).Classes, Does.Contain("Holiday"));
        });

        marks[0].End = new DateTime(2026, 10, 7);
        marks.Add(new CalendarDateMark { Start = new DateTime(2026, 10, 20) });

        Assert.Multiple(() =>
        {
            Assert.That(Day(7).IsMarked, Is.True, "a mark moved");
            Assert.That(Day(20).IsMarked, Is.True, "a mark added");
            Assert.That(Day(20).Classes, Is.Null.Or.Empty);
        });
    }

    [Test]
    public void TheDayStyle_IsAddedToEveryDay()
    {
        var style = new Style();
        var calendar = Shown(new Calendar { DisplayDate = October, CalendarDayButtonStyle = style });

        Assert.That(calendar.DayButtons.All(d => d.Styles.Contains(style)), Is.True);

        calendar.CalendarDayButtonStyle = null;

        Assert.That(calendar.DayButtons.Any(d => d.Styles.Contains(style)), Is.False);
    }

    [Test]
    public void ADayKnowsItsDayOfTheWeek()
    {
        var calendar = Shown(new Calendar { DisplayDate = October });

        Assert.That(calendar.DayButtons.All(d => d.DayOfWeek == d.Date.DayOfWeek), Is.True);
    }

    [Test]
    public void AnotherMonth_SlidesOverToTheOtherPage()
    {
        var calendar = Shown(new Calendar { DisplayDate = October });
        var before = Pages(calendar).Single(p => p.Visibility == Visibility.Visible);

        calendar.DisplayDate = new DateTime(2026, 11, 1);

        var after = Pages(calendar).Single(p => p != before);
        Assert.Multiple(() =>
        {
            Assert.That(calendar.DayButtons[0], Is.SameAs(after.Months[0].DayButtons[0]), "the days are the new page's");
            Assert.That(before.Visibility, Is.EqualTo(Visibility.Visible), "still sliding out");
            Assert.That(after.RenderTransform?.TranslateY, Is.GreaterThan(0), "coming up from below");
        });

        AnimationTime.Pass(0.4);

        Assert.Multiple(() =>
        {
            Assert.That(before.Visibility, Is.EqualTo(Visibility.Hidden), "gone once it slid out");
            Assert.That(after.RenderTransform.TranslateY, Is.EqualTo(0).Within(0.5));
        });
    }

    [Test]
    public void ALevelUp_ZoomsToTheOtherPage()
    {
        var calendar = Shown(new Calendar { DisplayDate = October });
        var before = Pages(calendar).Single(p => p.Visibility == Visibility.Visible);

        calendar.DisplayMode = CalendarMode.Year;

        var after = Pages(calendar).Single(p => p != before);
        Assert.Multiple(() =>
        {
            Assert.That(after.DisplayMode, Is.EqualTo(CalendarMode.Year));
            Assert.That(before.DisplayMode, Is.EqualTo(CalendarMode.Month), "the days zoom away");
            Assert.That(after.RenderTransform?.ScaleX, Is.GreaterThan(1), "the months come in from nearer");
        });

        AnimationTime.Pass(0.4);

        Assert.Multiple(() =>
        {
            Assert.That(before.Visibility, Is.EqualTo(Visibility.Hidden));
            Assert.That(after.Opacity, Is.EqualTo(1).Within(0.01));
            Assert.That(after.RenderTransform.ScaleX, Is.EqualTo(1).Within(0.01));
        });
    }

    [Test]
    public void NoDuration_MovesAtOnce()
    {
        var calendar = Shown(new Calendar { DisplayDate = October, TransitionDuration = 0 });
        var before = Pages(calendar).Single(p => p.Visibility == Visibility.Visible);

        calendar.DisplayDate = new DateTime(2026, 11, 1);

        Assert.That(Pages(calendar).Single(p => p.Visibility == Visibility.Visible), Is.SameAs(before));
    }

    [Test]
    public void ADragFromAnotherMonthsDay_KeepsTheMonthUntilLetGo()
    {
        var calendar = Shown(new Calendar
        {
            Language = "en-US", DisplayDate = new DateTime(2026, 1, 15), SelectionMode = CalendarSelectionMode.SingleRange,
        });

        Drag(calendar, InputModifiers.None, new DateTime(2026, 2, 3), new DateTime(2026, 2, 5));

        DateTime[] run = [new(2026, 2, 3), new(2026, 2, 4), new(2026, 2, 5)];
        Assert.That(calendar.SelectedDates, Is.EquivalentTo(run));
        Assert.That(calendar.DisplayDate.Month, Is.EqualTo(2), "February, once let go");
    }

    [Test]
    public void ACtrlDrag_GrowsAndShrinksItsRun_KeepingTheRest()
    {
        var calendar = Shown(new Calendar { DisplayDate = October, SelectionMode = CalendarSelectionMode.MultipleRange });
        calendar.SelectedDates.Add(new DateTime(2026, 10, 1));

        Drag(calendar, InputModifiers.LeftControl,
            new DateTime(2026, 10, 5), new DateTime(2026, 10, 10), new DateTime(2026, 10, 7));

        DateTime[] kept = [new(2026, 10, 1), new(2026, 10, 5), new(2026, 10, 6), new(2026, 10, 7)];
        Assert.That(calendar.SelectedDates, Is.EquivalentTo(kept));
    }

    [Test]
    public void AStruckOutDay_CannotBePressed()
    {
        var calendar = Shown(new Calendar { DisplayDate = October, SelectionMode = CalendarSelectionMode.SingleRange });
        calendar.BlackoutDates.Add(new CalendarDateRange(new DateTime(2026, 10, 10)));
        calendar.SelectedDate = new DateTime(2026, 10, 20);

        Drag(calendar, InputModifiers.None, new DateTime(2026, 10, 10), new DateTime(2026, 10, 9));

        DateTime[] one = [new(2026, 10, 20)];
        Assert.Multiple(() =>
        {
            Assert.That(calendar.DayButtons.Single(d => d.Date == new DateTime(2026, 10, 10)).IsEnabled, Is.False);
            Assert.That(calendar.SelectedDates, Is.EqualTo(one));
        });
    }

    [Test]
    public void ShiftAndAnArrow_WithNothingSelected_SelectsBothDays()
    {
        // A month without today: the cursor starts on today where it is shown, and on the date shown elsewhere.
        var shown = DateTime.Today.AddMonths(-2);
        var calendar = Shown(new Calendar { DisplayDate = shown, SelectionMode = CalendarSelectionMode.SingleRange });
        FocusManager.Focus(calendar, NavigationMethod.Tab);

        Press(calendar, Key.RightArrow, InputModifiers.LeftShift);

        DateTime[] both = [shown, shown.AddDays(1)];
        Assert.That(calendar.SelectedDates, Is.EquivalentTo(both));
    }

    [TestCase(CalendarMode.Year)]
    [TestCase(CalendarMode.Decade)]
    public void BackFromTheFirstEra_StaysPut(CalendarMode mode)
    {
        var calendar = Shown(new Calendar
        {
            Language = "ja-JP", CalendarSystem = CalendarSystem.Japanese, DisplayDate = new DateTime(1868, 10, 1), DisplayMode = mode,
        });

        Assert.That(() => ((ButtonBase)calendar.GetTemplateChild("PART_Previous")).PerformClick(), Throws.Nothing);
    }

    [Test]
    public void TheFirstYearACalendarCounts_IsReachedAndShown()
    {
        var calendar = Shown(new Calendar
        {
            Language = "he-IL", CalendarSystem = CalendarSystem.Hebrew, DisplayDate = new DateTime(1585, 6, 1),
            DisplayMode = CalendarMode.Year,
        });
        var previous = (ButtonBase)calendar.GetTemplateChild("PART_Previous");

        previous.PerformClick();
        previous.PerformClick();

        Assert.That(calendar.DisplayDate, Is.LessThan(new DateTime(1583, 10, 1)), "the year the calendar starts in");
        Assert.That(calendar.CalendarButtons.Any(c => c.Content != null && c.IsEnabled), Is.True);
    }

    [Test]
    public void TheTenthYear_IsInOneDecadeOnly()
    {
        var calendar = Shown(new Calendar
        {
            Language = "ja-JP", CalendarSystem = CalendarSystem.Japanese, DisplayDate = October, DisplayMode = CalendarMode.Decade,
        });

        Assert.That(calendar.CalendarButtons.Count(c => !c.IsInactive), Is.EqualTo(9), "Reiwa 1 to 9; 10 is the next decade's");
        Assert.That(calendar.HeaderText, Does.EndWith("令和9年"));
    }

    [Test]
    public void TheNextYear_CrossesIntoTheNextEra_AtItsStart()
    {
        var calendar = Shown(new Calendar
        {
            Language = "ja-JP", CalendarSystem = CalendarSystem.Japanese, DisplayDate = new DateTime(1926, 6, 1),
            DisplayMode = CalendarMode.Year,
        });
        var next = (ButtonBase)calendar.GetTemplateChild("PART_Next");

        next.PerformClick();
        Assert.That(calendar.DisplayDate, Is.EqualTo(new DateTime(1926, 12, 25)), "Showa 1, from its first day");

        next.PerformClick();
        Assert.That(calendar.DisplayDate.Year, Is.EqualTo(1927), "Showa 2");
    }

    [Test]
    public void BackIntoTheFirstYearOfAnEraStartedMidYear_LandsOnItsStart()
    {
        var calendar = Shown(new Calendar
        {
            Language = "ja-JP", CalendarSystem = CalendarSystem.Japanese, DisplayDate = new DateTime(1869, 7, 29),
            DisplayMode = CalendarMode.Year,
        });

        ((ButtonBase)calendar.GetTemplateChild("PART_Previous")).PerformClick();

        Assert.That(calendar.DisplayDate, Is.EqualTo(FirstCountedDay(new System.Globalization.JapaneseCalendar())),
            "Meiji 1, from the first day counted");
    }

    private static DateTime FirstCountedDay(System.Globalization.Calendar counting)
    {
        var last = counting.MinSupportedDateTime.Date.AddYears(2);
        for (var day = counting.MinSupportedDateTime.Date; day < last; day = day.AddDays(1))
        {
            try
            {
                counting.GetEra(day);
                return day;
            }
            catch (ArgumentException)
            {
            }
        }

        Assert.Fail("the calendar counts no day of its first two years");
        return last;
    }

    [Test]
    public void AHebrewMonth_IsTheSameMonthInALeapYear()
    {
        var hebrew = new System.Globalization.HebrewCalendar();
        var nisan5784 = hebrew.ToDateTime(5784, 8, 1, 0, 0, 0, 0);
        var calendar = Shown(new Calendar
        {
            Language = "he-IL", CalendarSystem = CalendarSystem.Hebrew, DisplayDate = nisan5784, DisplayMode = CalendarMode.Year,
        });

        ((ButtonBase)calendar.GetTemplateChild("PART_Next")).PerformClick();

        Assert.That(hebrew.GetMonth(calendar.DisplayDate), Is.EqualTo(7), "Nisan is the seventh month of an ordinary year");
    }

    [Test]
    public void TheNextYear_KeepsTheMonth()
    {
        var calendar = Shown(new Calendar { DisplayDate = October, DisplayMode = CalendarMode.Year });

        ((ButtonBase)calendar.GetTemplateChild("PART_Next")).PerformClick();

        Assert.That((calendar.DisplayDate.Year, calendar.DisplayDate.Month), Is.EqualTo((2027, 10)));
    }

    [Test]
    public void Automation_SeesOnlyThePageShown()
    {
        var calendar = Shown(new Calendar { Language = "en-US", DisplayDate = October });

        calendar.DisplayDate = new DateTime(2026, 11, 1);
        AnimationTime.Pass(0.4);

        var months = calendar.GetAutomationPeer().GetChildren().Where(p => p.ControlType == AutomationControlType.Group).ToList();
        string[] shown = ["November 2026"];
        Assert.That(months.Select(m => m.Name), Is.EqualTo(shown));
        Assert.That(months[0].GetChildren().Count(p => p.ControlType == AutomationControlType.DataItem), Is.EqualTo(42));
    }

    [Test]
    public void TheVanishingPage_TakesNoClick()
    {
        var calendar = Shown(new Calendar { DisplayDate = October });
        var before = Pages(calendar).Single(p => p.Visibility == Visibility.Visible);

        calendar.DisplayMode = CalendarMode.Year;

        Assert.That((before.IsHitTestVisible, Pages(calendar).Single(p => p != before).IsHitTestVisible),
            Is.EqualTo((false, true)));
        AnimationTime.Pass(0.4);
        Assert.That(before.IsHitTestVisible, Is.True, "back once it is hidden");
    }

    [Test]
    public void ACalendarTakenOutMidSlide_ComesBackSettled()
    {
        var calendar = Shown(new Calendar { DisplayDate = October });

        calendar.DisplayDate = new DateTime(2026, 11, 1);
        _window.Content = null;
        Settle();
        _window.Content = calendar;
        Settle();

        var visible = Pages(calendar).Where(p => p.Visibility == Visibility.Visible).ToList();
        Assert.That(visible, Has.Count.EqualTo(1));
        Assert.That(visible[0].RenderTransform?.TranslateY ?? 0, Is.EqualTo(0));
        Assert.That(calendar.DayButtons[0], Is.SameAs(visible[0].DayButtons[0]));
    }

    [Test]
    public void ThePageSlidingOut_KeepsWhatItShowed()
    {
        var calendar = Shown(new Calendar
        {
            Language = "he-IL", CalendarSystem = CalendarSystem.Hebrew, DisplayDate = new DateTime(2023, 3, 1),
            DisplayMode = CalendarMode.Year,
        });
        var before = Pages(calendar).Single(p => p.Visibility == Visibility.Visible);

        calendar.DisplayDate = new DateTime(2024, 3, 1);

        Assert.That(before.CalendarButtons.All(c => c.Content != null), Is.True, "a leap year's thirteen cells are not its");
        Assert.That(calendar.CalendarButtons, Has.Count.EqualTo(13));
    }

    [Test]
    public void TheDays_MayBeWalkedWhileSelecting()
    {
        var calendar = Shown(new Calendar { DisplayDate = October, SelectionMode = CalendarSelectionMode.MultipleRange });

        Assert.That(() =>
        {
            foreach (var day in calendar.DayButtons.Where(d => !d.IsInactive))
            {
                calendar.SelectedDates.Add(day.Date);
            }
        }, Throws.Nothing);
    }

    [Test]
    public void ALanguageWhoseCalendarEndsSooner_HoldsTheDatesWithinIt()
    {
        Assume.That(System.Globalization.CultureInfo.GetCultureInfo("ar-SA").Calendar,
            Is.Not.InstanceOf<System.Globalization.GregorianCalendar>(), "ar-SA counts by Umm al-Qura here");
        var calendar = Shown(new Calendar { DisplayDate = new DateTime(2100, 5, 1) });
        calendar.SelectedDate = new DateTime(2100, 5, 3);

        Assert.That(() => calendar.Language = "ar-SA", Throws.Nothing);

        Assert.Multiple(() =>
        {
            Assert.That(calendar.DisplayDate.Year, Is.LessThan(2078));
            Assert.That(calendar.SelectedDate, Is.Null, "a day the calendar no longer counts is let go");
        });
    }

    [Test]
    public void ASystemThatEndsSooner_LetsAnOutOfRangeSelectionGo()
    {
        var calendar = Shown(new Calendar { DisplayDate = new DateTime(2100, 5, 1), CalendarSystem = CalendarSystem.Gregorian });
        calendar.SelectedDate = new DateTime(2100, 5, 3);

        Assert.That(() => calendar.CalendarSystem = CalendarSystem.UmAlQura, Throws.Nothing);
        Assert.That(calendar.SelectedDate, Is.Null);
    }

    [TestCase(CalendarSystem.Japanese, "ja-JP", 1868, 9, 10)]
    [TestCase(CalendarSystem.Hebrew, "he-IL", 1583, 1, 5)]
    public void TheFirstMonthACalendarCounts_IsShownFromItsFirstDay(CalendarSystem system, string language, int year,
        int month, int day)
    {
        var calendar = Shown(new Calendar { Language = language, CalendarSystem = system });

        foreach (var mode in (CalendarMode[])[CalendarMode.Month, CalendarMode.Year, CalendarMode.Decade])
        {
            Assert.That(() =>
            {
                calendar.DisplayMode = mode;
                calendar.DisplayDate = new DateTime(year, month, day);
            }, Throws.Nothing, mode.ToString());
        }
    }

    [Test]
    public void AJapaneseDecade_IsYearsOfItsEra()
    {
        var calendar = Shown(new Calendar
        {
            Language = "ja-JP", CalendarSystem = CalendarSystem.Japanese, DisplayDate = October, DisplayMode = CalendarMode.Decade,
        });

        Assert.Multiple(() =>
        {
            Assert.That(calendar.HeaderText, Does.StartWith("令和"));
            Assert.That(calendar.CalendarButtons.Single(c => c.IsCurrent).Content, Is.EqualTo("令和8年"));
        });
    }

    [Test]
    public void AnEraThatEnded_HasNoYearsPastItsLast()
    {
        var calendar = Shown(new Calendar
        {
            Language = "ja-JP", CalendarSystem = CalendarSystem.Japanese, DisplayDate = new DateTime(2019, 2, 1),
            DisplayMode = CalendarMode.Decade,
        });

        Assert.Multiple(() =>
        {
            Assert.That(calendar.HeaderText, Does.StartWith("平成30年").And.EndsWith("平成31年"));
            Assert.That(calendar.CalendarButtons.Count(c => c.Content != null), Is.EqualTo(3), "29, 30 and 31");
        });
    }

    [Test]
    public void AnErasFirstYear_OpensWithinIt_AndTheNextYearFollowsTheLastOfTheOneBefore()
    {
        var calendar = Shown(new Calendar
        {
            Language = "ja-JP", CalendarSystem = CalendarSystem.Japanese, DisplayDate = new DateTime(2020, 6, 1),
            DisplayMode = CalendarMode.Decade,
        });
        var first = calendar.CalendarButtons.Single(c => c.Date == new DateTime(2019, 5, 1));

        first.PerformClick();

        Assert.That((calendar.DisplayMode, calendar.HeaderText), Is.EqualTo((CalendarMode.Year, first.Content)));
        Assert.That(calendar.CalendarButtons.Take(4).All(c => c.Content == null), Is.True, "January to April were Heisei's");

        calendar.DisplayMode = CalendarMode.Decade;
        FocusManager.Focus(calendar, NavigationMethod.Tab);
        Press(calendar, Key.LeftArrow);

        Assert.That(calendar.HeaderText, Does.EndWith("平成31年"), "Heisei 31 is reached");
    }

    [Test]
    public void AJapaneseMonth_IsNamedWithItsEra()
    {
        var calendar = Shown(new Calendar { Language = "ja-JP", CalendarSystem = CalendarSystem.Japanese, DisplayDate = October });

        Assert.That(calendar.HeaderText, Does.Contain("令和"));
    }

    [Test]
    public void ACalendarOfAnotherLanguage_KeepsTheLanguagesWeek()
    {
        var calendar = Shown(new Calendar { Language = "en-US", CalendarSystem = CalendarSystem.Hebrew, DisplayDate = October });

        Assert.That(Texts(calendar.Months[0], "PART_DayNames")[0], Is.EqualTo("Sun"));
    }

    [Test]
    public void AnEndlessDuration_MovesAtOnce()
    {
        var calendar = Shown(new Calendar { DisplayDate = October, TransitionDuration = double.PositiveInfinity });

        Assert.That(() => calendar.DisplayDate = new DateTime(2026, 11, 1), Throws.Nothing);
        Assert.That(Pages(calendar).Count(p => p.Visibility == Visibility.Visible), Is.EqualTo(1));
    }

    [Test]
    public void SeveralMonths_AtTheLastMonthThereIs_NameTheLastShown()
    {
        var calendar = Shown(new Calendar { Language = "en-US", DisplayDate = new DateTime(9999, 11, 1), MonthsShown = 3 });

        Assert.That(calendar.HeaderText, Is.EqualTo("November 9999 – December 9999"));
    }

    [Test]
    public void TheCursor_FollowsTheMonthShown()
    {
        var calendar = Shown(new Calendar { DisplayDate = October });
        calendar.SelectedDate = new DateTime(2026, 10, 18);
        FocusManager.Focus(calendar, NavigationMethod.Tab);

        calendar.DisplayDate = new DateTime(2026, 12, 18);
        Press(calendar, Key.RightArrow);

        Assert.That(calendar.SelectedDate, Is.EqualTo(new DateTime(2026, 12, 19)));
    }

    [Test]
    public void AKeyOnTheHeader_IsNotTheDays()
    {
        var calendar = Shown(new Calendar { DisplayDate = October });
        calendar.SelectedDate = new DateTime(2026, 10, 18);
        var header = (Button)calendar.GetTemplateChild("PART_Header");

        header.RaiseEvent(new KeyEventArgs(KeyboardDevice.CurrentDevice, Key.LeftArrow, InputModifiers.None, 0)
        {
            RoutedEvent = Keyboard.KeyDownEvent,
        });

        Assert.That(calendar.SelectedDate, Is.EqualTo(new DateTime(2026, 10, 18)));
    }

    [Test]
    public void CtrlUp_AfterAClick_GoesUpALevelAtOnce()
    {
        var calendar = Shown(new Calendar { DisplayDate = October });
        FocusManager.Focus(calendar, NavigationMethod.Mouse);

        Press(calendar, Key.UpArrow, InputModifiers.LeftControl);

        Assert.That(calendar.DisplayMode, Is.EqualTo(CalendarMode.Year));
    }

    [Test]
    public void PageDown_AYearOfMonths_ShowsTheNextYear()
    {
        var calendar = Shown(new Calendar { DisplayDate = October, DisplayMode = CalendarMode.Year });
        FocusManager.Focus(calendar, NavigationMethod.Tab);

        Press(calendar, Key.PageDown);

        Assert.That(calendar.DisplayDate.Year, Is.EqualTo(2027));
    }

    [Test]
    public void TheApplicationsLanguage_IsFollowed()
    {
        var before = Languages.Current;
        try
        {
            Languages.Current = "en-US";
            var calendar = Shown(new Calendar { DisplayDate = October });
            Assert.That(calendar.HeaderText, Is.EqualTo("October 2026"));

            Languages.Current = "ru-RU";

            Assert.That(calendar.HeaderText, Does.StartWith("октябрь"));
        }
        finally
        {
            Languages.Current = before;
        }
    }

    [Test]
    public void AHandlerOfTheSelection_MayChangeIt()
    {
        var calendar = Shown(new Calendar { DisplayDate = October, SelectionMode = CalendarSelectionMode.MultipleRange });
        calendar.SelectedDates.CollectionChanged += (_, _) => { };
        calendar.SelectedDatesChanged += (_, _) =>
        {
            if (calendar.SelectedDates.Count == 1)
            {
                calendar.SelectedDates.Add(new DateTime(2026, 10, 2));
            }
        };

        Assert.That(() => calendar.SelectedDates.Add(new DateTime(2026, 10, 1)), Throws.Nothing);
        Assert.That(calendar.SelectedDates, Has.Count.EqualTo(2));
    }

    [Test]
    public void ARangeMovedOverASelectedDay_StrikesItAndLetsItGo()
    {
        var calendar = Shown(new Calendar { DisplayDate = October });
        var range = new CalendarDateRange(new DateTime(2026, 10, 1));
        calendar.BlackoutDates.Add(range);
        calendar.SelectedDate = new DateTime(2026, 10, 5);

        range.End = new DateTime(2026, 10, 10);

        Assert.Multiple(() =>
        {
            Assert.That(calendar.DayButtons.Single(d => d.Date == new DateTime(2026, 10, 5)).IsBlackedOut, Is.True);
            Assert.That(calendar.SelectedDate, Is.Null);
        });
    }

    [Test]
    public void SingleRange_ADayAddedApart_ReplacesTheRun()
    {
        var calendar = Shown(new Calendar { DisplayDate = October, SelectionMode = CalendarSelectionMode.SingleRange });
        calendar.SelectedDates.AddRange(new DateTime(2026, 10, 1), new DateTime(2026, 10, 3));

        calendar.SelectedDates.Add(new DateTime(2026, 10, 20));

        DateTime[] one = [new(2026, 10, 20)];
        Assert.That(calendar.SelectedDates, Is.EqualTo(one));
    }

    [Test]
    public void ARunOverAStruckDay_IsRefusedWhole()
    {
        var calendar = Shown(new Calendar { DisplayDate = October, SelectionMode = CalendarSelectionMode.MultipleRange });
        calendar.BlackoutDates.Add(new CalendarDateRange(new DateTime(2026, 10, 5)));
        var changes = 0;
        calendar.SelectedDatesChanged += (_, _) => changes++;

        Assert.That(() => calendar.SelectedDates.AddRange(new DateTime(2026, 10, 1), new DateTime(2026, 10, 10)),
            Throws.TypeOf<ArgumentOutOfRangeException>());
        Assert.That((calendar.SelectedDates.Count, changes), Is.EqualTo((0, 0)));

        calendar.SelectedDates.AddRange(new DateTime(2026, 10, 6), new DateTime(2026, 10, 9));

        Assert.That((calendar.SelectedDates.Count, changes), Is.EqualTo((4, 1)), "one change for the run");
    }

    [Test]
    public void ASelectedDate_LosesItsTime()
    {
        var calendar = Shown(new Calendar { DisplayDate = October });
        calendar.SelectedDate = new DateTime(2026, 10, 5);

        calendar.SelectedDate = new DateTime(2026, 10, 5, 14, 0, 0);

        Assert.That(calendar.SelectedDate, Is.EqualTo(new DateTime(2026, 10, 5)));
    }
}
