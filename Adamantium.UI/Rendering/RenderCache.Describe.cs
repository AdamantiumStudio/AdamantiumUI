using System.Collections.Generic;
using Adamantium.Mathematics;
using Adamantium.UI.Core;

namespace Adamantium.UI.Rendering;

public partial class RenderCache
{
    /// <summary>The groups in paint order, as the frame verifier reads them. Pure reads of what the last draw left; call it
    /// on the drawing thread after the frame.</summary>
    internal IReadOnlyList<GroupView> DescribeGroups()
    {
        var views = new List<GroupView>(_groups.Count);
        foreach (var group in _groups)
        {
            var component = group.Component ?? (group.Units.Count > 0 ? group.Units[0].Component : null);
            if (component == null)
            {
                continue;
            }

            views.Add(new GroupView(component, group.Units.Count));
        }

        return views;
    }

    /// <summary>The layout the draw reads for <paramref name="component"/>, frozen from the packets.</summary>
    internal bool TryGetFrozen(IUIComponent component, out LayoutSnapshot snapshot) =>
        _applySnap.TryGetValue(component, out snapshot);

    /// <summary>Whether the frozen layout of <paramref name="component"/> was read from the live component because no
    /// packet carried it.</summary>
    internal bool IsReadLive(IUIComponent component) => _readLive.Contains(component);

    /// <summary>The world transform the draw composed for <paramref name="component"/>, if it composed one.</summary>
    internal bool TryGetComposedWorld(IUIComponent component, out Matrix4x4F world) =>
        _worldCache.TryGetValue(component, out world);
}
