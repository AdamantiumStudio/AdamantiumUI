using System.Globalization;
using Adamantium.UI.Core.MarkupExtensions;

namespace Adamantium.UI.Core.Data;

/// <summary>A <see cref="MultiBinding"/>'s converter markup writes as <c>{Name}</c>, its properties as arguments, with no
/// resource to declare. Markup makes one for each place it is written and keeps it for every build of that place, so a
/// converter keeps no state between calls.</summary>
public abstract class MultiValueConverter : MarkupExtension, IMultiValueConverter
{
    public abstract object Convert(object[] value, Type targetType, object parameter, CultureInfo culture);

    public abstract object[] ConvertBack(object value, Type targetType, object parameter, CultureInfo culture);

    /// <summary>The converter itself.</summary>
    public sealed override object ProvideObject(MarkupContext context) => this;
}
