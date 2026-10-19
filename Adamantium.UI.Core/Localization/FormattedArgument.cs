namespace Adamantium.UI.Core.Localization;

// A phrase's argument whose binding has a StringFormat: written by it in the culture the phrase is written in, while
// its value still chooses the phrase's case. A format the phrase gives its placeholder (a translator's {size:N2}) is
// applied to the value first, and the binding's around it.
internal sealed class FormattedArgument(object value, string format) : IFormattable
{
    public object Value { get; } = value;

    public string ToString(string placeholderFormat, IFormatProvider provider)
    {
        var written = !string.IsNullOrEmpty(placeholderFormat) && Value is IFormattable formattable
            ? formattable.ToString(placeholderFormat, provider)
            : Value;
        return string.Format(provider, format, written);
    }

    public override string ToString() => ToString(null, System.Globalization.CultureInfo.CurrentCulture);
}
