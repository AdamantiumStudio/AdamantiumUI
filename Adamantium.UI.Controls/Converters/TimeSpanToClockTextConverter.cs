using System;
using System.Globalization;
using Adamantium.UI.Core.Data;

namespace Adamantium.UI.Controls.Converters;

/// <summary>A <see cref="TimeSpan"/> as a player shows it: "01:04" below an hour, "1:02:05" from one. The parameter
/// "Remaining" shows it as what is left: "−01:27".</summary>
public class TimeSpanToClockTextConverter : ValueConverter
{
    public override object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var time = value is TimeSpan span ? span.Duration() : TimeSpan.Zero;
        // A part of a second is not shown, and does not count: 0.9 s is still 00:00.
        var text = time.TotalHours >= 1
            ? string.Create(CultureInfo.InvariantCulture, $"{(int)time.TotalHours}:{time.Minutes:00}:{time.Seconds:00}")
            : string.Create(CultureInfo.InvariantCulture, $"{time.Minutes:00}:{time.Seconds:00}");
        return parameter as string == "Remaining" ? "−" + text : text;
    }

    public override object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException("A clock's text is shown, not read back.");
}
