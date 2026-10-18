using System;
using System.ComponentModel;
using System.Linq;
using Adamantium.Mathematics;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Buttons;
using Adamantium.UI.Controls.Panels;
using Adamantium.UI.Controls.Primitives;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Automation;
using Adamantium.UI.Core.Data;
using Adamantium.UI.Core.Input;
using Adamantium.UI.Core.Localization;
using Adamantium.UI.Core.RoutedEvents;
using Adamantium.UI.Core.Templates;
using Adamantium.UI.Extensions;
using NUnit.Framework;

namespace Adamantium.UITests;

/// <summary>A calendar as WPF's: the days of a month laid out from the language's first day of the week, selected one,
/// a run or several runs at a time, struck-through and out-of-range days refused, a level up to the months and years,
/// the keyboard, automation - and cells that share whatever room the calendar is given.</summary>
[TestFixture]
public class CalendarTests
{
    private static readonly DateTime October = new(2026, 10, 18);

    private Window _window;

    [TearDown]
    public void LetGoOfTheFocus() => FocusManager.ResetFocus();

    private Calendar Shown(Calendar calendar, double width = double.NaN, double height = double.NaN)
    {
        calendar.Template = new ControlTemplate(() =>
        {
            var header = new Button();
            var previous = new RepeatButton();
            var next = new RepeatButton();
            var names = new UniformGrid { Columns = 7, Rows = 1 };
            var weeks = new UniformGrid { Columns = 1, Rows = 6 };
            var days = new UniformGrid { Columns = 7, Rows = 6 };
            var cells = new UniformGrid { Columns = 4, Rows = 3 };
            var root = new Grid
            {
                RowDefinitions =
                {
                    new RowDefinition { Height = GridLength.Auto }, new RowDefinition { Height = GridLength.Auto },
                    new RowDefinition { Height = new GridLength(1, GridUnitType.Star) },
                },
                ColumnDefinitions =
                {
                    new ColumnDefinition { Width = GridLength.Auto },
                    new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                },
            };
            var bar = new StackPanel { Orientation = Orientation.Horizontal };
            bar.Children.Add(header);
            bar.Children.Add(previous);
            bar.Children.Add(next);
            Grid.SetColumnSpan(bar, 2);
            Grid.SetRow(names, 1);
            Grid.SetColumn(names, 1);
            Grid.SetRow(weeks, 2);
            Grid.SetRow(days, 2);
            Grid.SetColumn(days, 1);
            Grid.SetRow(cells, 2);
            Grid.SetColumn(cells, 1);
            root.Children.Add(bar);
            root.Children.Add(names);
            root.Children.Add(weeks);
            root.Children.Add(days);
            root.Children.Add(cells);
            var result = new TemplateResult { RootComponent = root };
            result.RegisterName("PART_Header", header);
            result.RegisterName("PART_Previous", previous);
            result.RegisterName("PART_Next", next);
            result.RegisterName("PART_DayNames", names);
            result.RegisterName("PART_WeekNumbers", weeks);
            result.RegisterName("PART_Days", days);
            result.RegisterName("PART_Cells", cells);
            return result;
        });
        calendar.Width = width;
        calendar.Height = height;
        calendar.HorizontalAlignment = HorizontalAlignment.Left;
        calendar.VerticalAlignment = VerticalAlignment.Top;
        _window = new Window { Width = 1000, Height = 900, Content = calendar };
        Settle();
        return calendar;
    }

    private void Settle()
    {
        for (var i = 0; i < 4; i++)
        {
            WindowExtension.UpdateTree(_window);
        }
    }

    private static void Press(Calendar calendar, Key key, InputModifiers modifiers = InputModifiers.None) =>
        calendar.RaiseEvent(new KeyEventArgs(KeyboardDevice.CurrentDevice, key, modifiers, 0)
        {
            RoutedEvent = Keyboard.KeyDownEvent,
        });

    private void Drag(Calendar calendar, InputModifiers modifiers, params DateTime[] path)
    {
        var days = path.Select(date => calendar.DayButtons.Single(day => day.Date == date)).ToList();
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
            Assert.That(((UniformGrid)calendar.GetTemplateChild("PART_DayNames")).Children
                .OfType<Adamantium.UI.Controls.Text.TextBlock>().First().Text, Is.EqualTo(firstName));
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
        var weeks = ((UniformGrid)calendar.GetTemplateChild("PART_WeekNumbers")).Children
            .OfType<Adamantium.UI.Controls.Text.TextBlock>().Select(week => week.Text).ToArray();

        string[] first = ["40", "41"];
        Assert.That(weeks.Take(2), Is.EqualTo(first));
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
            Assert.That(calendar.DayButtons.Single(day => day.IsSelected).Date, Is.EqualTo(new DateTime(2026, 10, 5)));
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

        Assert.That(calendar.DisplayDate.Month, Is.EqualTo(2));
        Assert.That(calendar.DisplayDate.Year, Is.EqualTo(2027));
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
    public void ADateRefusedAsTheBindingFirstApplies_IsWrittenBack()
    {
        var choice = new DateChoice { Mode = CalendarSelectionMode.None, Date = new DateTime(2026, 10, 3) };
        var calendar = new Calendar { DisplayDate = October, DataContext = choice };
        calendar.SetBinding(Calendar.SelectionModeProperty, new Binding(nameof(DateChoice.Mode)));
        calendar.SetBinding(Calendar.SelectedDateProperty, new Binding(nameof(DateChoice.Date)));
        Shown(calendar);
        BindingUpdateQueue.Flush();

        Assert.That((calendar.SelectedDate, choice.Date), Is.EqualTo(((DateTime?)null, (DateTime?)null)));
    }

    [Test]
    public void Thai_KeepsItsBuddhistYears()
    {
        var calendar = Shown(new Calendar { Language = "th-TH", DisplayDate = October });

        Assert.That(calendar.HeaderText, Does.Contain("2569"));
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
        Assert.That(calendar.SelectedDates.Min(), Is.EqualTo(new DateTime(2026, 10, 10)));
        Assert.That(calendar.SelectedDates.Max(), Is.EqualTo(new DateTime(2026, 10, 17)));
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

    [TestCase("ar-SA")]
    [TestCase("fa-IR")]
    public void ALanguageOfAnotherCalendar_ShowsGregorianDays(string language)
    {
        var calendar = Shown(new Calendar { Language = language, DisplayDate = October });

        Assert.Multiple(() =>
        {
            Assert.That(calendar.DayButtons.Single(d => d.Date == new DateTime(2026, 10, 1)).Content, Is.EqualTo("1"));
            Assert.That(calendar.HeaderText, Does.Contain("2026"));
        });
        Assert.That(() => calendar.DisplayDate = new DateTime(2100, 1, 1), Throws.Nothing);
    }

    [TestCase("ru-RU")]
    [TestCase("en-US")]
    [TestCase("de-DE")]
    public void TheWeekAYearStartsIn_IsItsFirst(string language)
    {
        var calendar = Shown(new Calendar { Language = language, DisplayDate = new DateTime(2026, 1, 15), ShowWeekNumbers = true });
        var weeks = ((UniformGrid)calendar.GetTemplateChild("PART_WeekNumbers")).Children
            .OfType<Adamantium.UI.Controls.Text.TextBlock>().Select(week => week.Text).Take(2);

        string[] first = ["1", "2"];
        Assert.That(weeks, Is.EqualTo(first));
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
        var calendar = Shown(new Calendar { DisplayDate = October, SelectionMode = CalendarSelectionMode.SingleRange });
        FocusManager.Focus(calendar, NavigationMethod.Tab);

        Press(calendar, Key.RightArrow, InputModifiers.LeftShift);

        DateTime[] both = [October, October.AddDays(1)];
        Assert.That(calendar.SelectedDates, Is.EquivalentTo(both));
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

    private sealed class DateChoice : INotifyPropertyChanged
    {
        private CalendarSelectionMode _mode;
        private DateTime? _date;

        public event PropertyChangedEventHandler PropertyChanged;

        public CalendarSelectionMode Mode
        {
            get => _mode;
            set
            {
                _mode = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Mode)));
            }
        }

        public DateTime? Date
        {
            get => _date;
            set
            {
                _date = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Date)));
            }
        }
    }
}
