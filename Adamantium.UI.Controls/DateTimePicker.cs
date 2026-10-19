using System;
using Adamantium.UI.Controls.Automation;
using Adamantium.UI.Controls.Base;
using Adamantium.UI.Controls.Panels;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Automation;
using Adamantium.UI.Core.RoutedEvents;

namespace Adamantium.UI.Controls;

/// <summary>A date and a time entered part by part, or picked in what the button opens: a calendar and the times beside
/// it. A day picked keeps the time; a time picked keeps the day and closes the choice.</summary>
public class DateTimePicker : DateTimePickerBase
{
    public static readonly AdamantiumProperty TimeIntervalProperty = AdamantiumProperty.Register(nameof(TimeInterval),
        typeof(TimeSpan), typeof(DateTimePicker),
        new PropertyMetadata(TimeSpan.FromMinutes(15), (a, _) => ((DateTimePicker)a).RefreshDropDown()));

    public static readonly AdamantiumProperty PopupStyleProperty = AdamantiumProperty.Register(nameof(PopupStyle),
        typeof(TimePickerPopupStyle), typeof(DateTimePicker),
        new PropertyMetadata(TimePickerPopupStyle.List, OnPopupStyleChanged));

    public static readonly AdamantiumProperty DisplayDateStartProperty = AdamantiumProperty.Register(
        nameof(DisplayDateStart), typeof(DateTime?), typeof(DateTimePicker),
        new PropertyMetadata(null, (a, _) => ((DateTimePicker)a).RefreshDropDown()));

    public static readonly AdamantiumProperty DisplayDateEndProperty = AdamantiumProperty.Register(nameof(DisplayDateEnd),
        typeof(DateTime?), typeof(DateTimePicker), new PropertyMetadata(null, (a, _) => ((DateTimePicker)a).RefreshDropDown()));

    private CalendarChoice _days;
    private TimeChoice _times;

    static DateTimePicker()
    {
        FormatProperty.OverrideMetadata(typeof(DateTimePicker),
            new PropertyMetadata(DateTimeFieldFormat.ShortDateTime, OnPatternChanged));
    }

    /// <summary>The step between the times offered beside the calendar; 15 minutes by default.</summary>
    public TimeSpan TimeInterval
    {
        get => GetValue<TimeSpan>(TimeIntervalProperty);
        set => SetValue(TimeIntervalProperty, value);
    }

    /// <summary>How the time is offered beside the calendar: a list (the default), columns or a clock face.</summary>
    public TimePickerPopupStyle PopupStyle
    {
        get => GetValue<TimePickerPopupStyle>(PopupStyleProperty);
        set => SetValue(PopupStyleProperty, value);
    }

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
    public Calendar DropDownCalendar => _days?.Calendar;

    protected override MeasurableUIComponent CreateDropDownContent()
    {
        _days = new CalendarChoice(day => Pick(day + (Value?.TimeOfDay ?? TimeSpan.Zero), close: false), () => { });
        _times = new TimeChoice(PopupStyle, time => Pick(Day() + time, close: true), CloseDropDown, beside: true);
        var both = new StackPanel { Orientation = Orientation.Horizontal };
        both.Children.Add(_days.Calendar);
        both.Children.Add(_times.Content);
        return both;
    }

    protected override void PrepareDropDownContent()
    {
        _days.Show(Value, Language, CalendarSystem, DisplayDateStart, DisplayDateEnd);
        _times.Show(Value, TimeInterval, ShownCulture(), Format == DateTimeFieldFormat.LongTime);
    }

    protected override IInputComponent DropDownKeyboardTarget() => _days?.Calendar;

    protected override void OnDropDownShown() => _times?.Reveal();

    protected override void OnDropDownClosed() => _times?.LetGo();

    protected override AutomationPeer OnCreateAutomationPeer() => new DateTimePickerAutomationPeer(this);

    private static void OnPopupStyleChanged(AdamantiumComponent a, AdamantiumPropertyChangedEventArgs e) =>
        ((DateTimePicker)a).ForgetDropDownContent();

    private DateTime Day() => (Value ?? DateTime.SpecifyKind(DateTime.Today, DateTimeKind.Unspecified)).Date;
}
