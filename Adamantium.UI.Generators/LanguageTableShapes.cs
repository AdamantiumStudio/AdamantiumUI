using System;
using System.Collections.Generic;
using System.Linq;
using Adamantium.Localization;
using Adamantium.UI.Markup.CodeGeneration;

namespace Adamantium.UI.Generators;

/// <summary>What <c>{Localize}</c> knows of the tables a project's language files make before they are compiled.</summary>
internal static class LanguageTableShapes
{
    /// <summary>The shapes of the tables <paramref name="files"/> make: one for each table with a file in the base
    /// language. A table only translated here, made in another assembly, has none.</summary>
    public static IReadOnlyList<LanguageTableShape> Of(IEnumerable<LanguageFile> files, string rootNamespace, string neutralLanguage)
    {
        var baseLanguage = LanguageFileParser.BaseLanguage(neutralLanguage);
        var result = new List<LanguageTableShape>();
        foreach (var file in files.Where(f => f.Table != null && string.Equals(f.Language, baseLanguage, StringComparison.OrdinalIgnoreCase)))
        {
            var fullName = $"{LanguageFileParser.TableNamespace(file.Folder, rootNamespace)}.{file.Table}";
            if (result.Any(shape => shape.FullName == fullName))
            {
                continue;
            }

            var strings = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
            foreach (var entry in file.Entries)
            {
                strings[entry.Key] = LanguageFileParser.PlaceholdersOf(entry);
            }

            result.Add(new LanguageTableShape(fullName, strings));
        }

        return result;
    }
}
