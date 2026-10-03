namespace Adamantium.UI.Markup.CodeGeneration;

/// <summary>What a language table offers to <c>{Localize}</c>: its strings, each with the names of the placeholders it
/// fills. Known before the table is compiled, from its base language file.</summary>
public sealed class LanguageTableShape
{
    public LanguageTableShape(string fullName, IReadOnlyDictionary<string, IReadOnlyList<string>> strings)
    {
        FullName = fullName;
        Strings = strings;
    }

    public string FullName { get; }

    /// <summary>Each string's key and the names of the placeholders it fills.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> Strings { get; }
}
