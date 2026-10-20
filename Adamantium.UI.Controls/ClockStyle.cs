namespace Adamantium.UI.Controls;

/// <summary>How a <see cref="Clock"/> shows the time: with hands on a face, or in figures.</summary>
public enum ClockStyle
{
    /// <summary>Hands over a face with the hours in figures and a mark for every minute.</summary>
    Classic,

    /// <summary>Hands over a face with a mark for every hour and nothing else.</summary>
    Minimal,

    /// <summary>Hands over a face with the hours in Roman numerals and a mark for every minute.</summary>
    Roman,

    /// <summary>Hands over a face with a dot for every hour.</summary>
    Dots,

    /// <summary>The time in text, as the language writes it, the colon blinking.</summary>
    Text,

    /// <summary>The time in seven-segment figures, drawn rather than set in a font.</summary>
    SevenSegment,

    /// <summary>The time on cards that flip over as a figure changes.</summary>
    Flip
}
