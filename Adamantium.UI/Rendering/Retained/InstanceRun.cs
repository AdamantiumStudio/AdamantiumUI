namespace Adamantium.UI.Rendering.Retained;

/// <summary>A run of one geometry's instances drawn together: <paramref name="Count"/> of them from
/// <paramref name="First"/> in <paramref name="Segment"/>'s instance buffer.</summary>
internal readonly record struct InstanceRun(KeySegment Segment, uint First, uint Count);
