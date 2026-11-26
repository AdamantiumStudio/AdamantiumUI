using Adamantium.ProceduralGeometry;
using Adamantium.UI.Controls.Base;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Graphics;

namespace Adamantium.UITests.Printing;

/// <summary>An element that shows a live surface, as a 3D view does.</summary>
internal sealed class SurfaceView : MeasurableUIComponent
{
    protected override void OnRender(IDrawingContext context) =>
        context.ForControl(this).DrawImage(new SurfaceImage(), null, new Rect(0, 0, ActualWidth, ActualHeight),
            new CornerRadius(0));
}
