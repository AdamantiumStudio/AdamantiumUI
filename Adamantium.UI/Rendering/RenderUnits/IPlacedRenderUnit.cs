using Adamantium.Mathematics;
using Adamantium.UI.Rendering.Payloads;

namespace Adamantium.UI.Rendering.RenderUnits;

/// <summary>A unit whose draw is placed by a matrix (<see cref="IPlacedPayload"/>): the render cache tells it what its
/// placement shows now.</summary>
internal interface IPlacedRenderUnit
{
    IPlacedPayload Placed { get; }

    Matrix4x4F PlacedMatrix { get; set; }
}
