using System;
using System.Globalization;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Data;

namespace Adamantium.UI.Controls.Converters;

/// <summary>False to <see cref="Visibility.Visible"/>, true to <see cref="Visibility.Collapsed"/>, and back: what shows
/// while a switch is off, bound two-way.</summary>
public class InverseBooleanToVisibilityConverter : ValueConverter
{
    public override object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true ? Visibility.Collapsed : Visibility.Visible;

    public override object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is not Visibility.Visible;
}
