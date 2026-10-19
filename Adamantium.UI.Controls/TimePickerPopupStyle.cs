namespace Adamantium.UI.Controls;

/// <summary>How a <see cref="TimePicker"/> offers the time it opens.</summary>
public enum TimePickerPopupStyle
{
    /// <summary>A list of times a step apart (<see cref="TimePicker.TimeInterval"/>), as Telerik's.</summary>
    List,

    /// <summary>Columns of hours, minutes and AM/PM that scroll round, as WinUI's.</summary>
    Columns,

    /// <summary>A clock face: the hours first, then the minutes, a hand dragged round - as Material's.</summary>
    Dial
}
