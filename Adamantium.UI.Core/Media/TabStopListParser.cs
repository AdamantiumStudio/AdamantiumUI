using Adamantium.Core.TypeParsing;

namespace Adamantium.UI.Core.Media;

/// <summary>Turns <c>TabStops="120, 300 Right Leader=."</c> in markup into a <see cref="TabStopList"/>.</summary>
public class TabStopListParser : ITypeParser<TabStopList>
{
    public TabStopList Parse(string value) => TabStopList.Parse(value);
}
