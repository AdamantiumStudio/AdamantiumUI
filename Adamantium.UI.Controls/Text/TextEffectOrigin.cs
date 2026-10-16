namespace Adamantium.UI.Controls.Text;

/// <summary>What a <see cref="TextEffect"/>'s transform turns and scales about.</summary>
public enum TextEffectOrigin
{
    /// <summary>Each character about its own center: a turn spins every letter in its place.</summary>
    Glyph,

    /// <summary>The element's own space, as WPF's: a turn swings the characters about the element's top-left corner, or
    /// about the transform's center.</summary>
    Element
}
