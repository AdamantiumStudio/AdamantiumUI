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
    private readonly FontVariationList _from;
    private readonly FontVariationList _to;
    private readonly float _progress;

    public FontVariationList(IEnumerable<FontVariation> variations)
    {
        _variations = variations?.ToArray() ?? [];
    }

    private FontVariationList(FontVariation[] variations, FontVariationList from, FontVariationList to, float progress)
    {
        _variations = variations;
        _from = from;
        _to = to;
        _progress = progress;
    }

    /// <summary>The values at <paramref name="progress"/> of the way from <paramref name="from"/> to
    /// <paramref name="to"/>, as an animation passes them: an axis in both moves between its two values, an axis in one
    /// moves from or to what the font has. Text in them is laid out at exactly these values and drawn between the font's
    /// key instances on the way (<see cref="IFont.GetInstance(IReadOnlyList{FontVariation}, IReadOnlyList{FontVariation}, float)"/>).
    /// At either end it is that list itself, and the text is drawn as it is at rest.</summary>
    public static FontVariationList Between(FontVariationList from, FontVariationList to, double progress)
    {
        from ??= Empty;
        to ??= Empty;
        if (progress is <= 0 or >= 1)
        {
            return progress <= 0 ? from : to;
        }

        var at = (float)progress;
        var variations = from.Where(v => to.All(other => other.Tag != v.Tag))
            .Concat(to.Select(v => from.FirstOrDefault(other => other.Tag == v.Tag) is { Tag: not null } start
                ? new FontVariation(v.Tag, start.Value + (v.Value - start.Value) * at)
                : v))
            .ToArray();
        return new FontVariationList(variations, from, to, at);
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
        if (font.Axes.Count == 0)
        {
            return font;
        }

        if (_from != null)
        {
            return font.GetInstance(_from.Over(font), _to.Over(font), _progress);
        }

        return _variations.Length == 0 ? font : font.GetInstance(Over(font));
    }

    private FontVariation[] Over(IFont font) =>
        font.Variations
            .Where(v => v.Tag != "opsz" && _variations.All(own => own.Tag != v.Tag))
            .Concat(_variations)
            .ToArray();

    public int Count => _variations.Length;

    public FontVariation this[int index] => _variations[index];

    public IEnumerator<FontVariation> GetEnumerator() => ((IEnumerable<FontVariation>)_variations).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public bool Equals(FontVariationList other) =>
        other != null && _variations.SequenceEqual(other._variations) && Equals(_from, other._from) &&
        Equals(_to, other._to) && _progress.Equals(other._progress);

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
