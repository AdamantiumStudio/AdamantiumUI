using System;
using System.Globalization;
using System.Linq;
using Adamantium.UI.Controls.Panels;
using Adamantium.UI.Controls.Text;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Data;
using Adamantium.UI.Core.Localization;
using Adamantium.UI.Core.Resources;
using Adamantium.UI.Core.Templates;

namespace Adamantium.UI.Controls;

internal static class DateTimeValues
{
    public static bool IsDate(Type type) =>
        (Nullable.GetUnderlyingType(type) ?? type) is var t &&
        (t == typeof(DateTime) || t == typeof(DateTimeOffset) || t == typeof(DateOnly) || t == typeof(TimeOnly));

    public static DateTimeFieldFormat FormatFor(Type type) =>
        (Nullable.GetUnderlyingType(type) ?? type) is var t && t == typeof(DateOnly) ? DateTimeFieldFormat.ShortDate
        : t == typeof(TimeOnly) ? DateTimeFieldFormat.ShortTime
        : DateTimeFieldFormat.ShortDateTime;

    public static DateTime? ToDateTime(object value) => value switch
    {
        DateTime date => date,
        DateTimeOffset offset => offset.DateTime,
        DateOnly day => day.ToDateTime(TimeOnly.MinValue),
        TimeOnly time => DateTime.SpecifyKind(DateTime.Today, DateTimeKind.Unspecified) + time.ToTimeSpan(),
        _ => null,
    };

    public static object FromDateTime(DateTime edited, Type target, object original)
    {
        var type = Nullable.GetUnderlyingType(target) ?? target;
        var frame = original switch
        {
            DateTimeOffset => DateTimeKind.Unspecified,
            DateTime held => held.Kind,
            _ => edited.Kind,
        };
        if (edited.Kind != DateTimeKind.Unspecified && (edited.Kind != frame || original is DateTimeOffset))
        {
            return FromMoment(new DateTimeOffset(edited), target, original);
        }

        if (type == typeof(DateTimeOffset))
        {
            var offset = original is DateTimeOffset kept && kept != default ? kept.Offset : TimeZoneInfo.Local.GetUtcOffset(edited);
            var clock = DateTime.SpecifyKind(edited, DateTimeKind.Unspecified);
            var utc = clock.Ticks - offset.Ticks;
            return utc >= DateTime.MinValue.Ticks && utc <= DateTime.MaxValue.Ticks
                ? new DateTimeOffset(clock, offset)
                : new DateTimeOffset(clock, TimeSpan.Zero);
        }

        if (type == typeof(DateOnly))
        {
            return DateOnly.FromDateTime(edited);
        }

        if (type == typeof(TimeOnly))
        {
            return TimeOnly.FromDateTime(edited);
        }

        return original is DateTime was ? DateTime.SpecifyKind(edited, was.Kind) : edited;
    }

    public static DateTimeOffset? AsMoment(object value)
    {
        try
        {
            return value switch
            {
                DateTimeOffset moment => moment,
                DateTime { Kind: not DateTimeKind.Unspecified } clock => new DateTimeOffset(clock),
                _ => null,
            };
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    public static object InFrameOf(DateTime clock, object shown)
    {
        try
        {
            return shown switch
            {
                DateTimeOffset moment when moment != default => new DateTimeOffset(clock, moment.Offset),
                DateTime { Kind: not DateTimeKind.Unspecified } held => (object)DateTime.SpecifyKind(clock, held.Kind),
                _ => clock,
            };
        }
        catch (ArgumentOutOfRangeException)
        {
            return clock;
        }
    }

    public static DateTimePickerBase PickerFor(DateTimeFieldFormat format, string custom)
    {
        var pattern = DateTimeField.PatternOf(format, custom, Languages.Culture.DateTimeFormat);
        var dated = CalendarCulture.Names(pattern, 'd') || CalendarCulture.Names(pattern, 'M') ||
                    CalendarCulture.Names(pattern, 'y');
        var timed = CalendarCulture.Names(pattern, 'h') || CalendarCulture.Names(pattern, 'H') ||
                    CalendarCulture.Names(pattern, 'm') || CalendarCulture.Names(pattern, 's');
        DateTimePickerBase picker = dated && timed ? new DateTimePicker() : timed ? new TimePicker() : new DatePicker();
        picker.Format = format;
        picker.CustomFormat = custom;
        return picker;
    }

    private static string Shown(DateTimeFieldFormat format, string custom, DateTimeFormatInfo info)
    {
        var pattern = DateTimeField.PatternOf(format, custom, info);
        return CalendarCulture.Names(pattern, 'y') ? CalendarCulture.WithEra(pattern, info) : pattern;
    }

    public static string Text(object value, DateTimeFieldFormat format, string custom, CultureInfo culture)
    {
        var pattern = Shown(format, custom, culture.DateTimeFormat);
        try
        {
            return value is DateTimeOffset moment ? moment.ToString(pattern, culture)
                : ToDateTime(value) is { } date ? date.ToString(pattern, culture)
                : value?.ToString() ?? string.Empty;
        }
        catch (FormatException)
        {
            return value is IFormattable formattable ? formattable.ToString(null, culture) : value?.ToString() ?? string.Empty;
        }
        catch (ArgumentOutOfRangeException)
        {
            return value is IFormattable formattable
                ? formattable.ToString(null, CultureInfo.InvariantCulture)
                : value?.ToString() ?? string.Empty;
        }
    }

    private readonly record struct Shows(bool Era, bool Year, bool ShortYear, bool Month, bool Day, bool Weekday, bool Hour,
        bool Twelve, bool Half, bool Minute, bool Second, int Fraction, bool Zone)
    {
        public long Unit => Fraction >= 7 ? 1 : (long)Math.Pow(10, 7 - Fraction);

        public static Shows Of(string pattern)
        {
            bool Names(char letter) => CalendarCulture.Names(pattern, letter);
            var days = CalendarCulture.Runs(pattern, 'd');
            var fraction = Math.Max(CalendarCulture.Runs(pattern, 'f').Longest, CalendarCulture.Runs(pattern, 'F').Longest);
            return new Shows(Names('g'), Names('y'), Names('y') && CalendarCulture.Runs(pattern, 'y').Shortest <= 2,
                Names('M'), days.Shortest is > 0 and <= 2, days.Longest >= 3, Names('h') || Names('H'),
                Names('h') && !Names('H') && !Names('t'), Names('t'), Names('m'), Names('s'), Math.Min(fraction, 7),
                Names('z') || Names('K'));
        }

        public int HourOf(DateTime clock) => Twelve ? clock.Hour % 12 : clock.Hour;

        public long FractionOf(DateTime clock) =>
            Fraction == 0 ? 0 : clock.Ticks % TimeSpan.TicksPerSecond / Unit * Unit;
    }

    private static readonly string[] IsoForms =
    [
        "yyyy-MM-dd", "yyyy-MM-ddTHH:mm", "yyyy-MM-ddTHH:mm:ss", "yyyy-MM-ddTHH:mm:ss.FFFFFFF", "HH:mm", "HH:mm:ss",
        "HH:mm:ss.FFFFFFF",
    ];

    private static readonly string[] IsoMoments =
        ["yyyy-MM-ddTHH:mmK", "yyyy-MM-ddTHH:mm:ssK", "yyyy-MM-ddTHH:mm:ss.FFFFFFFK"];

    public static bool TryParseMoment(string text, DateTimeFieldFormat format, string custom, CultureInfo culture,
        object current, out object value)
    {
        value = null;
        text = text?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        var info = culture.DateTimeFormat;
        var pattern = Shown(format, custom, info);
        var calendar = info.Calendar;
        var zoned = CalendarCulture.Names(pattern, 'z') || CalendarCulture.Names(pattern, 'K');
        if (zoned && Try(() => (DateTimeOffset.TryParseExact(text, pattern, culture, DateTimeStyles.AllowWhiteSpaces,
                out var own), own)) is { } ownMoment)
        {
            value = Try(() => (true, new DateTimeOffset(Keep(ownMoment.DateTime, HeldIn(current, ownMoment.Offset), pattern,
                calendar), ownMoment.Offset))) ?? ownMoment;
            return true;
        }

        if (!zoned && Read(text, pattern, culture, current) is { } ownClock)
        {
            value = Keep(ownClock, current, pattern, calendar);
            return true;
        }

        foreach (var form in IsoForms)
        {
            if (DateTime.TryParseExact(text, form, CultureInfo.InvariantCulture, DateTimeStyles.NoCurrentDateDefault, out var clock))
            {
                value = Keep(clock, current, form, CultureInfo.InvariantCulture.Calendar);
                return true;
            }
        }

        if (DateTimeOffset.TryParseExact(text, IsoMoments, CultureInfo.InvariantCulture, DateTimeStyles.None, out var iso))
        {
            value = iso;
            return true;
        }

        foreach (var known in KnownPatterns(info))
        {
            if (Read(text, known, culture, current) is { } read)
            {
                value = Keep(read, current, known, calendar);
                return true;
            }
        }

        return false;
    }

    private static DateTime? Read(string text, string pattern, CultureInfo culture, object current)
    {
        DateTime? In(string year) =>
            Try(() => (DateTime.TryParseExact(text + " " + year, pattern + " yyyy", culture, DateTimeStyles.AllowWhiteSpaces,
                out var read), read));

        var calendar = culture.DateTimeFormat.Calendar;
        if (CalendarCulture.Names(pattern, 'y') || calendar.Eras.Length > 1)
        {
            return Try(() => (DateTime.TryParseExact(text, pattern, culture, DateTimeStyles.AllowWhiteSpaces, out var read), read));
        }

        var held = ToDateTime(current) ?? DateTime.Today;
        var year = Try(() => (true, calendar.GetYear(held))) ?? Try(() => (true, calendar.GetYear(DateTime.Today)));
        var read = year is { } y ? In(y.ToString("D4", CultureInfo.InvariantCulture)) : null;
        if (read != null || CalendarCulture.Runs(pattern, 'd').Longest >= 3 || year is not { } heldYear)
        {
            return read;
        }

        for (var leap = heldYear; leap > heldYear - 8; leap--)
        {
            if (Try(() => (true, calendar.IsLeapYear(leap))) == true)
            {
                return In(leap.ToString("D4", CultureInfo.InvariantCulture));
            }
        }

        return null;
    }

    private static readonly char[] KnownForms = ['d', 'D', 'f', 'F', 'g', 'G', 'm', 't', 'T', 'y'];

    private static readonly System.Text.RegularExpressions.Regex YearPart = new("[^dM]*y+[^dM]*");

    private static readonly System.Text.RegularExpressions.Regex YearRun = new("y+");

    private static System.Collections.Generic.IEnumerable<string> KnownPatterns(DateTimeFormatInfo info)
    {
        foreach (var form in KnownForms)
        {
            foreach (var known in info.GetAllDateTimePatterns(form))
            {
                yield return known;
                if (known.Contains("MMMM", StringComparison.Ordinal))
                {
                    yield return known.Replace("MMMM", "MMM", StringComparison.Ordinal);
                }

                if (known.Contains("yyyy", StringComparison.Ordinal))
                {
                    yield return known.Replace("yyyy", "yy", StringComparison.Ordinal);
                }
            }
        }

        foreach (var dated in (string[])[info.ShortDatePattern, info.LongDatePattern])
        {
            if (!CalendarCulture.Names(dated, 'y'))
            {
                continue;
            }

            foreach (var yearless in (string[])[YearPart.Replace(dated, string.Empty), YearRun.Replace(dated, string.Empty)])
            {
                if (yearless.Length > 0 && yearless.Count(c => c == '\'') % 2 == 0)
                {
                    yield return yearless;
                }
            }
        }
    }

    private static T? Try<T>(Func<(bool Parsed, T Value)> parse) where T : struct
    {
        try
        {
            var (parsed, result) = parse();
            return parsed ? result : null;
        }
        catch (Exception e) when (e is FormatException or ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    private static object HeldIn(object current, TimeSpan offset) => current switch
    {
        DateTimeOffset kept => kept == default ? null : kept.ToOffset(offset).DateTime,
        DateTime { Kind: not DateTimeKind.Unspecified } clock => new DateTimeOffset(clock).ToOffset(offset).DateTime,
        _ => current,
    };

    private static DateTime Keep(DateTime parsed, object current, string pattern, System.Globalization.Calendar calendar) =>
        Keep(parsed, current, Shows.Of(pattern), calendar);

    private static DateTime Keep(DateTime parsed, object current, Shows shows, System.Globalization.Calendar calendar)
    {
        var real = ToDateTime(current);
        var held = real ?? DateTime.SpecifyKind(DateTime.Today, DateTimeKind.Unspecified);
        try
        {
            var era = shows.Era ? calendar.GetEra(parsed) : calendar.GetEra(held);
            var year = calendar.GetYear(held);
            if (shows.Year)
            {
                var given = calendar.GetYear(parsed);
                year = real != null && shows.ShortYear && calendar.Eras.Length < 2 ? year - year % 100 + given % 100 : given;
            }

            var month = shows.Month ? calendar.GetMonth(parsed) : calendar.GetMonth(held);
            var day = shows.Day ? calendar.GetDayOfMonth(parsed) : calendar.GetDayOfMonth(held);
            var hour = !shows.Hour ? held.Hour
                : shows.Twelve ? parsed.Hour % 12 + (held.Hour >= 12 ? 12 : 0)
                : parsed.Hour;
            var minute = shows.Minute ? parsed.Minute : held.Minute;
            var second = shows.Second ? parsed.Second : held.Second;
            var below = held.Ticks % TimeSpan.TicksPerSecond;
            if (shows.Fraction > 0)
            {
                below = shows.FractionOf(parsed) + below % shows.Unit;
            }

            month = Math.Min(month, calendar.GetMonthsInYear(year, era));
            day = Math.Min(day, calendar.GetDaysInMonth(year, month, era));
            var kept = calendar.ToDateTime(year, month, day, hour, minute, second, 0, era).AddTicks(below);
            return DateTime.SpecifyKind(kept, DateTimeKind.Unspecified);
        }
        catch (ArgumentOutOfRangeException)
        {
            return DateTime.SpecifyKind(parsed, DateTimeKind.Unspecified);
        }
    }

    public static bool ShowsNoMore(DateTimeFieldFormat format, string custom, DateTimeFieldFormat than, string thanCustom)
    {
        var info = Languages.Culture.DateTimeFormat;
        var now = Shows.Of(Shown(format, custom, info));
        var was = Shows.Of(Shown(than, thanCustom, info));
        return (!now.Year || was.Year && (now.ShortYear || !was.ShortYear)) && (!now.Month || was.Month) &&
               (!now.Day || was.Day) && (!now.Weekday || was.Weekday || was.Day && was.Month && was.Year) &&
               (!now.Hour || was.Hour && (now.Twelve || !was.Twelve)) && (!now.Minute || was.Minute) &&
               (!now.Second || was.Second) && now.Fraction <= was.Fraction && (!now.Zone || was.Zone);
    }

    public static object Cut(object value, DateTimeFieldFormat format, string custom)
    {
        if (ToDateTime(value) is not { } clock)
        {
            return null;
        }

        var info = Languages.Culture.DateTimeFormat;
        var pattern = Shown(format, custom, info);
        var calendar = info.Calendar;
        var shows = Shows.Of(pattern);
        if (shows.Zone && value is DateTimeOffset moment)
        {
            clock = moment.UtcDateTime;
        }

        var time = (shows.Hour ? shows.HourOf(clock) * TimeSpan.TicksPerHour : 0) +
                   (shows.Minute ? clock.Minute * TimeSpan.TicksPerMinute : 0) +
                   (shows.Second ? clock.Second * TimeSpan.TicksPerSecond : 0) +
                   shows.FractionOf(clock);
        try
        {
            return (shows.Year ? calendar.GetEra(clock) : 0, shows.Year ? calendar.GetYear(clock) : 0,
                shows.Month ? calendar.GetMonth(clock) : 0, shows.Day ? calendar.GetDayOfMonth(clock) : 0,
                shows.Weekday && !shows.Day ? (int)clock.DayOfWeek : 0, time);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    public static string KeyOf(object value, DateTimeFieldFormat format, string custom)
    {
        if (Cut(value, format, custom) is not { } parts)
        {
            return value?.ToString() ?? string.Empty;
        }

        var key = Convert.ToString(parts, CultureInfo.InvariantCulture);
        var shows = Shows.Of(Shown(format, custom, Languages.Culture.DateTimeFormat));
        return !shows.Zone ? key
            : value is DateTimeOffset moment ? key + moment.Offset.ToString("c", CultureInfo.InvariantCulture)
            : value is DateTime clock ? key + clock.Kind
            : key;
    }

    public static object FromMoment(DateTimeOffset moment, Type target, object original)
    {
        var type = Nullable.GetUnderlyingType(target) ?? target;
        if (type == typeof(DateTimeOffset))
        {
            return original is DateTimeOffset kept && kept != default ? moment.ToOffset(kept.Offset) : moment;
        }

        var clock = original switch
        {
            DateTime { Kind: DateTimeKind.Utc } => moment.UtcDateTime,
            DateTime { Kind: DateTimeKind.Local } => moment.LocalDateTime,
            _ => DateTime.SpecifyKind(moment.DateTime, DateTimeKind.Unspecified),
        };
        return type == typeof(DateOnly) ? DateOnly.FromDateTime(clock)
            : type == typeof(TimeOnly) ? TimeOnly.FromDateTime(clock)
            : clock;
    }

    public static bool SameInstant(object held, DateTimeOffset moment) => held switch
    {
        DateTimeOffset offset => offset == moment,
        DateTime { Kind: DateTimeKind.Utc } utc => utc == moment.UtcDateTime,
        DateTime { Kind: DateTimeKind.Local } local => local == moment.LocalDateTime,
        _ => ToDateTime(held) is { } clock && clock == moment.DateTime,
    };

    public static string DurationLine(TimeSpan length, DurationParts parts, string dayUnit, CultureInfo culture)
    {
        var (days, time) = DurationText(length, parts, culture);
        var line = days.Length == 0 ? time
            : string.IsNullOrEmpty(dayUnit) ? days + " " + time
            : days + " " + dayUnit + " " + time;
        return time.Length == 0 ? line.TrimEnd() : line;
    }

    public static bool TryParseDuration(string text, DurationParts parts, CultureInfo culture, out TimeSpan length) =>
        TryParseDuration(text, parts, culture, null, out length);

    public static bool TryParseDuration(string text, DurationParts parts, CultureInfo culture, TimeSpan? basis,
        out TimeSpan length)
    {
        length = default;
        text = text?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        if (parts == DurationParts.None)
        {
            parts = DurationParts.Hours | DurationParts.Minutes | DurationParts.Seconds;
        }

        var negative = text[0] is '-' or '−';
        var numbers = System.Text.RegularExpressions.Regex.Matches(text, "[0-9]+");
        var shown = Units.Where(u => (parts & u.Part) != 0).ToArray();
        if (numbers.Count == shown.Length && numbers.All(n => n.Value.Length <= 19) && Fits(text, numbers, shown, culture))
        {
            decimal ticks = 0;
            for (var i = 0; i < shown.Length; i++)
            {
                var count = decimal.Parse(numbers[i].Value, CultureInfo.InvariantCulture);
                if (count > TimeSpan.MaxValue.Ticks / shown[i].Unit)
                {
                    return false;
                }

                ticks += count * shown[i].Unit;
            }

            if (basis is { } kept)
            {
                var held = kept.Ticks == long.MinValue ? long.MaxValue : Math.Abs(kept.Ticks);
                decimal rest = held % shown[0].Unit;
                foreach (var part in shown.Skip(1))
                {
                    rest -= held / part.Unit % part.Above * part.Unit;
                }

                ticks += rest;
            }

            if (ticks <= TimeSpan.MaxValue.Ticks)
            {
                length = new TimeSpan((long)(negative ? -ticks : ticks));
                return true;
            }
        }

        return TimeSpan.TryParseExact(text, "c", CultureInfo.InvariantCulture, out length)
               || TimeSpan.TryParse(text, culture, out length)
               || TimeSpan.TryParse(text, CultureInfo.InvariantCulture, out length);
    }

    private static bool Fits(string text, System.Text.RegularExpressions.MatchCollection numbers,
        (DurationParts Part, long Unit, long Above, int Width)[] shown, CultureInfo culture)
    {
        for (var i = 1; i < shown.Length; i++)
        {
            if (numbers[i].Value.Length != shown[i].Width ||
                int.Parse(numbers[i].Value, CultureInfo.InvariantCulture) >= shown[i].Above)
            {
                return false;
            }

            var between = text.Substring(numbers[i - 1].Index + numbers[i - 1].Length,
                numbers[i].Index - numbers[i - 1].Index - numbers[i - 1].Length);
            var fits = shown[i - 1].Part == DurationParts.Days
                ? between.Length > 0 && char.IsWhiteSpace(between[0]) && char.IsWhiteSpace(between[^1])
                : between == DurationField.Separator(shown[i - 1].Part, shown[i].Part, culture);
            if (!fits)
            {
                return false;
            }
        }

        return true;
    }

    public static TimeSpan DurationCut(TimeSpan value, DurationParts parts)
    {
        if (parts == DurationParts.None)
        {
            parts = DurationParts.Hours | DurationParts.Minutes | DurationParts.Seconds;
        }

        var magnitude = value.Ticks == long.MinValue ? long.MaxValue : Math.Abs(value.Ticks);
        var kept = 0L;
        var top = true;
        foreach (var (part, unit, above, _) in Units)
        {
            if ((parts & part) == 0)
            {
                continue;
            }

            kept += (top ? magnitude / unit : magnitude / unit % above) * unit;
            top = false;
        }

        return TimeSpan.FromTicks(value.Ticks < 0 ? -kept : kept);
    }

    public static (string Days, string Time) DurationText(TimeSpan value, DurationParts parts, CultureInfo culture)
    {
        if (parts == DurationParts.None)
        {
            parts = DurationParts.Hours | DurationParts.Minutes | DurationParts.Seconds;
        }

        var sign = value.Ticks < 0 ? "−" : string.Empty;
        var magnitude = value.Ticks == long.MinValue ? long.MaxValue : Math.Abs(value.Ticks);
        var days = string.Empty;
        var rest = new System.Text.StringBuilder();
        var top = true;
        var before = DurationParts.None;
        foreach (var (part, unit, above, width) in Units)
        {
            if ((parts & part) == 0)
            {
                continue;
            }

            var count = top ? magnitude / unit : magnitude / unit % above;
            var digits = count.ToString(new string('0', width), CultureInfo.InvariantCulture);
            if (part == DurationParts.Days)
            {
                days = digits;
            }
            else
            {
                if (rest.Length > 0)
                {
                    rest.Append(DurationField.Separator(before, part, culture));
                }

                rest.Append(digits);
            }

            top = false;
            before = part;
        }

        return days.Length > 0 ? (sign + days, rest.ToString()) : (string.Empty, sign + rest);
    }

    public static DataTemplate DurationTemplate(Func<DurationParts> parts) => new(() =>
    {
        var line = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var days = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
        var unit = new TextBlock { Classes = Classes.Parse("DurationFieldDayUnit") };
        var rest = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
        line.Children.Add(days);
        line.Children.Add(unit);
        line.Children.Add(rest);
        var result = new TemplateResult { RootComponent = line };
        var converter = new DurationConverter(parts);
        result.AddBinding(days, "Text", new Binding { Mode = BindingMode.OneWay, Converter = converter, ConverterParameter = 0 });
        result.AddBinding(unit, "Visibility", new Binding { Mode = BindingMode.OneWay, Converter = converter, ConverterParameter = 1 });
        result.AddBinding(rest, "Text", new Binding { Mode = BindingMode.OneWay, Converter = converter, ConverterParameter = 2 });
        return result;
    });

    private static readonly (DurationParts Part, long Unit, long Above, int Width)[] Units =
    [
        (DurationParts.Days, TimeSpan.TicksPerDay, 1, 1),
        (DurationParts.Hours, TimeSpan.TicksPerHour, 24, 2),
        (DurationParts.Minutes, TimeSpan.TicksPerMinute, 60, 2),
        (DurationParts.Seconds, TimeSpan.TicksPerSecond, 60, 2),
        (DurationParts.Milliseconds, TimeSpan.TicksPerMillisecond, 1000, 3),
    ];

    private sealed class DurationConverter(Func<DurationParts> parts) : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var (days, rest) = value is TimeSpan length ? DurationText(length, parts(), culture) : (string.Empty, value?.ToString() ?? string.Empty);
            return parameter switch
            {
                0 => days,
                1 => days.Length > 0 ? Visibility.Visible : Visibility.Collapsed,
                _ => rest,
            };
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }
}
