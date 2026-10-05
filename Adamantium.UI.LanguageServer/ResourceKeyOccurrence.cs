namespace Adamantium.UI.LanguageServer;

/// <summary>A resource key written in markup: where a dictionary declares it, or where a value refers to it.</summary>
public sealed record ResourceKeyOccurrence(string Key, int Start, bool IsDeclaration)
{
    /// <summary>The length of the key as written.</summary>
    public int Length => Key.Length;

    /// <summary>Whether <paramref name="offset"/> falls on the key, its end included.</summary>
    public bool Covers(int offset) => offset >= Start && offset <= Start + Length;
}
