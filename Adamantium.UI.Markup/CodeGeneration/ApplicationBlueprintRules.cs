namespace Adamantium.UI.Markup.CodeGeneration;

/// <summary>What the build and the editor both say about a project's application blueprints.</summary>
public static class ApplicationBlueprintRules
{
    /// <summary>The element a blueprint file has at its root.</summary>
    public const string RootName = "ApplicationBlueprint";

    /// <summary>The problem with a project holding more than one blueprint, naming them all.</summary>
    public static string TooMany(IReadOnlyCollection<string> files) =>
        $"An application has one blueprint, and this project holds {files.Count}: {string.Join(", ", files)}.";
}
