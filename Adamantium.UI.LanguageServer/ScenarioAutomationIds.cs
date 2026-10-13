using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Adamantium.UI.LanguageServer;

/// <summary>The AutomationIds a scenario can name: those the markup of the projects its test project references writes
/// down - AutomationProperties.AutomationId, x:Name and the classes of views. Ids made at run time, from bindings or data,
/// are not among them.</summary>
public static class ScenarioAutomationIds
{
    private static readonly Regex Declared = new(@"\b(?:AutomationProperties\.AutomationId|x:Name|x:Class)\s*=\s*""([^""{]+)""", RegexOptions.Compiled);

    public static IReadOnlyList<string> For(string scenarioPath, Func<string, string> openText)
    {
        if (LanguageProject.Of(scenarioPath) is not { } project)
        {
            return [];
        }

        var ids = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var directory in ReferencedProjectDirs(project.CsprojPath).Append(project.ProjectDir))
        {
            foreach (var file in Directory.EnumerateFiles(directory, "*.auml", SearchOption.AllDirectories))
            {
                if (IsBuildOutput(directory, file))
                {
                    continue;
                }

                foreach (Match match in Declared.Matches(openText(file) ?? File.ReadAllText(file)))
                {
                    var id = match.Groups[1].Value.Trim();
                    ids.Add(id.Contains('.') ? id[(id.LastIndexOf('.') + 1)..] : id);
                }
            }
        }

        return [.. ids];
    }

    private static IEnumerable<string> ReferencedProjectDirs(string csprojPath)
    {
        XDocument project;
        try
        {
            project = XDocument.Load(csprojPath);
        }
        catch (Exception)
        {
            return [];
        }

        var here = Path.GetDirectoryName(csprojPath);
        return project.Descendants()
            .Where(element => element.Name.LocalName == "ProjectReference" && element.Attribute("Include") is { } include && !include.Value.Contains("$("))
            .Select(element => Path.GetDirectoryName(Path.GetFullPath(Path.Combine(here, element.Attribute("Include").Value))))
            .Where(Directory.Exists)
            .ToList();
    }

    private static bool IsBuildOutput(string projectDir, string file)
    {
        var relative = Path.GetRelativePath(projectDir, file).Replace('\\', '/');
        return relative.StartsWith("obj/", StringComparison.OrdinalIgnoreCase) || relative.StartsWith("bin/", StringComparison.OrdinalIgnoreCase);
    }
}
