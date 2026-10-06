namespace Adamantium.UI.Core.Media;

/// <summary>Which ligatures text uses; flags, so <c>Default|Discretionary</c> adds to the usual ones.</summary>
[Flags]
public enum FontLigatures
{
    /// <summary>No ligatures, not even fi and fl.</summary>
    None = 0,

    /// <summary>The font's everyday ligatures (<c>liga</c>), such as fi and fl.</summary>
    Standard = 1,

    /// <summary>Ligatures the font forms in context (<c>clig</c>).</summary>
    Contextual = 2,

    /// <summary>Decorative ligatures (<c>dlig</c>), such as ct and st.</summary>
    Discretionary = 4,

    /// <summary>Historical ligatures (<c>hlig</c>).</summary>
    Historical = 8,

    /// <summary>Standard and contextual, as text has them unless told otherwise.</summary>
    Default = Standard | Contextual,
}
