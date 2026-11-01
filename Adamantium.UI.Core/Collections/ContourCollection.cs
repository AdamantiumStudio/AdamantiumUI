using Adamantium.Core.Collections;
using Adamantium.Core.TypeParsing;
using Adamantium.UI.Core.TypeParsers;

namespace Adamantium.UI.Core.Collections;

/// <summary>Closed rings of points - the edge of an area, its outer rings and its holes. In markup: rings apart by
/// semicolons, <c>"0,0 10,0 10,10 0,10; 2,2 2,8 8,8 8,2"</c>.</summary>
[TypeParser(typeof(ContourCollectionParser))]
public class ContourCollection : TrackingCollection<PointsCollection>
{
    public ContourCollection()
    {
    }

    public ContourCollection(IEnumerable<PointsCollection> contours) : base(contours)
    {
    }
}
