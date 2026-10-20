namespace Adamantium.UI.Controls;

/// <summary>What a <see cref="ClockFace"/> marks round its edge.</summary>
public enum ClockFaceStyle
{
    /// <summary>The hours in figures and a mark for every minute.</summary>
    Classic,

    /// <summary>A mark for every hour.</summary>
    Minimal,

    /// <summary>The hours in Roman numerals and a mark for every minute.</summary>
    Roman,

    /// <summary>A dot for every hour.</summary>
    Dots,

    /// <summary>Only its labels - the hours or minutes a time picker offers.</summary>
    Labels
}
