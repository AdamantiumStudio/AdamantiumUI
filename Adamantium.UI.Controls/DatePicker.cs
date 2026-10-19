using System;
using Adamantium.UI.Controls.Automation;
using Adamantium.UI.Controls.Base;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Automation;

namespace Adamantium.UI.Controls;

/// <summary>A date entered part by part, or picked in a calendar the button opens - as WPF's DatePicker, with
/// <see cref="DisplayDateStart"/> and <see cref="DisplayDateEnd"/> bounding the calendar. Picking a day keeps the time of
/// <see cref="DateTimeField.Value"/>.</summary>
public class DatePicker : DateTimePickerBase
{
    public static readonly AdamantiumProperty DisplayDateStartProperty = AdamantiumProperty.Register(
        nameof(DisplayDateStart), typeof(DateTime?), typeof(DatePicker),
        new PropertyMetadata(null, (a, _) => ((DatePicker)a).RefreshDropDown()));

    public static readonly AdamantiumProperty DisplayDateEndProperty = AdamantiumProperty.Register(nameof(DisplayDateEnd),
        typeof(DateTime?), typeof(DatePicker), new PropertyMetadata(null, (a, _) => ((DatePicker)a).RefreshDropDown()));

    private CalendarChoice _choice;

    /// <summary>The first day the calendar offers; null for none.</summary>
    public DateTime? DisplayDateStart
    {
        get => GetValue<DateTime?>(DisplayDateStartProperty);
        set => SetValue(DisplayDateStartProperty, value);
    }

    /// <summary>The last day the calendar offers; null for none.</summary>
    public DateTime? DisplayDateEnd
    {
        get => GetValue<DateTime?>(DisplayDateEndProperty);
        set => SetValue(DisplayDateEndProperty, value);
    }

    /// <summary>The calendar the button opens, once it has been opened.</summary>
    public Calendar DropDownCalendar => _choice?.Calendar;

    protected override MeasurableUIComponent CreateDropDownContent()
    {
        _choice = new CalendarChoice(day => Pick(day + (Value?.TimeOfDay ?? TimeSpan.Zero), close: false), CloseDropDown);
        return _choice.Calendar;
    }

    protected override void PrepareDropDownContent() =>
        _choice.Show(Value, Language, CalendarSystem, DisplayDateStart, DisplayDateEnd);

    protected override IInputComponent DropDownKeyboardTarget() => _choice?.Calendar;

    protected override AutomationPeer OnCreateAutomationPeer() => new DateTimePickerAutomationPeer(this);
}
