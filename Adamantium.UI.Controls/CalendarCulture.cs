using System;
using System.Globalization;
using System.Linq;

namespace Adamantium.UI.Controls;

internal static class CalendarCulture
{
    public static CultureInfo Of(CultureInfo culture, CalendarSystem system)
    {
        if (system == CalendarSystem.Language)
        {
            return culture;
        }

        var wanted = system switch
        {
            CalendarSystem.Hijri => typeof(HijriCalendar),
            CalendarSystem.UmAlQura => typeof(UmAlQuraCalendar),
            CalendarSystem.Persian => typeof(PersianCalendar),
            CalendarSystem.Hebrew => typeof(HebrewCalendar),
            CalendarSystem.Japanese => typeof(JapaneseCalendar),
            CalendarSystem.Korean => typeof(KoreanCalendar),
            CalendarSystem.Taiwan => typeof(TaiwanCalendar),
            CalendarSystem.ThaiBuddhist => typeof(ThaiBuddhistCalendar),
            _ => typeof(GregorianCalendar),
        };
        if (culture.Calendar.GetType() == wanted)
        {
            return culture;
        }

        foreach (var named in (string[])[culture.Name, HomeOf(system)])
        {
            var candidate = string.IsNullOrEmpty(named) ? CultureInfo.InvariantCulture : CultureInfo.GetCultureInfo(named);
            if (candidate.OptionalCalendars.FirstOrDefault(c => c.GetType() == wanted) is { } counted)
            {
                var clone = (CultureInfo)candidate.Clone();
                var format = clone.DateTimeFormat;
                format.Calendar = counted;
                if (!ReferenceEquals(candidate, culture))
                {
                    var own = culture.DateTimeFormat;
                    format.DayNames = own.DayNames;
                    format.AbbreviatedDayNames = own.AbbreviatedDayNames;
                    format.ShortestDayNames = own.ShortestDayNames;
                    format.FirstDayOfWeek = own.FirstDayOfWeek;
                    format.CalendarWeekRule = own.CalendarWeekRule;
                }

                return CultureInfo.ReadOnly(clone);
            }
        }

        return CultureInfo.InvariantCulture;
    }

    public static string WithEra(string pattern, DateTimeFormatInfo format)
    {
        if (format.Calendar.Eras.Length < 2 || !Names(pattern, 'y') || Names(pattern, 'g'))
        {
            return pattern;
        }

        var shown = new System.Text.StringBuilder("gg");
        for (var i = 0; i < pattern.Length; i++)
        {
            var literal = Literal(pattern, i);
            if (literal > i)
            {
                shown.Append(pattern, i, literal - i);
                i = literal - 1;
            }
            else if (string.CompareOrdinal(pattern, i, "yyyy", 0, 4) == 0)
            {
                shown.Append('y');
                i += 3;
            }
            else
            {
                shown.Append(pattern[i]);
            }
        }

        return shown.ToString();
    }

    public static bool Names(string pattern, char letter)
    {
        for (var i = 0; i < pattern.Length; i++)
        {
            var literal = Literal(pattern, i);
            if (literal > i)
            {
                i = literal - 1;
            }
            else if (pattern[i] == letter)
            {
                return true;
            }
        }

        return false;
    }

    private static int Literal(string pattern, int at)
    {
        var c = pattern[at];
        if (c is '\'' or '"')
        {
            var close = pattern.IndexOf(c, at + 1);
            return close < 0 ? pattern.Length : close + 1;
        }

        return c == '\\' ? Math.Min(at + 2, pattern.Length) : at;
    }

    private static string HomeOf(CalendarSystem system) => system switch
    {
        CalendarSystem.Hijri or CalendarSystem.UmAlQura => "ar-SA",
        CalendarSystem.Persian => "fa-IR",
        CalendarSystem.Hebrew => "he-IL",
        CalendarSystem.Japanese => "ja-JP",
        CalendarSystem.Korean => "ko-KR",
        CalendarSystem.Taiwan => "zh-TW",
        CalendarSystem.ThaiBuddhist => "th-TH",
        _ => string.Empty,
    };
}
