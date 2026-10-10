using System.Collections.Generic;
using Adamantium.UI.Core;

namespace Adamantium.UI.Rendering;

public partial class RenderCache
{
    /// <summary>The groups this cache holds, as the frame verifier reports them. Reads only what the last draw left; call it
    /// on the drawing thread after the frame.</summary>
    internal IReadOnlyList<GroupView> DescribeGroups()
    {
        var views = new List<GroupView>(_groups.Count);
        foreach (var group in _groups)
        {
            var component = group.Component;
            if (component == null || !_applySnap.TryGetValue(component, out var snap))
            {
                continue;
            }

            var slots = 0;
            foreach (var run in group.Runs)
            {
                slots += run.Count;
            }

            var world = new Rect(0, 0, snap.RenderSize.Width, snap.RenderSize.Height).TransformToAABB(World(component));
            views.Add(new GroupView(component, world, CumulativeClip(component), snap.Opacity, group.Units.Count, slots,
                group.WalkVersion == _walkVersion));
        }

        return views;
    }
}
