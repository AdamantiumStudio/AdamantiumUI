namespace Adamantium.UI.Core.Media;

/// <summary>Raised and lowered forms the font draws itself, rather than smaller letters moved.</summary>
public enum FontVariants
{
    Normal,

    /// <summary>Superscript (<c>sups</c>).</summary>
    Superscript,

    /// <summary>Subscript (<c>subs</c>).</summary>
    Subscript,

    /// <summary>Ordinal forms, such as 1st and 2º (<c>ordn</c>).</summary>
    Ordinal,

    /// <summary>Scientific inferiors, as in chemical formulas (<c>sinf</c>).</summary>
    Inferior,
}
