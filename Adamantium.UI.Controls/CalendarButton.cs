using Adamantium.UI.Controls.Automation;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Automation;

namespace Adamantium.UI.Controls;

/// <summary>A month of a <see cref="Calendar"/>'s year, or a year of its decade: pressed, the calendar opens it.</summary>
public class CalendarButton : CalendarButtonBase
{
    public static readonly AdamantiumProperty IsCurrentProperty = AdamantiumProperty.RegisterReadOnly(nameof(IsCurrent),
        typeof(bool), typeof(CalendarButton), new PropertyMetadata(false));

    public static readonly AdamantiumProperty HasSelectedDatesProperty = AdamantiumProperty.RegisterReadOnly(
        nameof(HasSelectedDates), typeof(bool), typeof(CalendarButton), new PropertyMetadata(false));

    /// <summary>The month or the year of today.</summary>
    public bool IsCurrent
    {
        get => GetValue<bool>(IsCurrentProperty);
        internal set => SetValue(IsCurrentProperty, value);
    }

    /// <summary>Whether a selected day falls in it.</summary>
    public bool HasSelectedDates
    {
        get => GetValue<bool>(HasSelectedDatesProperty);
        internal set => SetValue(HasSelectedDatesProperty, value);
    }

    private protected override int Columns() => Calendar.CellColumns;

    protected override void OnClick()
    {
        base.OnClick();
        Owner?.Open(this);
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new CalendarButtonAutomationPeer(this);
}
