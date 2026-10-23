using System;
using System.Globalization;
using Adamantium.UI.Core.Data;

namespace Adamantium.UI.Sandbox.Converters;

/// <summary>Shows a time span as a stopwatch does: minutes, seconds and tenths.</summary>
public class ElapsedConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is TimeSpan span ? $"{(int)span.TotalMinutes}:{span.Seconds:00}.{span.Milliseconds / 100}" : string.Empty;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
