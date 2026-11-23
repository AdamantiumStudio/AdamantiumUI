using System.Collections.Generic;

namespace Adamantium.UI.Platforms.Linux.Hunspell;

/// <summary>An affix rule's condition, as hunspell(5) writes it: characters in a row, each a character, a dot for any,
/// or a class in brackets - [abc], or [^abc] for any but those. A suffix's condition is matched against the end of the
/// stem, a prefix's against its start.</summary>
internal sealed class HunspellCondition
{
    private readonly List<Position> _positions;

    private HunspellCondition(List<Position> positions) => _positions = positions;

    /// <summary>A condition that holds for every stem: "." in the rule.</summary>
    public static HunspellCondition Any { get; } = new([]);

    /// <summary>The condition written as <paramref name="text"/>; one with an unclosed bracket holds for no stem.</summary>
    public static HunspellCondition Parse(string text)
    {
        if (string.IsNullOrEmpty(text) || text == ".")
            return Any;

        List<Position> positions = [];
        for (var index = 0; index < text.Length; index++)
        {
            if (text[index] == '.')
            {
                positions.Add(new Position(null, false));
                continue;
            }

            if (text[index] != '[')
            {
                positions.Add(new Position(text[index].ToString(), false));
                continue;
            }

            var close = text.IndexOf(']', index + 1);
            if (close < 0)
                return new HunspellCondition(null);

            var negated = index + 1 < close && text[index + 1] == '^';
            var start = negated ? index + 2 : index + 1;
            positions.Add(new Position(text[start..close], negated));
            index = close;
        }

        return new HunspellCondition(positions);
    }

    /// <summary>Whether <paramref name="stem"/> ends as the condition says.</summary>
    public bool MatchesEnd(string stem) => _positions != null && _positions.Count <= stem.Length
                                           && Matches(stem, stem.Length - _positions.Count);

    /// <summary>Whether <paramref name="stem"/> begins as the condition says.</summary>
    public bool MatchesStart(string stem) => _positions != null && _positions.Count <= stem.Length && Matches(stem, 0);

    private bool Matches(string stem, int from)
    {
        for (var index = 0; index < _positions.Count; index++)
        {
            var position = _positions[index];
            if (position.Characters == null)
                continue;

            if (position.Characters.Contains(stem[from + index]) == position.Negated)
                return false;
        }

        return true;
    }

    private sealed record Position(string Characters, bool Negated);
}
