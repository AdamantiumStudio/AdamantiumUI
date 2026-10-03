namespace Adamantium.UI.LanguageServer;

/// <summary>A string of a language table: its key, the names of its placeholders, its base text when known, and - for
/// one written in cases - the placeholder whose value chooses the case, whether that is a number, and the cases.</summary>
public sealed record LanguageStringInfo(string Key, IReadOnlyList<string> Parameters, string Text, string Chooser = null,
    bool ByNumber = false, IReadOnlyList<string> Cases = null);
