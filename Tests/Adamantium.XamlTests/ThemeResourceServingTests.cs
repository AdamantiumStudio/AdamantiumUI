using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace Adamantium.XamlTests;

// Color-typed slots (GradientStop.Color) only reference keys declared As="Color"; checked textually, like
// ThemeTemplateBindingTests.
[TestFixture]
public class ThemeResourceServingTests
{
    private static readonly Regex Declaration =
        new(@"<PaletteColor\b[^>]*\bKey\s*=\s*""([^""]+)""[^>]*>", RegexOptions.Compiled);
    private static readonly Regex ServedAsColor = new(@"\bAs\s*=\s*""Color""", RegexOptions.Compiled);

    private static readonly Regex ColorSlot =
        new(@"\b(?:Color|Color1|Color2)\s*=\s*""\{\s*(?:ObservableResource|ResourceReference)\s+([A-Za-z0-9_]+)\s*\}""",
            RegexOptions.Compiled);

    private static readonly Regex BrushSlot =
        new(@"\b(?:Background|Foreground|BorderBrush|Fill|Stroke|OverlayBrush|IndicatorBrush|IndicatorStroke|ActiveBrush|PreviewBrush|SelectionIndicatorBrush)\s*=\s*""\{\s*(?:ObservableResource|ResourceReference)\s+([A-Za-z0-9_]+)\s*\}""",
            RegexOptions.Compiled);

    [TestCase("GraphiteTheme")]
    [TestCase("FluentTheme")]
    [TestCase("MacOsTheme")]
    public void AKeyIsUsedInTheSlotItIsServedFor(string themeFolder)
    {
        var folder = Path.Combine(ThemesRoot(), themeFolder);
        var servedAsColor = new Dictionary<string, bool>();

        foreach (var file in Directory.EnumerateFiles(folder, "*.auml"))
        {
            var inComment = false;
            foreach (var raw in File.ReadLines(file))
            {
                var line = StripComments(raw, ref inComment);
                foreach (Match declaration in Declaration.Matches(line))
                    servedAsColor[declaration.Groups[1].Value] = ServedAsColor.IsMatch(declaration.Value);
            }
        }

        Assert.That(servedAsColor, Is.Not.Empty, "the theme's palette should have been found");

        var complaints = new List<string>();

        foreach (var file in Directory.EnumerateFiles(folder, "*.auml"))
        {
            var inComment = false;
            foreach (var (raw, number) in File.ReadLines(file).Select((l, i) => (l, i + 1)))
            {
                var line = StripComments(raw, ref inComment);
                if (line.Length == 0) continue;

                foreach (Match use in ColorSlot.Matches(line))
                {
                    var key = use.Groups[1].Value;
                    // A key this theme does not declare is answered by a borrowed palette; that is a different question.
                    if (!servedAsColor.TryGetValue(key, out var asColor) || asColor) continue;
                    complaints.Add($"{Path.GetFileName(file)}:{number}: '{key}' is served as a BRUSH but sits in a " +
                                   "color slot - this throws inside the render walk and blanks the whole scene");
                }

                foreach (Match use in BrushSlot.Matches(line))
                {
                    var key = use.Groups[1].Value;
                    if (!servedAsColor.TryGetValue(key, out var asColor) || !asColor) continue;
                    complaints.Add($"{Path.GetFileName(file)}:{number}: '{key}' is served as a COLOR but sits in a " +
                                   "brush slot");
                }
            }
        }

        Assert.That(complaints, Is.Empty, string.Join(Environment.NewLine, complaints));
    }

    private static string StripComments(string line, ref bool inComment)
    {
        var kept = new System.Text.StringBuilder();
        var i = 0;

        while (i < line.Length)
        {
            if (inComment)
            {
                var end = line.IndexOf("-->", i, StringComparison.Ordinal);
                if (end < 0) break;
                inComment = false;
                i = end + 3;
                continue;
            }

            var start = line.IndexOf("<!--", i, StringComparison.Ordinal);
            if (start < 0) { kept.Append(line, i, line.Length - i); break; }

            kept.Append(line, i, start - i);
            inComment = true;
            i = start + 4;
        }

        return kept.ToString();
    }

    private static string ThemesRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, "Adamantium.UI.Themes");
            if (Directory.Exists(Path.Combine(candidate, "FluentTheme"))) return candidate;
            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("could not find Adamantium.UI.Themes above " + AppContext.BaseDirectory);
    }
}
