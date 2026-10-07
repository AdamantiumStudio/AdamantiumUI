using Adamantium.Core.TypeParsing;

namespace Adamantium.UI.Core.Media;

/// <summary>Turns <c>FontFeatures="liga=0, ss01"</c> in markup into a <see cref="FontFeatureList"/>.</summary>
public class FontFeatureListParser : ITypeParser<FontFeatureList>
{
    public FontFeatureList Parse(string value) => FontFeatureList.Parse(value);
}
