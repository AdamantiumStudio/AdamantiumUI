namespace Adamantium.UI.Controls;

/// <summary>How the days of a <see cref="Calendar"/> are selected, as WPF's.</summary>
public enum CalendarSelectionMode
{
    /// <summary>One day.</summary>
    SingleDate,

    /// <summary>One run of days: Shift and a click, or a drag, from the day selected first.</summary>
    SingleRange,

    /// <summary>Any days and runs of days: Ctrl and a click adds or takes away one, Shift adds a run.</summary>
    MultipleRange,

    /// <summary>None: the calendar is only looked at.</summary>
    None,
}
