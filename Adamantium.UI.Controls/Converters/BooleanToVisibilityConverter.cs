using System;
using System.Globalization;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Data;

namespace Adamantium.UI.Controls.Converters;

/// <summary>True to <see cref="Visibility.Visible"/>, false to <see cref="Visibility.Collapsed"/>, and back, so a
/// switch and what it shows can be bound two-way. The parameter "Invert" turns it round, for what shows while the switch
/// is off.</summary>
public class BooleanToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        (value is true) != IsInverted(parameter) ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        (value is Visibility.Visible) != IsInverted(parameter);

    private static bool IsInverted(object parameter) => parameter as string == "Invert";
}
