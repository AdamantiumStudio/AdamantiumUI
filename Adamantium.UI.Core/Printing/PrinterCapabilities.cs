using System.Collections.Generic;
using Adamantium.Mathematics;

namespace Adamantium.UI.Core.Printing;

/// <summary>What a printer can do, as the system says - each option of the print dialog that is the printer's is shown
/// only when it has more than one choice here, and offers only these. An option the printer lacks never shows: there is
/// nothing it would do.</summary>
public sealed class PrinterCapabilities
{
    /// <summary>The paper the printer takes; the first is used when the default is not among them.</summary>
    public IReadOnlyList<PaperSize> Papers { get; init; } = PaperSize.Standard;

    public PaperSize DefaultPaper { get; init; } = PaperSize.A4;

    /// <summary>The most copies the printer makes of a job itself; 1 hides the copies.</summary>
    public int MaxCopies { get; init; } = 1;

    /// <summary>Whether it puts copies in order - all of the first, then all of the second.</summary>
    public bool Collates { get; init; }

    /// <summary>Which sides it prints; one choice or none hides the option.</summary>
    public IReadOnlyList<DuplexMode> Duplex { get; init; } = [];

    /// <summary>Color, gray, or both; one choice or none hides the option.</summary>
    public IReadOnlyList<PrintColorMode> Colors { get; init; } = [];

    /// <summary>The resolutions it prints at; one or none hides the option.</summary>
    public IReadOnlyList<PrintQuality> Qualities { get; init; } = [];

    public PrintQuality DefaultQuality { get; init; }

    /// <summary>Where it takes paper from; one or none hides the option.</summary>
    public IReadOnlyList<string> Trays { get; init; } = [];

    /// <summary>The kinds of paper it is told it prints on; one or none hides the option.</summary>
    public IReadOnlyList<string> MediaTypes { get; init; } = [];

    /// <summary>The edges of each paper it cannot reach, in DIPs, the paper standing upright; none where not given.</summary>
    public IReadOnlyDictionary<string, Thickness> Margins { get; init; } = new Dictionary<string, Thickness>();

    /// <summary>Whether its driver has a window of its own for what the dialog does not offer - Windows only.</summary>
    public bool HasDriverSettings { get; init; }

    /// <summary>What saving to a PDF file can do: every standard paper, nothing of a printer's.</summary>
    public static PrinterCapabilities Pdf { get; } = new();

    /// <summary>The margins of <paramref name="paper"/>, none where the printer does not say.</summary>
    public Thickness MarginsOf(PaperSize paper) =>
        paper != null && Margins.TryGetValue(paper.Name, out var margins) ? margins : default;
}
