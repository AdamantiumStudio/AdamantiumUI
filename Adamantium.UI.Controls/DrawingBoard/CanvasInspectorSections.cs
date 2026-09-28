using Adamantium.Core.Collections;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Resources;

namespace Adamantium.UI.Controls.DrawingBoard;

/// <summary>The sets in one of these, in the order they were written - see <see cref="CanvasInspectorSections"/>.</summary>
public class CanvasSectionSets : TrackingCollection<CanvasSectionSet>
{
}

/// <summary>What a canvas's inspector shows: <see cref="CanvasSectionSet"/>s in written order, general before particular.
/// An application's own list is laid over the theme default (<see cref="InfiniteCanvas.InspectorSections"/>).</summary>
[PerTarget]
public class CanvasInspectorSections : AdamantiumComponent
{
    /// <summary>The sets. [Content], so one of these is written as the sets it is.</summary>
    [Content]
    public CanvasSectionSets Sets { get; } = new();

    /// <summary>The sections, in order, whose <see cref="CanvasSectionSet.For"/> is empty or matches one of the names (an
    /// item's class and <see cref="ICanvasItem.Sort"/>, or a tool's <see cref="ICanvasTool.Name"/>).</summary>
    public IReadOnlyList<PropertySection> For(IReadOnlyList<string> names)
    {
        var found = new List<PropertySection>();

        foreach (var set in Sets)
        {
            if (set == null || !Matches(set.For, names)) continue;

            foreach (var section in set.Sections) found.Add(section);
        }

        return found;
    }

    /// <summary>This list with another laid over it: a set for something already covered replaces it, a new one is added
    /// after.</summary>
    public CanvasInspectorSections With(CanvasInspectorSections other)
    {
        if (other == null || other.Sets.Count == 0) return this;

        var merged = new CanvasInspectorSections();

        foreach (var set in Sets)
        {
            if (set == null) continue;

            merged.Sets.Add(Named(other, set.For) ?? set);
        }

        foreach (var set in other.Sets)
        {
            if (set == null || Named(this, set.For) != null) continue;

            merged.Sets.Add(set);
        }

        return merged;
    }

    private static CanvasSectionSet Named(CanvasInspectorSections among, string what)
    {
        foreach (var set in among.Sets)
        {
            if (set != null && Same(set.For, what)) return set;
        }

        return null;
    }

    private static bool Matches(string what, IReadOnlyList<string> names)
    {
        if (string.IsNullOrEmpty(what)) return true;
        if (names == null) return false;

        foreach (var name in names)
        {
            if (Same(what, name)) return true;
        }

        return false;
    }

    // BY NAME, ignoring case: these are written by hand in markup, and "image" meaning something else than "Image"
    // would be a difference nobody can see in the file they are looking at.
    private static bool Same(string one, string other) =>
        string.Equals(one ?? string.Empty, other ?? string.Empty, StringComparison.OrdinalIgnoreCase);
}
