using System;
using System.Globalization;

namespace Adamantium.UI.Controls.Panels;

/// <summary>How much of its row a pane takes: fixed pixels or a weight of what is left. One number with a mode, like a Grid
/// length, so there is no second value to drift.</summary>
public enum PaneUnit
{
    /// <summary>A weight in whatever is left after the fixed panes have taken theirs (a Grid's star).</summary>
    Star,

    /// <summary>Exactly this many pixels, along the row's own axis.</summary>
    Pixel,

    /// <summary>As much as the pane ITSELF needs - its desired extent, measured. What a COLLAPSED pane takes: it is
    /// shrunk to its own tab strip, and how tall a strip is is not a number anyone should be typing.</summary>
    Auto
}

/// <summary>A pane's length in its row - see <see cref="PaneUnit"/>.</summary>
public readonly struct PaneLength : IEquatable<PaneLength>
{
    public PaneLength(double value, PaneUnit unit = PaneUnit.Star)
    {
        Value = double.IsNaN(value) || value < 0 ? 0 : value;
        Unit = unit;
    }

    public double Value { get; }

    public PaneUnit Unit { get; }

    public bool IsPixel => Unit == PaneUnit.Pixel;

    public bool IsStar => Unit == PaneUnit.Star;

    public bool IsAuto => Unit == PaneUnit.Auto;

    /// <summary>One share of the leftovers - what a pane takes when nobody said anything about it.</summary>
    public static PaneLength Star => new(1, PaneUnit.Star);

    /// <summary>As much as the pane needs and no more.</summary>
    public static PaneLength Auto => new(0, PaneUnit.Auto);

    public static PaneLength Pixels(double value) => new(value, PaneUnit.Pixel);

    public static PaneLength Stars(double weight) => new(weight, PaneUnit.Star);

    /// <summary>Parses <c>"240"</c> (pixels), <c>"*"</c>, <c>"2*"</c> - the same spelling a Grid length uses, so markup
    /// says the same thing in both places.</summary>
    public static PaneLength Parse(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return Star;

        text = text.Trim();
        if (text == "*") return Star;

        // ToString writes "Auto", so Parse has to read it back - a round trip that loses a collapsed panel's length is
        // a saved layout that springs open when it is loaded.
        if (text.Equals("Auto", StringComparison.OrdinalIgnoreCase)) 
            return Auto;
        
        if (text.EndsWith('*'))
        {
            var weight = text[..^1];
            return weight.Length == 0
                ? Star
                : Stars(double.Parse(weight, CultureInfo.InvariantCulture));
        }

        return Pixels(double.Parse(text, CultureInfo.InvariantCulture));
    }

    public bool Equals(PaneLength other) => Unit == other.Unit && Value.Equals(other.Value);

    public override bool Equals(object obj) => obj is PaneLength other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Value, (int)Unit);

    public static bool operator ==(PaneLength left, PaneLength right) => left.Equals(right);

    public static bool operator !=(PaneLength left, PaneLength right) => !left.Equals(right);

    public override string ToString() => Unit switch
    {
        PaneUnit.Auto => "Auto",
        PaneUnit.Pixel => Value.ToString(CultureInfo.InvariantCulture),
        _ => Value == 1 ? "*" : Value.ToString(CultureInfo.InvariantCulture) + "*"
    };
}
