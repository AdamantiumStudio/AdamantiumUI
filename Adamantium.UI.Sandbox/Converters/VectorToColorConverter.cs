using System;
using System.Globalization;
using Adamantium.Mathematics;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Data;

namespace Adamantium.UI.Sandbox.Converters;

/// <summary>Shows a colour kept as a <see cref="Vector3F"/> - a light's, 0 to 1 per channel - in a colour picker, and
/// hands the picked <see cref="Color"/> back the same way.</summary>
public class VectorToColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is Vector3F vector ? new Color(vector) : AdamantiumProperty.UnsetValue;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is Color color ? color.ToVector3() : null;
    }
}
