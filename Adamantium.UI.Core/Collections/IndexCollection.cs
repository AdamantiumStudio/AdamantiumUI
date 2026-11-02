using Adamantium.Core.Collections;
using Adamantium.Core.TypeParsing;
using Adamantium.UI.Core.TypeParsers;

namespace Adamantium.UI.Core.Collections;

/// <summary>Indices into a list of points - three to a triangle in a <see cref="Media.MeshGeometry"/>. In markup:
/// <c>"0 1 2 2 3 0"</c>.</summary>
[TypeParser(typeof(IndexCollectionParser))]
public class IndexCollection : TrackingCollection<int>
{
    public IndexCollection()
    {
    }

    public IndexCollection(IEnumerable<int> indices) : base(indices)
    {
    }
}
