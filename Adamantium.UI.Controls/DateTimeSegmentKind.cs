namespace Adamantium.UI.Controls;

/// <summary>What a <see cref="DateTimeFieldSegment"/> holds.</summary>
public enum DateTimeSegmentKind
{
    Day,
    Month,
    Year,
    Hour,
    Minute,
    Second,

    /// <summary>AM or PM, in a 12-hour clock.</summary>
    Period,

    /// <summary>The era of a calendar that counts by eras.</summary>
    Era,

    /// <summary>The day of the week: shown with the date, not entered.</summary>
    DayOfWeek
}
