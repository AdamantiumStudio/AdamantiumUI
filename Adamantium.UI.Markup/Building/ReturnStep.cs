namespace Adamantium.UI.Markup.Building;

/// <summary>Ends a local function with what it built.</summary>
public sealed class ReturnStep(BuildValue value) : BuildStep
{
    /// <summary>The value returned.</summary>
    public BuildValue Value { get; } = value;
}
