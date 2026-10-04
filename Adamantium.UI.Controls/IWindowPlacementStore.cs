namespace Adamantium.UI.Controls;

/// <summary>Where windows' places are kept between runs. The application registers one that keeps them in a file of the
/// user's application data; register another to keep them elsewhere.</summary>
public interface IWindowPlacementStore
{
    /// <summary>The placement kept under <paramref name="key"/>, or null.</summary>
    WindowPlacement Load(string key);

    void Save(string key, WindowPlacement placement);
}
