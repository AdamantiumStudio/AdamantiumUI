using Adamantium.UI.Core;

namespace Adamantium.UI.Controls.Canvases;

/// <summary>Where things went when the plane was laid out anew under them - a document's sheets turned or moved - so the
/// history can follow (<see cref="CanvasHistory.Remap"/>) instead of being forgotten.</summary>
public interface ICanvasRemap
{
    /// <summary>The item that now stands for one the history holds: itself when it stayed, its new copy when it was put
    /// down again; null when nothing does, and the steps that hold it cannot follow.</summary>
    ICanvasItem ItemFor(ICanvasItem was);

    /// <summary>Where a box the history holds for an item lies now, in the plane's units.</summary>
    Rect BoxFor(ICanvasItem was, Rect box);
}
