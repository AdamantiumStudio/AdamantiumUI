using System;
using System.Globalization;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Data;
using Adamantium.UI.Core.Media;

namespace Adamantium.UI.Sandbox.Converters;

/// <summary>Shows the element whose ConverterParameter names the bound enum value and collapses the rest; works for any
/// enum.</summary>
public class EnumVisibilityConverter : IValueConverter
{
    /// <inheritdoc/>
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var wanted = parameter as string;
        var visible = value != null
                      && !string.IsNullOrEmpty(wanted)
                      && string.Equals(value.ToString(), wanted, StringComparison.OrdinalIgnoreCase);
        return visible ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <inheritdoc/>
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => null;
}
