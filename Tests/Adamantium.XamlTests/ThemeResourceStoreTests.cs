using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Resources;
using NUnit.Framework;

namespace Adamantium.XamlTests;

// Each marker names a key in its own store: {ThemeResource} theme properties (accent, focus), {ObservableResource} and
// {ResourceReference} the palette. A miss is silent, so it is checked textually.
[TestFixture]
public class ThemeResourceStoreTests
{
    private static readonly Regex FromDictionary =
        new(@"\{\s*(?:ObservableResource|ResourceReference)\s+([A-Za-z0-9_]+)\s*\}", RegexOptions.Compiled);

    private static readonly Regex FromThemeProperties =
        new(@"\{\s*ThemeResource\s+([A-Za-z0-9_]+)\s*\}", RegexOptions.Compiled);

    [TestCase("EditorProTheme")]
    [TestCase("FluentTheme")]
    [TestCase("MacOsTheme")]
    public void AKeyIsAskedFromTheStoreThatHoldsIt(string themeFolder)
    {
        var properties = ThemeProperties();
        Assert.That(properties, Does.Contain("AccentFillColorDefault"), "the theme's own brush properties");

        var folder = Path.Combine(ThemesRoot(), themeFolder);
        var complaints = new List<string>();

        foreach (var file in Directory.EnumerateFiles(folder, "*.auml"))
        {
            var inComment = false;
            foreach (var (raw, number) in File.ReadLines(file).Select((l, i) => (l, i + 1)))
            {
                var line = StripComments(raw, ref inComment);
                if (line.Length == 0) continue;

                foreach (Match use in FromDictionary.Matches(line))
                {
                    if (!properties.Contains(use.Groups[1].Value)) continue;

                    complaints.Add($"{Path.GetFileName(file)}:{number}: '{use.Groups[1].Value}' is a theme PROPERTY, " +
                                   "so it is not in the palette this asks - use {ThemeResource}. The setter gets null " +
                                   "and the brush never appears, silently");
                }

                foreach (Match use in FromThemeProperties.Matches(line))
                {
                    if (properties.Contains(use.Groups[1].Value)) continue;

                    complaints.Add($"{Path.GetFileName(file)}:{number}: '{use.Groups[1].Value}' is not a theme " +
                                   "property, so {ThemeResource} resolves it to nothing - use {ObservableResource}");
                }
            }
        }

        Assert.That(complaints, Is.Empty, string.Join(Environment.NewLine, complaints));
    }

    private static HashSet<string> ThemeProperties()
    {
        // Reflected rather than listed: {ThemeResource} resolves against the registered properties of the theme TYPE,
        // so a list written out here would go stale the moment one was added and quietly stop guarding it.
        return typeof(Theme)
            .GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
            .Where(field => field.FieldType == typeof(AdamantiumProperty))
            .Select(field => ((AdamantiumProperty)field.GetValue(null))?.Name)
            .Where(name => !string.IsNullOrEmpty(name))
            .ToHashSet(StringComparer.Ordinal);
    }

    private static string StripComments(string line, ref bool inComment)
    {
        var kept = new StringBuilder();
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
            if (Directory.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("could not find Adamantium.UI.Themes above " + AppContext.BaseDirectory);
    }
}
