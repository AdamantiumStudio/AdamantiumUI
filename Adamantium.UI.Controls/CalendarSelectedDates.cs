using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;

namespace Adamantium.UI.Controls;

/// <summary>The days selected in a <see cref="Calendar"/>, as WPF's: each once, without its time. A day the calendar
/// cannot select - struck through, out of its range, a second one where it selects one - is refused; where it selects
/// one run, a day or a run added replaces the run selected, and a day out of its middle cannot be taken.</summary>
public sealed class CalendarSelectedDates : ObservableCollection<DateTime>
{
    private readonly Calendar _owner;
    private int _batch;
    private bool _changed;
    private bool _added;

    internal CalendarSelectedDates(Calendar owner)
    {
        _owner = owner;
    }

    /// <summary>Every day from <paramref name="start"/> to <paramref name="end"/>, in either order; none of them when one
    /// cannot be selected.</summary>
    public void AddRange(DateTime start, DateTime end)
    {
        var first = start.Date <= end.Date ? start.Date : end.Date;
        var last = start.Date <= end.Date ? end.Date : start.Date;
        _owner.CheckSelectable(first, last);
        if (_owner.SelectionMode == CalendarSelectionMode.SingleDate && (first != last || (Count > 0 && this[0] != first)))
        {
            throw new InvalidOperationException("The calendar selects one day.");
        }

        Batch(() =>
        {
            if (_owner.SelectionMode == CalendarSelectionMode.SingleRange && Count > 0)
            {
                base.ClearItems();
            }

            var selected = this.ToHashSet();
            for (var day = first; ; day = day.AddDays(1))
            {
                if (selected.Add(day))
                {
                    base.InsertItem(Count, day);
                }

                if (day == last)
                {
                    break;
                }
            }
        });
    }

    internal void ReplaceWith(IReadOnlyList<DateTime> days)
    {
        Batch(() =>
        {
            var wanted = days.ToHashSet();
            for (var i = Count - 1; i >= 0; i--)
            {
                if (!wanted.Contains(this[i]))
                {
                    base.RemoveItem(i);
                }
            }

            for (var i = 0; i < days.Count; i++)
            {
                var at = i < Count && this[i] == days[i] ? i : IndexOf(days[i]);
                if (at < 0)
                {
                    base.InsertItem(i, days[i]);
                }
                else if (at != i)
                {
                    base.MoveItem(at, i);
                }
            }
        });
    }

    protected override void InsertItem(int index, DateTime item)
    {
        var day = item.Date;
        if (Contains(day))
        {
            return;
        }

        _owner.CheckSelectable(day, day);
        switch (_owner.SelectionMode)
        {
            case CalendarSelectionMode.SingleDate when Count > 0:
                throw new InvalidOperationException("The calendar selects one day.");
            case CalendarSelectionMode.SingleRange when Count > 0:
                ReplaceWith([day]);
                return;
            default:
                base.InsertItem(index, day);
                return;
        }
    }

    protected override void SetItem(int index, DateTime item)
    {
        var day = item.Date;
        var at = IndexOf(day);
        if (at == index)
        {
            return;
        }

        if (at >= 0)
        {
            throw new InvalidOperationException("The day is selected already.");
        }

        if (_owner.SelectionMode == CalendarSelectionMode.SingleRange && Count > 1)
        {
            throw new InvalidOperationException("Replacing a day would split the one run the calendar selects.");
        }

        _owner.CheckSelectable(day, day);
        base.SetItem(index, day);
    }

    protected override void RemoveItem(int index)
    {
        var day = this[index];
        if (_owner.SelectionMode == CalendarSelectionMode.SingleRange && day != this.Min() && day != this.Max())
        {
            throw new InvalidOperationException("Taking the day out would split the one run the calendar selects.");
        }

        base.RemoveItem(index);
    }

    protected override void ClearItems()
    {
        if (Count > 0)
        {
            base.ClearItems();
        }
    }

    protected override void OnCollectionChanged(NotifyCollectionChangedEventArgs e)
    {
        base.OnCollectionChanged(e);
        _added |= e.Action is NotifyCollectionChangedAction.Add or NotifyCollectionChangedAction.Replace;
        _changed = true;
        if (_batch == 0)
        {
            Announce();
        }
    }

    private void Batch(Action change)
    {
        _batch++;
        try
        {
            change();
        }
        finally
        {
            _batch--;
        }

        if (_batch == 0 && _changed)
        {
            Announce();
        }
    }

    private void Announce()
    {
        var added = _added;
        _changed = false;
        _added = false;
        _owner.OnSelectedDatesChanged(added);
    }
}
