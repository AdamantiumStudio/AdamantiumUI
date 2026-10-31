using Adamantium.UI.Core.Resources;

namespace Adamantium.UI.Core;

internal class StyleValuePair
{
    public StyleValuePair(Style style, object value, int band)
    {
        Style = style;
        Value = value;
        Band = band;
    }
    public Style Style { get; }

    public object Value { get; set; }

    /// <summary>Where the style stands among the element's styles: its own band, or the one its scope gives it.</summary>
    public int Band { get; }
}