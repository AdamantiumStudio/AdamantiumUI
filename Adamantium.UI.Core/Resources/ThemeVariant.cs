using System;
using Adamantium.Core.TypeParsing;

namespace Adamantium.UI.Core.Resources;

/// <summary>Which variant of a theme is in force; a variant changes only palette colors. An open value rather than an
/// enum, so themes can declare their own; <see cref="Light"/>, <see cref="Dark"/> and <see cref="System"/> are named.</summary>
/// <remarks>Whether a theme declares the variant is known only at runtime, and a missing one fails visibly.</remarks>
[TypeParser(typeof(TypeParsers.ThemeVariantParser))]
public readonly struct ThemeVariant : IEquatable<ThemeVariant>
{
    /// <summary>The light variant, by convention. A theme is free not to have one.</summary>
    public static readonly ThemeVariant Light = new("Light");

    /// <summary>The dark variant, by convention. A theme is free not to have one.</summary>
    public static readonly ThemeVariant Dark = new("Dark");

    /// <summary>Follows the OS appearance; an explicit value, so a subtree can opt in under a pinned ancestor. A theme
    /// without light/dark mapping uses its default variant.</summary>
    public static readonly ThemeVariant System = new("System");

    /// <summary>A variant of a theme's own naming - <c>ThemeVariant.Named("Amber")</c>. No engine change needed.</summary>
    public static ThemeVariant Named(string key) => new(key);

    private ThemeVariant(string key) => Key = key;

    /// <summary>The variant's key, as the theme declares it. Null for the default-constructed value, which means
    /// "unspecified" - the state a property is in before anyone sets it.</summary>
    public string Key { get; }

    /// <summary>Nobody has said which variant this is. Distinct from <see cref="System"/>: this one defers to whoever
    /// is asked next (an ancestor, then the theme's default), that one stops and asks the OS.</summary>
    public bool IsUnspecified => string.IsNullOrEmpty(Key);

    /// <summary>Whether this is the follow-the-OS value.</summary>
    public bool FollowsSystem => Equals(System);

    /// <summary>Case-insensitive, so markup may write <c>dark</c> and code <c>ThemeVariant.Dark</c> and mean it.</summary>
    public bool Equals(ThemeVariant other) => string.Equals(Key, other.Key, StringComparison.OrdinalIgnoreCase);

    public override bool Equals(object obj) => obj is ThemeVariant other && Equals(other);

    public override int GetHashCode() => Key == null ? 0 : StringComparer.OrdinalIgnoreCase.GetHashCode(Key);

    public static bool operator ==(ThemeVariant left, ThemeVariant right) => left.Equals(right);

    public static bool operator !=(ThemeVariant left, ThemeVariant right) => !left.Equals(right);

    public override string ToString() => Key ?? "(unspecified)";

    /// <summary>Reads a variant written in markup. An empty string is <see cref="IsUnspecified"/>; anything else is
    /// taken at its word, because a theme may name its variants whatever it likes - the check that it EXISTS belongs
    /// to the theme that is asked for it, which is the only thing that knows.</summary>
    public static ThemeVariant Parse(string text) =>
        string.IsNullOrWhiteSpace(text) ? default : new ThemeVariant(text.Trim());
}
