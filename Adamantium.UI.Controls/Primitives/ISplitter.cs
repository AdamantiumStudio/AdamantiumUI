namespace Adamantium.UI.Controls.Primitives;

/// <summary>A thumb that moves the boundary between two neighbors, for automation to move without a drag.</summary>
internal interface ISplitter
{
    /// <summary>True when the boundary runs down and moves across; false when it moves up and down.</summary>
    bool MovesAcross { get; }

    /// <summary>Whether there are two neighbors to move the boundary between.</summary>
    bool CanMoveSplit { get; }

    /// <summary>Moves the boundary by <paramref name="delta"/> of the splitter's own units, as a drag that long would,
    /// kept within what the neighbors allow.</summary>
    void MoveSplit(double delta);
}
