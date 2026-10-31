using Adamantium.Core.TypeParsing;
using Adamantium.Mathematics;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Media;
using Adamantium.UI.Core.Resources;

namespace Adamantium.XamlTests;

/// <summary>A style set linked into an element's resources: one style made by its constructor, one when initialized.
/// </summary>
public sealed class ScopedPlateStyles : StyleSet
{
    public ScopedPlateStyles()
    {
        Add(Plate("Background", new SolidColorBrush(Colors.Red)));
    }

    protected override void OnInitialize(ITheme theme)
    {
        Add(Plate("BorderThickness", "5"));
    }

    private static Style Plate(string property, object value)
    {
        var style = new Style { Selector = TypeParser.Parse<StyleSelector>("Border.Plate") };
        style.Add(new Setter(property, value));
        return style;
    }
}
