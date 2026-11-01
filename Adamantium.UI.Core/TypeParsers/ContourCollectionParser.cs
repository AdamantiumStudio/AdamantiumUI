using System;
using System.Linq;
using Adamantium.Core.TypeParsing;
using Adamantium.UI.Core.Collections;

namespace Adamantium.UI.Core.TypeParsers;

/// <summary>Parses rings of points, apart by semicolons - <c>"0,0 10,0 10,10 0,10; 2,2 2,8 8,8 8,2"</c> - into a
/// <see cref="ContourCollection"/>, each ring as <see cref="PointsCollectionParser"/> reads one.</summary>
public class ContourCollectionParser : ITypeParser<ContourCollection>
{
    private static readonly PointsCollectionParser Ring = new();

    public ContourCollection Parse(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return new ContourCollection();
        }

        return new ContourCollection(value.Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Where(ring => !string.IsNullOrWhiteSpace(ring))
            .Select(Ring.Parse));
    }
}
