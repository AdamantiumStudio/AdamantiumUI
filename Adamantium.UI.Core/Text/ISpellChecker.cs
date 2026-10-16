using System.Collections.Generic;

namespace Adamantium.UI.Core.Text;

/// <summary>The system's spelling: its dictionaries for the languages installed and the user's own words. A language
/// is a BCP 47 tag; one the system cannot check is no error anywhere.</summary>
public interface ISpellChecker
{
    /// <summary>Whether the system can check text in <paramref name="language"/>.</summary>
    bool Supports(string language);

    /// <summary>The misspelled words of <paramref name="text"/>.</summary>
    IReadOnlyList<TextRange> Check(string text, string language);

    /// <summary>What the misspelled <paramref name="word"/> may have been meant as, the likeliest first.</summary>
    IReadOnlyList<string> Suggest(string word, string language);

    /// <summary>Takes <paramref name="word"/> as spelled right until the application ends.</summary>
    void IgnoreAll(string word, string language);

    /// <summary>Adds <paramref name="word"/> to the user's dictionary, for every application.</summary>
    void AddToDictionary(string word, string language);
}
