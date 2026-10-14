namespace Adamantium.UI.Markup.Building;

/// <summary>Sets an entry of a collection by its key.</summary>
public sealed class IndexAssignStep(BuildValue target, BuildValue key, BuildValue value) : BuildStep
{
    /// <summary>The collection.</summary>
    public BuildValue Target { get; } = target;

    /// <summary>The entry's key.</summary>
    public BuildValue Key { get; } = key;

    /// <summary>The value set.</summary>
    public BuildValue Value { get; } = value;
}
