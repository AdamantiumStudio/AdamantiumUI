using System;
using System.Globalization;
using Adamantium.Mathematics;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Data;

namespace Adamantium.UI.Controls.Converters;

/// <summary>A color kept as numbers - a <see cref="Vector3F"/> of red, green and blue, or a <see cref="Vector4F"/> with
/// alpha, as a game's components keep it - shown as a <see cref="Color"/> and written back as the same kind, so a color
/// picker edits it.</summary>
public class VectorToColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value switch
    {
        Vector3F rgb => new Color(rgb),
        Vector4F rgba => new Color(rgba),
        _ => AdamantiumProperty.UnsetValue
    };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not Color color) return AdamantiumProperty.UnsetValue;
        if (targetType == typeof(Vector3F)) return color.ToVector3();
        if (targetType == typeof(Vector4F)) return color.ToVector4();
        return AdamantiumProperty.UnsetValue;
    }
}
