using System;
using System.Globalization;
using Adamantium.UI.Core.Data;

namespace Adamantium.UI.Controls.Converters;

/// <summary>A <see cref="TimeSpan"/> to its seconds and back, so a slider, whose value is a number, can be bound two-way
/// to a time: a position in a sound or a video.</summary>
public class TimeSpanToSecondsConverter : ValueConverter
{
    public override object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is TimeSpan time ? time.TotalSeconds : 0.0;

    public override object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is double seconds && double.IsFinite(seconds) ? TimeSpan.FromSeconds(seconds) : TimeSpan.Zero;
}
