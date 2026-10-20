using System;
using System.Collections.ObjectModel;
using Adamantium.MVVM;
using Adamantium.UI.Controls;

namespace Adamantium.UI.Sandbox.ViewModels;

/// <summary>Date and time tab: date and time fields in each format, the date one bound to <see cref="SelectedDate"/> with
/// the calendars; calendars - one bound to <see cref="SelectedDate"/> with its selection mode, week numbers and language
/// switched, one with a range and days struck through, several months side by side counted by the chosen calendar with
/// holidays marked, and one given a large size its cells share; lengths of time in fields, and dates, times and lengths
/// in a table of <see cref="Schedule"/> and an inspector of <see cref="Inspected"/>; clocks, and countdowns of
/// <see cref="TimerLength"/> and stopwatches on them.</summary>
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

    [Bindable] private TimeSpan? _length = new TimeSpan(1, 6, 30, 0);

    [Bindable] private TimeSpan? _cooldown = TimeSpan.FromMilliseconds(2500);

    /// <summary>The table's records: a date and time, a day, a time of day, a moment with its offset and a length.</summary>
    public ObservableCollection<ScheduledTask> Schedule { get; } =
    [
        new() { Name = "Release", Due = new DateTime(2026, 11, 2, 18, 0, 0), Day = new DateOnly(2026, 11, 2), Start = new TimeOnly(10, 0), Stamp = new DateTimeOffset(2026, 10, 19, 14, 5, 0, TimeSpan.FromHours(3)), Length = new TimeSpan(2, 4, 0, 0) },
        new() { Name = "Review", Due = new DateTime(2026, 10, 28, 12, 30, 0), Day = new DateOnly(2026, 10, 28), Start = new TimeOnly(15, 45), Stamp = new DateTimeOffset(2026, 10, 20, 9, 0, 0, TimeSpan.FromHours(-5)), Length = TimeSpan.FromMinutes(90) },
        new() { Name = "Cooldown", Due = new DateTime(2026, 10, 21, 9, 0, 0), Day = new DateOnly(2026, 10, 21), Start = new TimeOnly(9, 0), Stamp = new DateTimeOffset(2026, 10, 21, 9, 0, 0, TimeSpan.Zero), Length = TimeSpan.FromSeconds(2.5) },
    ];

    /// <summary>What the inspector shows: the table's first record.</summary>
    public ScheduledTask Inspected => Schedule[0];

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

/// <summary>A record of the date and time tab's table and inspector; each shows what the other writes.</summary>
[ViewModel]
public partial class ScheduledTask
{
    [Bindable] private string _name;

    [Bindable] private DateTime _due;

    [Bindable] private DateOnly _day;

    [Bindable] private TimeOnly _start;

    [Bindable] private DateTimeOffset _stamp;

    [Bindable] private TimeSpan _length;
}
