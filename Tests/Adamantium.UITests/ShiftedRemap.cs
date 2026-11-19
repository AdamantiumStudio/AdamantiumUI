using System.Collections.Generic;
using Adamantium.Mathematics;
using Adamantium.UI.Controls.Canvases;
using Adamantium.UI.Core;

namespace Adamantium.UITests;

/// <summary>A plane laid out anew by a shift: each item named has a new copy, its places shifted along; the rest are gone.
/// </summary>
internal sealed class ShiftedRemap(Dictionary<ICanvasItem, ICanvasItem> copies, Vector2<double> shift) : ICanvasRemap
{
    public ICanvasItem ItemFor(ICanvasItem was) => copies.GetValueOrDefault(was);

    public Rect BoxFor(ICanvasItem was, Rect box) => new(box.X + shift.X, box.Y + shift.Y, box.Width, box.Height);
}
