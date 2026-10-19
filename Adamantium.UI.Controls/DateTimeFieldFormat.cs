namespace Adamantium.UI.Controls;

/// <summary>Which parts of a date and time a <see cref="DateTimeField"/> shows, in the order and form of its language.</summary>
public enum DateTimeFieldFormat
{
    /// <summary>The short date: 19.10.2026, 10/19/2026.</summary>
    ShortDate,

    /// <summary>The long date: 19 октября 2026 г., Monday, October 19, 2026.</summary>
    LongDate,

    /// <summary>Hours and minutes: 14:05, 2:05 PM.</summary>
    ShortTime,

    /// <summary>Hours, minutes and seconds.</summary>
    LongTime,

    /// <summary>The short date and the short time.</summary>
    ShortDateTime,

    /// <summary>The pattern in <see cref="DateTimeField.CustomFormat"/>.</summary>
    Custom
}
