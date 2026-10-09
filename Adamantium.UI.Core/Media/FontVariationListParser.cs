using Adamantium.Core.TypeParsing;

namespace Adamantium.UI.Core.Media;

/// <summary>Turns <c>FontVariations="wght=650, opsz=auto"</c> in markup into a <see cref="FontVariationList"/>.</summary>
public class FontVariationListParser : ITypeParser<FontVariationList>
{
    public FontVariationList Parse(string value) => FontVariationList.Parse(value);
}
