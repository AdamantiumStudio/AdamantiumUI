using System.Collections.Generic;

namespace Adamantium.UI.Core.Printing;

/// <summary>A sheet of paper, standing upright: its <see cref="Name"/> as standards and printers know it (ISO 216 "A4",
/// ANSI "Letter") and its size in DIPs, 96 to the inch.</summary>
public sealed record PaperSize(string Name, double Width, double Height)
{
    private const double PerMillimeter = 96 / 25.4;

    public static PaperSize A3 { get; } = Millimeters("A3", 297, 420);

    public static PaperSize A4 { get; } = Millimeters("A4", 210, 297);

    public static PaperSize A5 { get; } = Millimeters("A5", 148, 210);

    public static PaperSize B5 { get; } = Millimeters("B5", 176, 250);

    public static PaperSize Letter { get; } = new("Letter", 8.5 * 96, 11 * 96);

    public static PaperSize Legal { get; } = new("Legal", 8.5 * 96, 14 * 96);

    public static PaperSize Tabloid { get; } = new("Tabloid", 11 * 96, 17 * 96);

    /// <summary>The sizes offered where no printer says what it takes - saving to PDF.</summary>
    public static IReadOnlyList<PaperSize> Standard { get; } = [A3, A4, A5, B5, Letter, Legal, Tabloid];

    /// <summary>A sheet of <paramref name="width"/> by <paramref name="height"/> millimeters.</summary>
    public static PaperSize Millimeters(string name, double width, double height) =>
        new(name, width * PerMillimeter, height * PerMillimeter);
}
