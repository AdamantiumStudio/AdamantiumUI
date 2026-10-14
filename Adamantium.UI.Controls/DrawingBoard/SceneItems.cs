using System;
using System.Runtime.CompilerServices;

namespace Adamantium.UI.Controls.DrawingBoard;

internal interface ISceneFollower
{
    void StopFollowing(ICanvasScene scene);
}

internal static class SceneItems
{
    private static readonly ConditionalWeakTable<ICanvasScene, Kept> Scenes = new();

    public static ICanvasItem Of(ICanvasScene scene, ICanvasPlaced model) =>
        Scenes.TryGetValue(scene, out var kept) && kept.Items.TryGetValue(model, out var item) ? item : null;

    public static void Keep(ICanvasScene scene, ICanvasPlaced model, ICanvasItem item) =>
        Scenes.GetOrCreateValue(scene).Items.AddOrUpdate(model, item);

    // ONE FOLLOWER PER SCENE. The items are shared, so a canvas left but not yet torn down would go on showing - and
    // then discarding - the controls of the one that replaced it.
    public static void Follow(ICanvasScene scene, ISceneFollower follower)
    {
        var kept = Scenes.GetOrCreateValue(scene);

        if (kept.Follower != null && kept.Follower.TryGetTarget(out var before) && !ReferenceEquals(before, follower))
        {
            before.StopFollowing(scene);
        }

        kept.Follower = new WeakReference<ISceneFollower>(follower);
    }

    private sealed class Kept
    {
        public readonly ConditionalWeakTable<ICanvasPlaced, ICanvasItem> Items = new();
        public WeakReference<ISceneFollower> Follower;
    }
}
