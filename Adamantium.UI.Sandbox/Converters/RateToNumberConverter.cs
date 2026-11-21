using System;
using System.Globalization;
using Adamantium.UI.Core.Data;

namespace Adamantium.UI.Sandbox.Converters;

/// <summary>A frame rate in a number box: an empty box is a rate not set.</summary>
public class RateToNumberConverter : ValueConverter
{
    public override object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is uint rate ? (double?)rate : null;

    public override object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is double number ? (uint?)Math.Max(0, Math.Round(number)) : null;
}
