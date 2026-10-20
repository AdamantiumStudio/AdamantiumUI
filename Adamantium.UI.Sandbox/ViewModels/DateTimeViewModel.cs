using System;
using System.Collections.ObjectModel;
using Adamantium.MVVM;
using Adamantium.UI.Controls;

namespace Adamantium.UI.Sandbox.ViewModels;

/// <summary>Date and time tab: date and time fields in each format, the date one bound to <see cref="SelectedDate"/> with
/// the calendars; calendars - one bound to <see cref="SelectedDate"/> with its selection mode, week numbers and language
/// switched, one with a range and days struck through, several months side by side counted by the chosen calendar with
/// holidays marked, and one given a large size its cells share; clocks, and countdowns of <see cref="TimerLength"/> and
/// stopwatches on them.</summary>
[ViewModel]
public partial class DateTimeViewModel : TabPageViewModel
{
    public DateTimeViewModel() : base("DateTime")
    {
    }

    [Bindable] private DateTime? _selectedDate = DateTime.Today;

    [Bindable] private CalendarSelectionMode _selectionMode = CalendarSelectionMode.SingleDate;

    [Bindable] private bool _showWeekNumbers = true;

    [Bindable] private string _calendarLanguage = "ru-RU";

    [Bindable] private CalendarSystem _calendarSystem = CalendarSystem.Language;

    [Bindable] private int _monthsShown = 2;

    [Bindable] private DateTime? _meetingTime = DateTime.Today.AddHours(14).AddMinutes(5);

    [Bindable] private TimeSpan _timerLength = TimeSpan.FromSeconds(90);

    [Bindable] private int _timesDone;

    /// <summary>What a countdown runs when it reaches zero: counts it.</summary>
    [Command] private void TimerDone() => TimesDone++;

    /// <summary>Tokyo's time zone, for a clock of the world's cities.</summary>
    public TimeZoneInfo Tokyo { get; } = Zone("Asia/Tokyo");

    /// <summary>New York's time zone, for a clock of the world's cities.</summary>
    public TimeZoneInfo NewYork { get; } = Zone("America/New_York");

    /// <summary>The first day the range calendar shows: today.</summary>
    public DateTime RangeStart => DateTime.Today;

    /// <summary>The last day the range calendar shows: three months on.</summary>
    public DateTime RangeEnd => DateTime.Today.AddMonths(3);

    private static TimeZoneInfo Zone(string id)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(id);
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.Utc;
        }
    }

    /// <summary>Days the calendar of several months marks.</summary>
    public ObservableCollection<CalendarDateMark> Holidays { get; } =
    [
        new() { Start = new DateTime(2026, 11, 4), Classes = "Holiday" },
        new() { Start = new DateTime(2026, 12, 31), End = new DateTime(2027, 1, 8), Classes = "Holiday" },
        new() { Start = new DateTime(2027, 2, 23), Classes = "Holiday" },
        new() { Start = new DateTime(2027, 3, 8), Classes = "Holiday" },
    ];
}
