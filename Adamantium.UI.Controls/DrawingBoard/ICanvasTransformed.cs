namespace Adamantium.UI.Controls.DrawingBoard;

/// <summary>An item that can be rotated and skewed about its middle; <see cref="ICanvasItem.Bounds"/> stays the unturned box.</summary>
public interface ICanvasTransformed
{
    /// <summary>How it is turned and leaned.</summary>
    CanvasTransform Transform { get; set; }
}
