using System;
using System.ComponentModel;

namespace Adamantium.UI.Controls;

/// <summary>A run of days, its first and last included, as WPF's; the times of the dates are not looked at, and the two
/// may be given in either order. <c>&lt;CalendarDateRange Start="2026-12-24" End="2026-12-26"/&gt;</c>; one day where
/// only <see cref="Start"/> is given.</summary>
public sealed class CalendarDateRange : INotifyPropertyChanged
{
    private DateTime _start;
    private DateTime? _end;

    public CalendarDateRange()
    {
    }

    public CalendarDateRange(DateTime day) : this(day, day)
    {
    }

    public CalendarDateRange(DateTime start, DateTime end)
    {
        Start = start;
        End = end;
    }

    public event PropertyChangedEventHandler PropertyChanged;

    public DateTime Start
    {
        get => _start;
        set
        {
            if (_start != value.Date)
            {
                _start = value.Date;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Start)));
            }
        }
    }

    public DateTime End
    {
        get => _end ?? _start;
        set
        {
            if (_end != value.Date)
            {
                _end = value.Date;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(End)));
            }
        }
    }

    private DateTime First => Start <= End ? Start : End;

    private DateTime Last => Start <= End ? End : Start;

    public bool Contains(DateTime day) => day.Date >= First && day.Date <= Last;

    /// <summary>Whether the run has a day in common with the one from <paramref name="start"/> to
    /// <paramref name="end"/>.</summary>
    public bool Overlaps(DateTime start, DateTime end) => First <= end.Date && Last >= start.Date;
}
