using System;
using System.ComponentModel;

namespace Adamantium.UI.Controls;

/// <summary>Days a <see cref="Calendar"/> marks - an event, a holiday: the days of the run, first and last included, wear
/// <see cref="Classes"/> for styles to pick out. <c>&lt;CalendarDateMark Start="2026-12-31" Classes="Holiday"/&gt;</c>; one
/// day where only <see cref="Start"/> is given.</summary>
public sealed class CalendarDateMark : INotifyPropertyChanged
{
    private DateTime _start;
    private DateTime? _end;
    private string _classes;

    public event PropertyChangedEventHandler PropertyChanged;

    /// <summary>The first day marked; its time is not looked at.</summary>
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

    /// <summary>The last day marked - <see cref="Start"/> where none is given; the two may come in either order.</summary>
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

    /// <summary>The style classes the marked days wear, separated by spaces.</summary>
    public string Classes
    {
        get => _classes;
        set
        {
            if (_classes != value)
            {
                _classes = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Classes)));
            }
        }
    }

    /// <summary>Whether <paramref name="day"/> is one of the days marked.</summary>
    public bool Contains(DateTime day) =>
        day.Date >= (Start <= End ? Start : End) && day.Date <= (Start <= End ? End : Start);
}
