using Adamantium.Core.Collections;
using Adamantium.Core.TypeParsing;
using Adamantium.UI.Core.TypeParsers;

namespace Adamantium.UI.Core.Resources;

/// <summary>A component's classes, which style selectors match. One belongs to one component - it changes and watches
/// it as its own - so markup builds one per target for a setter's value or a keyed resource; code gives a setter a
/// <see cref="PerTargetValue"/>.</summary>
[TypeParser(typeof(ClassesParser))]
[PerTarget]
public class  Classes : TrackingCollection<string>
{
    public Classes()
    {

    }

    public Classes(IEnumerable<string> collection): base(collection)
    {

    }

    public static Classes Parse(string identifierString)
    {
        var ids = identifierString.Split(' ', System.StringSplitOptions.RemoveEmptyEntries | System.StringSplitOptions.TrimEntries);

        if (ids.Length == 0) return null;

        return new Classes(ids);
    }
    
    public override string ToString()
    {
        return string.Join(' ', this);
    }
}
