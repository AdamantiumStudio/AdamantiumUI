using System;
using System.Globalization;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Data;

namespace Adamantium.UI.Sandbox.Converters;

/// <summary>Checks the toggle whose ConverterParameter names the bound enum value and writes it back when checked; lets a
/// row of buttons drive one choice. Unchecking writes nothing.</summary>
public class EnumToBoolConverter : IValueConverter
{
    /// <inheritdoc/>
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value != null
        && parameter is string wanted
        && string.Equals(value.ToString(), wanted, StringComparison.OrdinalIgnoreCase);

    /// <inheritdoc/>
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not true || parameter is not string wanted) return AdamantiumProperty.UnsetValue;

        var enumType = Nullable.GetUnderlyingType(targetType) ?? targetType;
        return enumType.IsEnum && Enum.TryParse(enumType, wanted, true, out var parsed)
            ? parsed
            : AdamantiumProperty.UnsetValue;
    }
}
