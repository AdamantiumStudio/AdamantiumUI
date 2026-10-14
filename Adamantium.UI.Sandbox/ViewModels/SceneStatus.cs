namespace Adamantium.UI.Sandbox.ViewModels;

/// <summary>Where the scene page's universe and its model stand.</summary>
public enum SceneStatus
{
    Starting,
    StartFailed,
    Ready,
    NotReady,
    Loading,
    Loaded,
    Failed,
}
