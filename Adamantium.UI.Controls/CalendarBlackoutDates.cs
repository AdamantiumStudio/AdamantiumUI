using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;

namespace Adamantium.UI.Controls;

/// <summary>The days of a <see cref="Calendar"/> that cannot be selected, as WPF's: shown, struck through. A run added
/// over selected days, or moved over them, takes them out of the selection.</summary>
public sealed class CalendarBlackoutDates : ObservableCollection<CalendarDateRange>
{
    private bool _watching;

    internal event EventHandler RangeChanged;

    /// <summary>Whether <paramref name="day"/> is one of them.</summary>
    public bool Contains(DateTime day) => this.Any(range => range.Contains(day));

    /// <summary>Whether a day from <paramref name="start"/> to <paramref name="end"/> is one of them.</summary>
    public bool ContainsAny(DateTime start, DateTime end) => this.Any(range => range.Overlaps(start, end));

    /// <summary>Every day before today.</summary>
    public void AddDatesInPast() => Add(new CalendarDateRange(DateTime.MinValue, DateTime.Today.AddDays(-1)));

    internal void Watch(bool watching)
    {
        if (_watching == watching)
        {
            return;
        }

        _watching = watching;
        foreach (var range in this)
        {
            Watch(range, watching);
        }
    }

    protected override void InsertItem(int index, CalendarDateRange item)
    {
        ArgumentNullException.ThrowIfNull(item);
        Watch(item, _watching);
        base.InsertItem(index, item);
    }

    protected override void SetItem(int index, CalendarDateRange item)
    {
        ArgumentNullException.ThrowIfNull(item);
        Watch(this[index], false);
        Watch(item, _watching);
        base.SetItem(index, item);
    }

    protected override void RemoveItem(int index)
    {
        Watch(this[index], false);
        base.RemoveItem(index);
    }

    protected override void ClearItems()
    {
        foreach (var range in this)
        {
            Watch(range, false);
        }

        base.ClearItems();
    }

    private void Watch(CalendarDateRange range, bool watching)
    {
        range.PropertyChanged -= OnRangeChanged;
        if (watching)
        {
            range.PropertyChanged += OnRangeChanged;
        }
    }

    private void OnRangeChanged(object sender, PropertyChangedEventArgs e) => RangeChanged?.Invoke(this, EventArgs.Empty);
}
