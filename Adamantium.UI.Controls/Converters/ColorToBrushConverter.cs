using System;
using System.Globalization;
using Adamantium.Mathematics;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Data;
using Adamantium.UI.Core.Media;

namespace Adamantium.UI.Controls.Converters;

/// <summary>A color into a new brush per binding, and back, so no shared brush gets recolored; a null color leaves the
/// target to the theme.</summary>
public class ColorToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is Color colour ? new SolidColorBrush(colour) : AdamantiumProperty.UnsetValue;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is SolidColorBrush brush ? brush.Color : null;
}
