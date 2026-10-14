using Adamantium.Core.TypeParsing;
using Adamantium.UI.Core.Media;

namespace Adamantium.UI.Core.TypeParsers;

public class BrushParser : ITypeParser<Brush>
{
    public Brush Parse(string value)
    {
        return value == CurrentColorBrush.Name ? new CurrentColorBrush() : new SolidColorBrush(value);
    }
}