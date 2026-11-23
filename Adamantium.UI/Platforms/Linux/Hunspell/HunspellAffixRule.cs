namespace Adamantium.UI.Platforms.Linux.Hunspell;

/// <summary>One line of a PFX or SFX class of an .aff file: what it strips from the stem, what it adds, the flags the
/// form it makes takes on (a suffix that takes another), and when it applies.</summary>
internal sealed class HunspellAffixRule(char flag, bool isPrefix, bool crossProduct, string strip, string add,
    string continuation, HunspellCondition condition)
{
    public char Flag { get; } = flag;

    public bool IsPrefix { get; } = isPrefix;

    /// <summary>Whether a prefix and a suffix of classes marked so may go on the same stem together.</summary>
    public bool CrossProduct { get; } = crossProduct;

    public string Strip { get; } = strip;

    public string Add { get; } = add;

    /// <summary>The flags of the form made, each one character: a suffix of these may go on it too.</summary>
    public string Continuation { get; } = continuation;

    public HunspellCondition Condition { get; } = condition;

    /// <summary>The form this rule makes of <paramref name="stem"/>, or null where it does not apply.</summary>
    public string Apply(string stem)
    {
        if (IsPrefix)
        {
            return stem.StartsWith(Strip, System.StringComparison.Ordinal) && Condition.MatchesStart(stem)
                ? Add + stem[Strip.Length..]
                : null;
        }

        return stem.EndsWith(Strip, System.StringComparison.Ordinal) && Condition.MatchesEnd(stem)
            ? stem[..^Strip.Length] + Add
            : null;
    }
}
