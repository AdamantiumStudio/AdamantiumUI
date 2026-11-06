using Adamantium.Mathematics;

namespace Adamantium.UI.Rendering.RenderUnits;

/// <summary>A unit that can draw nothing but a stroke - a line, or a geometry with no fill - and so can hand that stroke
/// to a <see cref="StrokeBatchCollector"/> instead of drawing it itself.</summary>
internal interface IStrokeOnlyUnit
{
    /// <summary>Its stroke while the stroke is all it draws; null otherwise.</summary>
    GpuStrokeRenderComponent Stroke { get; }

    /// <summary>What a record bakes on top of the element's bake: the draw's own local transform, unless a placement
    /// carries it in a slot of its own.</summary>
    Matrix4x4F PlaceBaked(Matrix4x4F world);
}
