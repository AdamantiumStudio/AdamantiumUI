using System;
using Adamantium.UI.Controls.Automation;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Automation;
using Adamantium.UI.Core.Resources;

namespace Adamantium.UI.Controls;

/// <summary>A day of a <see cref="Calendar"/>'s month: its number, and what the calendar says of it - another month's,
/// today, selected, struck through, the one the keyboard is on.</summary>
public class CalendarDayButton : CalendarButtonBase
{
    public static readonly AdamantiumProperty IsTodayProperty = AdamantiumProperty.RegisterReadOnly(nameof(IsToday),
        typeof(bool), typeof(CalendarDayButton), new PropertyMetadata(false));

    public static readonly AdamantiumProperty IsSelectedProperty = AdamantiumProperty.RegisterReadOnly(nameof(IsSelected),
        typeof(bool), typeof(CalendarDayButton), new PropertyMetadata(false));

    public static readonly AdamantiumProperty IsBlackedOutProperty = AdamantiumProperty.RegisterReadOnly(
        nameof(IsBlackedOut), typeof(bool), typeof(CalendarDayButton), new PropertyMetadata(false));

    public static readonly AdamantiumProperty IsMarkedProperty = AdamantiumProperty.RegisterReadOnly(nameof(IsMarked),
        typeof(bool), typeof(CalendarDayButton), new PropertyMetadata(false));

    public static readonly AdamantiumProperty DayOfWeekProperty = AdamantiumProperty.RegisterReadOnly(nameof(DayOfWeek),
        typeof(DayOfWeek), typeof(CalendarDayButton), new PropertyMetadata(DayOfWeek.Sunday));

    private string _markClasses;

    public bool IsToday
    {
        get => GetValue<bool>(IsTodayProperty);
        internal set => SetValue(IsTodayProperty, value);
    }

    public bool IsSelected
    {
        get => GetValue<bool>(IsSelectedProperty);
        internal set => SetValue(IsSelectedProperty, value);
    }

    /// <summary>One of the calendar's <see cref="Calendar.BlackoutDates"/>.</summary>
    public bool IsBlackedOut
    {
        get => GetValue<bool>(IsBlackedOutProperty);
        internal set => SetValue(IsBlackedOutProperty, value);
    }

    /// <summary>One of the calendar's <see cref="Calendar.MarkedDates"/>; it wears their classes.</summary>
    public bool IsMarked
    {
        get => GetValue<bool>(IsMarkedProperty);
        internal set => SetValue(IsMarkedProperty, value);
    }

    /// <summary>The day of the week - what a style picks out weekends by: <c>CalendarDayButton[DayOfWeek=Sunday]</c>.</summary>
    public DayOfWeek DayOfWeek
    {
        get => GetValue<DayOfWeek>(DayOfWeekProperty);
        internal set => SetValue(DayOfWeekProperty, value);
    }

    internal void Mark(string classes)
    {
        IsMarked = classes != null;
        if (_markClasses != classes)
        {
            _markClasses = classes;
            if (string.IsNullOrWhiteSpace(classes))
            {
                ClearValue(ClassesProperty);
            }
            else
            {
                Classes = Classes.Parse(classes);
            }
        }
    }

    internal override int Row => Index % CalendarMonth.Days / Calendar.DayColumns;

    internal override int Column => Index / CalendarMonth.Days * Calendar.DayColumns + Index % Calendar.DayColumns;

    protected override AutomationPeer OnCreateAutomationPeer() => new CalendarDayButtonAutomationPeer(this);
}
