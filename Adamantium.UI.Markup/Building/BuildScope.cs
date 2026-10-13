namespace Adamantium.UI.Markup.Building;

/// <summary>A step that holds further steps, in the order they run.</summary>
public abstract class BuildScope : BuildStep
{
    /// <summary>The steps inside, in order.</summary>
    public List<BuildStep> Steps { get; } = [];
}
