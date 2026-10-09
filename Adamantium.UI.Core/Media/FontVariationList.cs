using System.Collections;
using System.Globalization;
using System.Runtime.CompilerServices;
using Adamantium.Core.TypeParsing;
using Adamantium.Fonts;

namespace Adamantium.UI.Core.Media;

/// <summary>
/// Values of a variable font's axes for text, over the ones its weight, width and style set:
/// <c>FontVariations="wght=650, GRAD=150, opsz=36"</c> in markup. An axis the font lacks is left out; <c>opsz=auto</c>,
/// like leaving <c>opsz</c> out, sets the optical size to the text's size. Immutable: a change is a new list.
/// </summary>
[TypeParser(typeof(FontVariationListParser))]
[CollectionBuilder(typeof(FontVariationList), nameof(Create))]
public sealed class FontVariationList : IReadOnlyList<FontVariation>, IEquatable<FontVariationList>
{
    private readonly FontVariation[] _variations;

    public FontVariationList(IEnumerable<FontVariation> variations)
    {
        _variations = variations?.ToArray() ?? [];
    }

    public static FontVariationList Empty { get; } = new([]);

    public static FontVariationList Create(ReadOnlySpan<FontVariation> variations) => new(variations.ToArray());

    /// <summary>Parses <c>wght=650, opsz=auto</c>: four-letter axis tags, each with a number or, for <c>opsz</c>,
    /// <c>auto</c>.</summary>
    /// <exception cref="FormatException">An entry is not a tag, '=' and a value.</exception>
    public static FontVariationList Parse(string text)
    {
        var variations = new List<FontVariation>();
        foreach (var entry in (text ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = entry.Split('=');
            var tag = parts[0].Trim().Trim('"', '\'');
            if (parts.Length != 2 || tag.Length != 4)
            {
                throw new FormatException($"'{entry.Trim()}' is not an axis and its value, as wght=650");
            }

            var value = parts[1].Trim();
            if (tag == "opsz" && value == "auto")
            {
                continue;
            }

            if (!float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
            {
                throw new FormatException($"'{value}' is not a value for the axis {tag}");
            }

            variations.Add(new FontVariation(tag, number));
        }

        return new FontVariationList(variations);
    }

    /// <summary><paramref name="font"/> with these values over the ones it has; the font itself when there are none.</summary>
    public IFont Apply(IFont font)
    {
        if (_variations.Length == 0 || font.Axes.Count == 0)
        {
            return font;
        }

        var values = font.Variations
            .Where(v => v.Tag != "opsz" && _variations.All(own => own.Tag != v.Tag))
            .Concat(_variations)
            .ToArray();
        return font.GetInstance(values);
    }

    public int Count => _variations.Length;

    public FontVariation this[int index] => _variations[index];

    public IEnumerator<FontVariation> GetEnumerator() => ((IEnumerable<FontVariation>)_variations).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public bool Equals(FontVariationList other) => other != null && _variations.SequenceEqual(other._variations);

    public override bool Equals(object obj) => obj is FontVariationList other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var variation in _variations)
        {
            hash.Add(variation);
        }

        return hash.ToHashCode();
    }

    public override string ToString() => string.Join(", ", _variations);
}
