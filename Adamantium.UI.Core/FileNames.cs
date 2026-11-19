using System.Buffers;
using System.Globalization;
using System.Text;

namespace Adamantium.UI.Core;

/// <summary>Names that come from outside - a download's, a PDF attachment's - made safe to save a file under.</summary>
public static class FileNames
{
    /// <summary>The longest name <see cref="MakeSafe"/> gives, in characters.</summary>
    public const int Limit = 200;

    private static readonly SearchValues<char> Invalid = SearchValues.Create(Path.GetInvalidFileNameChars());

    /// <summary><paramref name="name"/> made safe for a file: its last part only, without characters a name cannot hold
    /// or that change how it reads (control and direction marks), without a device name, at most <see cref="Limit"/>
    /// long; <paramref name="fallback"/> when nothing is left.</summary>
    public static string MakeSafe(string name, string fallback)
    {
        var lastPart = Path.GetFileName((name ?? string.Empty).Replace('\\', '/'));
        var written = new StringBuilder(lastPart.Length);
        foreach (var rune in lastPart.EnumerateRunes())
        {
            if (HidesHowItReads(rune) || (rune.IsBmp && Invalid.Contains((char)rune.Value)))
            {
                written.Append('_');
            }
            else
            {
                written.Append(rune.ToString());
            }
        }

        var safe = TrimEnds(written.ToString());
        if (safe.Length == 0)
        {
            safe = fallback;
        }

        var stem = Path.GetFileNameWithoutExtension(safe);
        var extension = Path.GetExtension(safe);
        // What comes before the first dot is what Windows takes for a device, spaces before the dot aside: NUL.tar.gz too.
        if (OperatingSystem.IsWindows() && IsDeviceName(safe.Split('.')[0].TrimEnd(' ')))
        {
            stem = "_" + stem;
        }

        if (extension.Length > Limit / 2)
        {
            extension = TrimEnds(Cut(extension, Limit / 2));
        }

        if (stem.Length + extension.Length > Limit)
        {
            stem = TrimEnds(Cut(stem, Limit - extension.Length));
        }

        return stem + extension;
    }

    /// <summary><paramref name="name"/> numbered, as a second file of the same name is: "report (2).pdf".</summary>
    public static string Numbered(string name, int number) =>
        $"{Path.GetFileNameWithoutExtension(name)} ({number}){Path.GetExtension(name)}";

    // Windows drops dots and spaces at the end, so a name kept with them is not the name of the file written.
    private static string TrimEnds(string name) => name.Trim().TrimEnd('.', ' ');

    // A cut never splits a character written as two UTF-16 units.
    private static string Cut(string text, int length) =>
        char.IsHighSurrogate(text[length - 1]) ? text[..(length - 1)] : text[..length];

    // Controls, and format characters such as a right-to-left override that shows "evil‮fdp.exe" as "evilexe.pdf".
    private static bool HidesHowItReads(Rune rune) =>
        Rune.IsControl(rune) || Rune.GetUnicodeCategory(rune) == UnicodeCategory.Format;

    private static bool IsDeviceName(string beforeDot)
    {
        var upper = beforeDot.ToUpperInvariant();
        return upper is "CON" or "PRN" or "AUX" or "NUL" or "CONIN$" or "CONOUT$" ||
               (upper.Length == 4 && (upper.StartsWith("COM", StringComparison.Ordinal) ||
                                      upper.StartsWith("LPT", StringComparison.Ordinal)) &&
                upper[3] is >= '0' and <= '9' or '¹' or '²' or '³');
    }
}
