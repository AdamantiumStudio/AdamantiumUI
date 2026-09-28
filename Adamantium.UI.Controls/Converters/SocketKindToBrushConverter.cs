using System;
using System.Collections;
using System.Globalization;
using Adamantium.UI.Controls.DrawingBoard;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Data;
using Adamantium.UI.Core.Media;

namespace Adamantium.UI.Controls.Converters;

/// <summary>A socket's kind into its color, from the application's <see cref="InfiniteCanvas.SocketKinds"/> table; an unknown
/// or empty kind keeps the theme's.</summary>
internal class SocketKindToBrushConverter : IValueConverter
{
    private readonly IEnumerable _kinds;

    public SocketKindToBrushConverter(IEnumerable kinds) => _kinds = kinds;

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (_kinds == null || value is not string kind || string.IsNullOrEmpty(kind))
        {
            return AdamantiumProperty.UnsetValue;
        }

        foreach (var item in _kinds)
        {
            if (item is not ICanvasSocketKind entry || entry.Kind != kind) continue;

            return entry.Color is { } colour ? new SolidColorBrush(colour) : AdamantiumProperty.UnsetValue;
        }

        return AdamantiumProperty.UnsetValue;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        AdamantiumProperty.UnsetValue;
}
