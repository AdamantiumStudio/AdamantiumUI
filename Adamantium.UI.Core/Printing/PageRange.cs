using System.Collections.Generic;
using System.Globalization;

namespace Adamantium.UI.Core.Printing;

/// <summary>Which pages are printed: all of them, the one shown, or those written out - "1-3, 5, 8-", numbered from 1,
/// "8-" running to the end.</summary>
public sealed class PageRange
{
    private readonly List<(int First, int Last)> _spans;

    private PageRange(PageRangeKind kind, string text, List<(int First, int Last)> spans)
    {
        Kind = kind;
        Text = text;
        _spans = spans;
    }

    public static PageRange All { get; } = new(PageRangeKind.All, string.Empty, []);

    public static PageRange Current { get; } = new(PageRangeKind.Current, string.Empty, []);

    public PageRangeKind Kind { get; }

    /// <summary>The pages as written, for <see cref="PageRangeKind.Chosen"/>.</summary>
    public string Text { get; }

    /// <summary>The pages <paramref name="text"/> writes out; false when it writes none, or what it writes is not page
    /// numbers - a page 0, a span running backwards.</summary>
    public static bool TryParse(string text, out PageRange range)
    {
        range = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var spans = new List<(int First, int Last)>();
        foreach (var part in text.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var dash = part.IndexOfAny(['-', '–', '—']);
            if (dash < 0)
            {
                if (!Number(part, out var page))
                {
                    return false;
                }

                spans.Add((page, page));
                continue;
            }

            var from = part[..dash].Trim();
            var to = part[(dash + 1)..].Trim();
            var first = 1;
            var last = int.MaxValue;
            if (from.Length > 0 && !Number(from, out first) || to.Length > 0 && !Number(to, out last) || last < first)
            {
                return false;
            }

            spans.Add((first, last));
        }

        if (spans.Count == 0)
        {
            return false;
        }

        range = new PageRange(PageRangeKind.Chosen, text.Trim(), spans);
        return true;
    }

    private static bool Number(string text, out int page) =>
        int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out page) && page >= 1;

    /// <summary>The pages printed of a document of <paramref name="count"/>, showing <paramref name="current"/> -
    /// numbered from 0, in order, each once; pages past its end left out.</summary>
    public IReadOnlyList<int> Pages(int count, int current)
    {
        var pages = new List<int>();
        switch (Kind)
        {
            case PageRangeKind.All:
                for (var page = 0; page < count; page++)
                {
                    pages.Add(page);
                }

                break;
            case PageRangeKind.Current:
                if (count > 0)
                {
                    pages.Add(Math.Clamp(current, 0, count - 1));
                }

                break;
            default:
                var chosen = new bool[count];
                foreach (var (first, last) in _spans)
                {
                    for (var page = first; page <= Math.Min(last, count); page++)
                    {
                        chosen[page - 1] = true;
                    }
                }

                for (var page = 0; page < count; page++)
                {
                    if (chosen[page])
                    {
                        pages.Add(page);
                    }
                }

                break;
        }

        return pages;
    }

    public override string ToString() => Kind == PageRangeKind.Chosen ? Text : Kind.ToString();
}
