using System;
using Adamantium.Core.Collections;

namespace Adamantium.UI.Controls.Canvases;

/// <summary>The tools an <see cref="InfiniteCanvas"/> offers - see <see cref="InfiniteCanvas.Tools"/>.</summary>
public class CanvasTools : TrackingCollection<ICanvasTool>
{
    protected override void CheckItems(ReadOnlySpan<ICanvasTool> range) => ThrowIfAnyNull(range);
}
