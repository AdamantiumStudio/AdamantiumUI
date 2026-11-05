using Adamantium.Mathematics;
using Adamantium.UI.Core.Graphics;

namespace Adamantium.UI.Rendering.Payloads;

/// <summary>A draw placed by a matrix on top of its element's world: baked into its instances, or - when a
/// <see cref="Core.Graphics.Placement"/> carries it - riding that placement's transform slot.</summary>
public interface IPlacedPayload
{
    /// <summary>Where the draw sits on top of the element's world; a placement's matrix as it was recorded.</summary>
    Matrix4x4F LocalTransform { get; }

    /// <summary>The placement the matrix moves with, or null for a matrix baked once.</summary>
    Placement Placement { get; }

    /// <summary>The placement's version when it was recorded.</summary>
    long PlacedVersion { get; }

    /// <summary>Marks the draw as moving with <paramref name="placement"/>; called while it is recorded.</summary>
    internal void PlaceBy(Placement placement);
}
