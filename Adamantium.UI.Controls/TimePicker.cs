using System;
using Adamantium.UI.Controls.Automation;
using Adamantium.UI.Controls.Base;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Automation;
using Adamantium.UI.Core.RoutedEvents;

namespace Adamantium.UI.Controls;

/// <summary>A time entered part by part, or picked in what the button opens - a list of times a
/// <see cref="TimeInterval"/> apart, columns, or a clock face (<see cref="PopupStyle"/>). The date of
/// <see cref="DateTimeField.Value"/> is kept; with none it is today.</summary>
public class TimePicker : DateTimePickerBase
{
    public static readonly AdamantiumProperty TimeIntervalProperty = AdamantiumProperty.Register(nameof(TimeInterval),
        typeof(TimeSpan), typeof(TimePicker),
        new PropertyMetadata(TimeSpan.FromMinutes(15), (a, _) => ((TimePicker)a).RefreshDropDown()));

    public static readonly AdamantiumProperty PopupStyleProperty = AdamantiumProperty.Register(nameof(PopupStyle),
        typeof(TimePickerPopupStyle), typeof(TimePicker), new PropertyMetadata(TimePickerPopupStyle.List, OnPopupStyleChanged));

    private TimeChoice _choice;

    static TimePicker()
    {
        FormatProperty.OverrideMetadata(typeof(TimePicker),
            new PropertyMetadata(DateTimeFieldFormat.ShortTime, OnPatternChanged));
    }

    /// <summary>The step between the times the list offers; 15 minutes by default.</summary>
    public TimeSpan TimeInterval
    {
        get => GetValue<TimeSpan>(TimeIntervalProperty);
        set => SetValue(TimeIntervalProperty, value);
    }

    /// <summary>How the time is offered: a list (the default), columns or a clock face.</summary>
    public TimePickerPopupStyle PopupStyle
    {
        get => GetValue<TimePickerPopupStyle>(PopupStyleProperty);
        set => SetValue(PopupStyleProperty, value);
    }

    protected override MeasurableUIComponent CreateDropDownContent()
    {
        _choice = new TimeChoice(PopupStyle, time => Pick(Day() + time, close: true), CloseDropDown);
        return _choice.Content;
    }

    protected override void PrepareDropDownContent() =>
        _choice.Show(Value, TimeInterval, ShownCulture(), Format == DateTimeFieldFormat.LongTime);

    protected override IInputComponent DropDownKeyboardTarget() => _choice?.KeyboardTarget;

    protected override void OnDropDownShown() => _choice?.Reveal();

    protected override void OnDropDownClosed() => _choice?.LetGo();

    protected override AutomationPeer OnCreateAutomationPeer() => new DateTimePickerAutomationPeer(this);

    private static void OnPopupStyleChanged(AdamantiumComponent a, AdamantiumPropertyChangedEventArgs e) =>
        ((TimePicker)a).ForgetDropDownContent();

    private DateTime Day() => (Value ?? DateTime.SpecifyKind(DateTime.Today, DateTimeKind.Unspecified)).Date;
}
