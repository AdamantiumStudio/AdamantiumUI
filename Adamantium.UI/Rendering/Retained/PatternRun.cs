namespace Adamantium.UI.Rendering.Retained;

/// <summary>A run of one geometry's pattern or noise instances, all of one <paramref name="Kind"/>: every kind is a pass
/// of its own, so a run ends where the kind changes.</summary>
internal readonly record struct PatternRun(KeySegment Segment, uint First, uint Count, int Kind);
