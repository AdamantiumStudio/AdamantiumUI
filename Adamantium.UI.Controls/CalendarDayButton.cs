using Adamantium.UI.Controls.Automation;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Automation;

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

    private protected override int Columns() => Calendar.DayColumns;

    protected override AutomationPeer OnCreateAutomationPeer() => new CalendarDayButtonAutomationPeer(this);
}
