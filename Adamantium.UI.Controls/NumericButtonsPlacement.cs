namespace Adamantium.UI.Controls;

/// <summary>Where a <see cref="NumericUpDown"/> puts its two buttons. Which of the pair is decrease and which is
/// increase is a separate question - see <see cref="NumericUpDown.AreButtonsSwapped"/> - so that every placement can be
/// had in either order without doubling the values here.</summary>
public enum NumericButtonsPlacement
{
    /// <summary>One at each end, with the number between them: left and right, or - <see cref="NumericUpDown.Orientation"/>
    /// vertical - over and under it.</summary>
    Split,

    /// <summary>Both to the left of the number, side by side.</summary>
    Left,

    /// <summary>Both to the right of the number, side by side.</summary>
    Right,

    /// <summary>Both over the number, side by side, each half its width.</summary>
    Top,

    /// <summary>Both under the number, side by side, each half its width.</summary>
    Bottom
}
