using Adamantium.Mathematics;
using Adamantium.UI.Controls.Panels;
using Adamantium.UI.Core;

namespace Adamantium.UITests;

/// <summary>A window root for tests that stands at <see cref="Position"/> on the desktop: its client points are desktop
/// pixels less that position.</summary>
internal sealed class MovableTestWindowRoot : Grid, IRootVisualComponent
{
    public Vector2<double> PointToClient(PixelPoint point) => new(point.X - Position.X, point.Y - Position.Y);
    public PixelPoint PointToScreen(Vector2<double> point) => new((int)point.X + Position.X, (int)point.Y + Position.Y);
    public PixelPoint Position { get; set; }
    public void AttachContextAndInitialize(IUIContext context) { }
    public double Left { get; set; }
    public double Top { get; set; }
    public string Title { get; set; }
    public double ClientWidth { get; set; }
    public double ClientHeight { get; set; }
    public IUIContext UIContext => null;
}
