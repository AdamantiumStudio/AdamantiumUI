using System.Collections.Generic;
using Adamantium.UI.Core.Printing;

namespace Adamantium.UI.Platforms.Cups;

/// <summary>A value a printer offers: the keyword IPP names it by, and its name in the user's language.</summary>
internal sealed record CupsValue(string Keyword, string Title);

/// <summary>A paper a printer takes, in hundredths of a millimeter, standing upright.</summary>
internal sealed record CupsMedia(string Keyword, string Title, int Width, int Length, int Left, int Top, int Right,
    int Bottom)
{
    public bool IsBorderless => Left == 0 && Top == 0 && Right == 0 && Bottom == 0;
}

/// <summary>What CUPS answers about a printer, as IPP says it (RFC 8011, PWG 5100).</summary>
internal sealed class CupsAnswers
{
    public IReadOnlyList<CupsMedia> Media { get; init; } = [];

    /// <summary>The paper it prints on unless told otherwise, one of <see cref="Media"/>.</summary>
    public CupsMedia DefaultMedia { get; init; }

    public IReadOnlyList<string> Sides { get; init; } = [];

    public IReadOnlyList<string> ColorModes { get; init; } = [];

    public IReadOnlyList<PrintQuality> Resolutions { get; init; } = [];

    public PrintQuality DefaultResolution { get; init; }

    public IReadOnlyList<CupsValue> Sources { get; init; } = [];

    public IReadOnlyList<CupsValue> Types { get; init; } = [];

    /// <summary>The source and the type it takes unless told otherwise; null where it does not say.</summary>
    public string DefaultSource { get; init; }

    public string DefaultType { get; init; }

    public int MaxCopies { get; init; } = 1;

    /// <summary>How it orders the copies of a job: multiple-document-handling.</summary>
    public IReadOnlyList<string> Handling { get; init; } = [];
}
