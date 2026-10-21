using System;
using System.Collections.Concurrent;
using SystemCalendar = System.Globalization.Calendar;

namespace Adamantium.UI.Controls;

internal static class CalendarRange
{
    private static readonly ConcurrentDictionary<Type, DateTime> FirstDays = new();

    /// <summary>The first day a calendar counts: later than its MinSupportedDateTime where its first era begins later,
    /// as ICU's Meiji does (1868-10-23, while .NET supports the Japanese calendar from 1868-09-08).</summary>
    public static DateTime FirstDay(this SystemCalendar calendar) =>
        FirstDays.GetOrAdd(calendar.GetType(), static (_, counting) => FindFirstDay(counting), calendar);

    private static DateTime FindFirstDay(SystemCalendar calendar)
    {
        var counted = calendar.MinSupportedDateTime.Date;
        if (Counts(calendar, counted))
        {
            return counted;
        }

        var before = counted;
        var after = counted.AddYears(1);
        if (!Counts(calendar, after))
        {
            return counted;
        }

        while ((after - before).Days > 1)
        {
            var middle = before.AddDays((after - before).Days / 2);
            if (Counts(calendar, middle))
            {
                after = middle;
            }
            else
            {
                before = middle;
            }
        }

        return after;
    }

    private static bool Counts(SystemCalendar calendar, DateTime day)
    {
        try
        {
            calendar.GetEra(day);
            calendar.GetYear(day);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
