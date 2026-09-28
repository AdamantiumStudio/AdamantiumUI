namespace Adamantium.UI.Controls.DrawingBoard;

/// <summary>A drawing object as the application holds it, the counterpart of <see cref="ICanvasNode"/>: plain data the
/// canvas draws and keeps in step both ways.</summary>
public interface ICanvasObject : ICanvasPlaced
{
    double Height { get; set; }

    /// <summary>WHAT IT IS. A description the canvas understands - <see cref="ICanvasShapeDescription"/> - draws as
    /// that shape; anything else is the application's own object and is drawn by the template chosen for its type, in a
    /// control the canvas builds.</summary>
    object Content { get; set; }
}
