using System.Collections;
using System.Runtime.CompilerServices;
using Adamantium.Core.TypeParsing;
using Adamantium.Fonts.Shaping;

namespace Adamantium.UI.Core.Media;

/// <summary>
/// OpenType features for text: <c>[FontFeature.Ligatures.Off, FontFeature.StylisticSet(1)]</c> in code,
/// <c>FontFeatures="liga=0, ss01, cv05=2"</c> in markup, where a tag the OpenType registry does not have is an error.
/// Immutable: a change is a new list.
/// </summary>
[TypeParser(typeof(FontFeatureListParser))]
[CollectionBuilder(typeof(FontFeatureList), nameof(Create))]
public sealed class FontFeatureList : IReadOnlyList<FontFeature>, IEquatable<FontFeatureList>
{
    private readonly FontFeature[] _features;

    public FontFeatureList(IEnumerable<FontFeature> features)
    {
        _features = features?.ToArray() ?? [];
    }

    public static FontFeatureList Empty { get; } = new([]);

    public static FontFeatureList Create(ReadOnlySpan<FontFeature> features) => new(features.ToArray());

    /// <summary>Parses <c>liga=0, ss01, cv05=2</c>, checked as <see cref="FontFeature.TryParseList"/> checks it.</summary>
    public static FontFeatureList Parse(string text) => new(FontFeature.ParseList(text));

    public int Count => _features.Length;

    public FontFeature this[int index] => _features[index];

    public IEnumerator<FontFeature> GetEnumerator() => ((IEnumerable<FontFeature>)_features).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public bool Equals(FontFeatureList other) => other != null && _features.SequenceEqual(other._features);

    public override bool Equals(object obj) => obj is FontFeatureList other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var feature in _features)
        {
            hash.Add(feature);
        }

        return hash.ToHashCode();
    }

    public override string ToString() => string.Join(", ", _features);
}
