using System.Collections;
using System.Globalization;
using System.Runtime.CompilerServices;
using Adamantium.Core.TypeParsing;
using Adamantium.Mathematics;

namespace Adamantium.UI.Core.Media;

/// <summary>
/// Areas text flows around: <c>[new RectangleF(0, 0, 120, 90)]</c> in code, <c>Exclusions="0,0,120,90; 240,200,100,100"</c>
/// in markup - left, top, width and height of each, the areas apart by semicolons. Immutable: a change is a new list.
/// </summary>
[TypeParser(typeof(ExclusionListParser))]
[CollectionBuilder(typeof(ExclusionList), nameof(Create))]
public sealed class ExclusionList : IReadOnlyList<RectangleF>, IEquatable<ExclusionList>
{
    private readonly RectangleF[] _areas;

    public ExclusionList(IEnumerable<RectangleF> areas)
    {
        _areas = areas?.ToArray() ?? [];
    }

    public static ExclusionList Empty { get; } = new([]);

    public static ExclusionList Create(ReadOnlySpan<RectangleF> areas) => new(areas.ToArray());

    /// <summary>Parses <c>0,0,120,90; 240,200,100,100</c>.</summary>
    public static ExclusionList Parse(string text)
    {
        List<RectangleF> areas = [];
        foreach (var entry in (text ?? string.Empty).Split(';'))
        {
            var numbers = entry.Split([',', ' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
            if (numbers.Length == 0)
            {
                continue;
            }

            if (numbers.Length != 4)
            {
                throw new FormatException($"'{entry.Trim()}' is not an area: left, top, width and height.");
            }

            var values = new float[4];
            for (var i = 0; i < 4; i++)
            {
                if (!float.TryParse(numbers[i], NumberStyles.Float, CultureInfo.InvariantCulture, out values[i])
                    || float.IsNaN(values[i]) || float.IsInfinity(values[i]) || (i >= 2 && values[i] < 0))
                {
                    throw new FormatException($"'{numbers[i]}' is not a {(i < 2 ? "position" : "size")} of an area.");
                }
            }

            areas.Add(new RectangleF(values[0], values[1], values[2], values[3]));
        }

        return new ExclusionList(areas);
    }

    public int Count => _areas.Length;

    public RectangleF this[int index] => _areas[index];

    public IEnumerator<RectangleF> GetEnumerator() => ((IEnumerable<RectangleF>)_areas).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public bool Equals(ExclusionList other) => other != null && _areas.SequenceEqual(other._areas);

    public override bool Equals(object obj) => obj is ExclusionList other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var area in _areas)
        {
            hash.Add(area);
        }

        return hash.ToHashCode();
    }

    /// <summary>The areas as <see cref="Parse"/> reads them back.</summary>
    public override string ToString() => string.Join("; ", _areas.Select(Format));

    private static string Format(RectangleF area)
    {
        float[] values = [area.X, area.Y, area.Width, area.Height];
        return string.Join(",", values.Select(value => value.ToString("R", CultureInfo.InvariantCulture)));
    }
}
