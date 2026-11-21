using System;
using System.Globalization;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Data;

namespace Adamantium.UI.Controls.Converters;

/// <summary>True to <see cref="Visibility.Visible"/>, false to <see cref="Visibility.Collapsed"/>, and back, so a
/// switch and what it shows can be bound two-way. What shows while the switch is off takes
/// <see cref="InverseBooleanToVisibilityConverter"/>.</summary>
public class BooleanToVisibilityConverter : ValueConverter
{
    public override object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true ? Visibility.Visible : Visibility.Collapsed;

    public override object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is Visibility.Visible;
}
