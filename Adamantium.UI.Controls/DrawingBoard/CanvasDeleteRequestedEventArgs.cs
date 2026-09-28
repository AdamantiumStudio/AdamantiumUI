using System;
using System.Collections.Generic;

namespace Adamantium.UI.Controls.DrawingBoard;

/// <summary>Raised before the canvas deletes anything, keyboard included. Set <see cref="Handled"/> to take over, e.g. ask
/// asynchronously and then call <see cref="InfiniteCanvas.DeleteSelection"/>.</summary>
public sealed class CanvasDeleteRequestedEventArgs : EventArgs
{
    public CanvasDeleteRequestedEventArgs(IReadOnlyList<ICanvasItem> items)
    {
        Items = items;
    }

    /// <summary>What would be removed.</summary>
    public IReadOnlyList<ICanvasItem> Items { get; }

    /// <summary>Set by a handler that has taken responsibility for the deletion. The canvas then removes nothing.</summary>
    public bool Handled { get; set; }
}
