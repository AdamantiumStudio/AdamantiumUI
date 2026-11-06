using Adamantium.Mathematics;

namespace Adamantium.UI.Controls.Canvases;

/// <summary>An item that can be rotated and skewed about its middle; <see cref="ICanvasItem.Bounds"/> stays the unturned box.</summary>
public interface ICanvasTransformed
{
    /// <summary>How it is turned and leaned.</summary>
    CanvasTransform Transform { get; set; }

    /// <summary>The world point it is turned about.</summary>
    Vector2<double> Pivot { get; }
}
