namespace Adamantium.UI.Controls.Canvases;

/// <summary>What a <see cref="CanvasHistory"/> last did - what its latest <see cref="CanvasHistory.Changed"/> is about.
/// </summary>
public enum CanvasHistoryChange
{
    /// <summary>Nothing yet.</summary>
    None,

    /// <summary>A new step was added: an edit made just now.</summary>
    Pushed,

    /// <summary>A step was taken back.</summary>
    Undone,

    /// <summary>A step taken back was done again.</summary>
    Redone,

    /// <summary>Every step was forgotten.</summary>
    Cleared,
}
