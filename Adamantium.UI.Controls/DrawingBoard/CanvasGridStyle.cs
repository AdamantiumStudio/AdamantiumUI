namespace Adamantium.UI.Controls.DrawingBoard;

/// <summary>How <see cref="InfiniteCanvas"/> draws the plane behind its content.
/// <para>The first three values all paint a PLANE and differ in what is on it. <see cref="Transparent"/> is the one that
/// is not like the others: there is no plane at all.</para></summary>
public enum CanvasGridStyle
{
    /// <summary>A plain plane: the ground is painted, the world's axes are marked, and there are no grid marks.</summary>
    None,

    /// <summary>A dot where the lines would cross. Quieter than lines, and the usual choice for drawing on.</summary>
    Dots,

    /// <summary>Full lines both ways - a drafting grid, for work that is measured rather than drawn.</summary>
    Lines,

    /// <summary>Nothing painted behind the content, so the canvas becomes an annotation layer over whatever is beneath; the
    /// ground quad is skipped entirely.</summary>
    Transparent
}
