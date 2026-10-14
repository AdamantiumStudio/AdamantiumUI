namespace Adamantium.UI.Core.Markup;

/// <summary>What became of one saved markup file.</summary>
public sealed class MarkupReloadReport(string file, bool applied, int instances, IReadOnlyList<string> messages)
{
    /// <summary>The file's full path.</summary>
    public string File { get; } = file;

    /// <summary>Whether the saved version was applied; when it was not, the running one stays.</summary>
    public bool Applied { get; } = applied;

    /// <summary>How many live views, windows and controls took the change.</summary>
    public int Instances { get; } = instances;

    /// <summary>Why it was not applied, or what of it could not be.</summary>
    public IReadOnlyList<string> Messages { get; } = messages;
}
