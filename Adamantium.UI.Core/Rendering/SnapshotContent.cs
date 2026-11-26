namespace Adamantium.UI.Core.Rendering;

/// <summary>What of an element a region snapshot draws (<see cref="IVisualRenderer"/>).</summary>
public enum SnapshotContent
{
    /// <summary>The element and all it holds.</summary>
    Subtree,

    /// <summary>The element's own drawing, without what it holds - its shadow and glow with it.</summary>
    Own,

    /// <summary>Its outer shadows and glows alone, which lie under its fills.</summary>
    BandsUnder,

    /// <summary>Its inner shadows and glows alone, which lie over its fills and under its text.</summary>
    BandsOver,

    /// <summary>Its text's glyph effects alone - outlines, shadows, glows, reflections - which lie over its fills and
    /// under its glyphs.</summary>
    GlyphEffects,
}
