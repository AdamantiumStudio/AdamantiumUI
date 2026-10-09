using System.Collections;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using Adamantium.Core.TypeParsing;
using Adamantium.Graphics.Fonts;

namespace Adamantium.UI.Core.Media;

/// <summary>
/// The tab stops of text: <c>[new TabStop(120), new TabStop(300, TabAlignment.Right, ".")]</c> in code,
/// <c>TabStops="120, 300 Right Leader=., 400 Decimal AlignOn=','"</c> in markup - each stop a position, then an
/// alignment (<c>Left</c> by default, <c>Center</c>, <c>Right</c>, <c>Decimal</c>), what fills the gap before it and
/// the character a decimal stop lines up on (<c>.</c> by default), a value in single quotes when it holds a comma or a
/// space. Immutable: a change is a new list.
/// </summary>
[TypeParser(typeof(TabStopListParser))]
[CollectionBuilder(typeof(TabStopList), nameof(Create))]
public sealed class TabStopList : IReadOnlyList<TabStop>, IEquatable<TabStopList>
{
    private readonly TabStop[] _stops;

    public TabStopList(IEnumerable<TabStop> stops)
    {
        _stops = stops?.ToArray() ?? [];
    }

    public static TabStopList Empty { get; } = new([]);

    public static TabStopList Create(ReadOnlySpan<TabStop> stops) => new(stops.ToArray());

    /// <summary>Parses <c>120, 300 Right Leader=., 400 Decimal AlignOn=','</c>.</summary>
    public static TabStopList Parse(string text)
    {
        List<TabStop> stops = [];
        foreach (var entry in Split(text ?? string.Empty, symbol => symbol == ','))
        {
            var tokens = Split(entry, char.IsWhiteSpace);
            if (tokens.Count == 0)
            {
                continue;
            }

            if (!double.TryParse(tokens[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var position)
                || double.IsNaN(position) || double.IsInfinity(position) || position < 0)
            {
                throw new FormatException($"'{tokens[0]}' is not the position of a tab stop: a number, zero or more.");
            }

            var alignment = TabAlignment.Left;
            string leader = null;
            var alignOn = '.';
            foreach (var token in tokens.Skip(1))
            {
                if (token.StartsWith("Leader=", StringComparison.OrdinalIgnoreCase))
                {
                    leader = Unquote(token.Substring("Leader=".Length));
                }
                else if (token.StartsWith("AlignOn=", StringComparison.OrdinalIgnoreCase))
                {
                    var value = Unquote(token.Substring("AlignOn=".Length));
                    if (value.Length != 1)
                    {
                        throw new FormatException($"A tab stop aligns on one character, not '{value}'.");
                    }

                    alignOn = value[0];
                }
                else if (!token.All(char.IsLetter) || !Enum.TryParse(token, true, out alignment))
                {
                    throw new FormatException($"'{token}' is not a tab alignment: Left, Center, Right or Decimal.");
                }
            }

            stops.Add(new TabStop(position, alignment, leader, alignOn));
        }

        return new TabStopList(stops);
    }

    public int Count => _stops.Length;

    public TabStop this[int index] => _stops[index];

    public IEnumerator<TabStop> GetEnumerator() => ((IEnumerable<TabStop>)_stops).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public bool Equals(TabStopList other) => other != null && _stops.SequenceEqual(other._stops);

    public override bool Equals(object obj) => obj is TabStopList other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var stop in _stops)
        {
            hash.Add(stop);
        }

        return hash.ToHashCode();
    }

    /// <summary>The stops as <see cref="Parse"/> reads them back.</summary>
    public override string ToString() => string.Join(", ", _stops.Select(Format));

    private static string Format(TabStop stop)
    {
        var text = new StringBuilder(stop.Position.ToString("R", CultureInfo.InvariantCulture));
        if (stop.Alignment != TabAlignment.Left)
        {
            text.Append(' ').Append(stop.Alignment);
        }

        if (stop.Leader != null)
        {
            text.Append(" Leader=").Append(Quote(stop.Leader));
        }

        if (stop.AlignOn != '.')
        {
            text.Append(" AlignOn=").Append(Quote(stop.AlignOn.ToString()));
        }

        return text.ToString();
    }

    private static string Quote(string value) =>
        value.Any(symbol => symbol == ',' || char.IsWhiteSpace(symbol)) ? $"'{value}'" : value;

    private static List<string> Split(string text, Func<char, bool> separates)
    {
        List<string> parts = [];
        var part = new StringBuilder();
        var quoted = false;
        foreach (var symbol in text)
        {
            if (symbol == '\'')
            {
                quoted = !quoted;
            }

            if (separates(symbol) && !quoted)
            {
                Add();
                continue;
            }

            part.Append(symbol);
        }

        if (quoted)
        {
            throw new FormatException($"A quote in '{text}' is not closed.");
        }

        Add();
        return parts;

        void Add()
        {
            var value = part.ToString().Trim();
            if (value.Length > 0)
            {
                parts.Add(value);
            }

            part.Clear();
        }
    }

    private static string Unquote(string value) =>
        value.Length >= 2 && value[0] == '\'' && value[value.Length - 1] == '\'' ? value.Substring(1, value.Length - 2) : value;
}
