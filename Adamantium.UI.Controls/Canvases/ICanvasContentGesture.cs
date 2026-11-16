using Adamantium.Mathematics;
using Adamantium.UI.Core;

namespace Adamantium.UI.Controls.Canvases;

/// <summary>What the plane's own content offers a press of the <see cref="SelectTool"/> where there is no item: text to
/// select in a document shown under the plane, say. Asked after the items and before a band, as the tool asks a socket
/// whether a press pulls a wire - the knowledge of what the content is stays here.</summary>
public interface ICanvasContentGesture
{
    /// <summary>Whether a press it took is still held.</summary>
    bool IsBusy { get; }

    /// <summary>What the pointer wears at <paramref name="world"/>, where no item is: the content's own cursor, or null
    /// where it offers nothing.</summary>
    Cursor CursorAt(InfiniteCanvas canvas, Vector2<double> world);

    /// <summary>Offered a press where no item is; true takes it, and the moves and the release that follow.</summary>
    bool Press(InfiniteCanvas canvas, CanvasPointerEventArgs e);

    /// <summary>The pointer moved while the press it took is held.</summary>
    void Move(InfiniteCanvas canvas, CanvasPointerEventArgs e);

    /// <summary>The press it took is let go.</summary>
    void Release(InfiniteCanvas canvas, CanvasPointerEventArgs e);

    /// <summary>The press it took is called off - another tool, a lost capture.</summary>
    void Cancel(InfiniteCanvas canvas);
}
