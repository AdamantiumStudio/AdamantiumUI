namespace Adamantium.UI.Input;

/// <summary>Where the drag ghost stands against the cursor.</summary>
public enum DragGhostAnchor
{
    /// <summary>Below and right of the cursor, by <see cref="DragDropOptions.GhostCursorOffset"/>, so it never hides the
    /// drop point.</summary>
    Cursor,

    /// <summary>Held by the point it was grabbed at, as if the element itself were carried.</summary>
    Grab
}
