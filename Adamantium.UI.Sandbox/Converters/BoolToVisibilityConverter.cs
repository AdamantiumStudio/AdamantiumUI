using System;
using System.Globalization;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Data;

namespace Adamantium.UI.Sandbox.Converters;

/// <summary>
/// Maps a bool to <see cref="Visibility"/> (true =&gt; Visible, false =&gt; Collapsed) so a toggle can drive the
/// visibility of the permanent diagnostics plate in the Sandbox demo; the parameter "Invert" turns it round, for the one
/// of two things that shows while the other does not.
/// </summary>
public class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        (value is true) != IsInverted(parameter) ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        (value is Visibility.Visible) != IsInverted(parameter);

    private static bool IsInverted(object parameter) => parameter as string == "Invert";
}
