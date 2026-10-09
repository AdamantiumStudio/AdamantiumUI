using System.ComponentModel.DataAnnotations;

namespace Adamantium.UI.Sandbox.ViewModels;

/// <summary>Which topic the text tab is showing: one at a time, each with the room it needs.</summary>
public enum TextStand
{
    /// <summary>Size, trimming, and runs of one text with their own colors, backgrounds and lines.</summary>
    [Display(Name = "Basics")]
    Basics,

    /// <summary>Text boxes: one line, placeholder, read-only, several lines.</summary>
    [Display(Name = "Editing")]
    Editing,

    /// <summary>Weights, widths and slants of a family, synthesized faces, fonts substituted for missing characters.</summary>
    [Display(Name = "Faces")]
    Faces,

    /// <summary>Emoji sequences, color glyphs from images, SVG and paint graphs, palettes.</summary>
    [Display(Name = "Color and emoji")]
    Color,

    /// <summary>Typography by name, features by tag, the alternates of a glyph.</summary>
    [Display(Name = "OpenType")]
    OpenType,

    /// <summary>A variable font's axes, optical size, values on the axes, and axes in motion.</summary>
    [Display(Name = "Variable fonts")]
    Variable,

    /// <summary>Scripts of other directions and shapes: Hebrew, Arabic, Urdu in Nastaliq, Church Slavonic, Old
    /// Cyrillic, and the initials of the liturgical books.</summary>
    [Display(Name = "Scripts")]
    Scripts,

    /// <summary>Paragraphs broken into lines a line or a paragraph at a time, words hyphenated by the patterns of their
    /// language or at soft hyphens.</summary>
    [Display(Name = "Paragraph")]
    Paragraph,
}
