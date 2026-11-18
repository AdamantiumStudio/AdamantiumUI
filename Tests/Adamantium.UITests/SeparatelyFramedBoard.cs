using System.Collections.Generic;
using Adamantium.UI.Controls.Canvases;

namespace Adamantium.UITests;

/// <summary>A board that never frames what is selected as one, as a viewer of separate sheets does not.</summary>
internal sealed class SeparatelyFramedBoard : DrawingBoard
{
    protected override bool SharesFrame(IReadOnlyList<ICanvasItem> selection) => false;
}
