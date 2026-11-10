namespace Adamantium.UI.Core.Media;

/// <summary>What of a soft mask's picture lets the group through: its coverage, or how light it is.</summary>
public enum MaskKind
{
    /// <summary>The picture's alpha: where it is drawn, the group shows.</summary>
    Alpha,

    /// <summary>The picture's luminosity, drawn over <see cref="DrawingGroupOptions.MaskBackdrop"/>: white shows the
    /// group, black hides it.</summary>
    Luminosity,
}
