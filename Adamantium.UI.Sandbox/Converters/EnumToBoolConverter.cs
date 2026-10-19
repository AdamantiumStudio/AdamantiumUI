using System;
using System.Globalization;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Data;

namespace Adamantium.UI.Sandbox.Converters;

/// <summary>Checks the toggle whose ConverterParameter names the bound value - an enum member, a string, a number - and
/// writes it back when checked; lets a row of buttons drive one choice. Unchecking writes nothing.</summary>
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

        var valueType = Nullable.GetUnderlyingType(targetType) ?? targetType;
        if (valueType.IsEnum)
        {
            return Enum.TryParse(valueType, wanted, true, out var parsed) ? parsed : AdamantiumProperty.UnsetValue;
        }

        try
        {
            return System.Convert.ChangeType(wanted, valueType, CultureInfo.InvariantCulture);
        }
        catch (Exception e) when (e is FormatException or InvalidCastException or OverflowException)
        {
            return AdamantiumProperty.UnsetValue;
        }
    }
}
