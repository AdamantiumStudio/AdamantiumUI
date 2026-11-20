using System.Collections.Generic;

namespace Adamantium.UI.Controls.Canvases;

/// <summary>Many items in or out of a scene as ONE change: an item at a time is a walk of the scene each, and of
/// everything that follows it.</summary>
internal static class CanvasSceneBatch
{
    /// <summary>Whether the scene holds the item - asked where the scene numbered it, rather than searched for.</summary>
    public static bool Holds(this ICanvasScene scene, ICanvasItem item)
    {
        var items = scene.Items;
        var at = item.Order;
        return at >= 0 && at < items.Count && ReferenceEquals(items[at], item);
    }

    /// <summary>Takes <paramref name="gone"/> out, the rest kept in its order; with <paramref name="added"/> put on top.
    /// </summary>
    public static void ResetWithout(this ICanvasScene scene, HashSet<ICanvasItem> gone,
        IReadOnlyList<ICanvasItem> added = null)
    {
        var items = new List<ICanvasItem>(scene.Items.Count + (added?.Count ?? 0));
        foreach (var item in scene.Items)
        {
            if (gone == null || !gone.Contains(item)) items.Add(item);
        }

        // Nothing taken out and nothing put in: the scene is left as it is, and told nothing.
        if (items.Count == scene.Items.Count && added is not { Count: > 0 }) return;

        if (added != null) items.AddRange(added);

        scene.Reset(items);
    }
}
