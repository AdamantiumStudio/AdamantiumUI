using System;
using System.Globalization;
using System.Linq;
using Adamantium.Core.TypeParsing;
using Adamantium.UI.Core.Collections;

namespace Adamantium.UI.Core.TypeParsers;

/// <summary>Parses <c>"0 1 2 2 3 0"</c> - whole numbers apart by whitespace or commas - into an
/// <see cref="IndexCollection"/>.</summary>
public class IndexCollectionParser : ITypeParser<IndexCollection>
{
    private static readonly char[] Separators = [' ', ',', '\t', '\r', '\n'];

    public IndexCollection Parse(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return new IndexCollection();
        }

        return new IndexCollection(value.Split(Separators, StringSplitOptions.RemoveEmptyEntries)
            .Select(index => int.Parse(index, NumberStyles.None, CultureInfo.InvariantCulture)));
    }
}
