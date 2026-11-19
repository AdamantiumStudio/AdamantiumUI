using System;
using System.Globalization;
using Adamantium.UI.Core.Data;

namespace Adamantium.UI.Controls;

internal sealed class PropertyValueText(PropertyDefinition definition) : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => definition.TextOf(value);

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
