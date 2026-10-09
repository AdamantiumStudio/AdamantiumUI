using Adamantium.Core.TypeParsing;

namespace Adamantium.UI.Core.Media;

/// <summary>Turns <c>Exclusions="0,0,120,90; 240,200,100,100"</c> in markup into an <see cref="ExclusionList"/>.</summary>
public class ExclusionListParser : ITypeParser<ExclusionList>
{
    public ExclusionList Parse(string value) => ExclusionList.Parse(value);
}
