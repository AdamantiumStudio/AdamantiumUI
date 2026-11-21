namespace Adamantium.UI.Markup.Building;

/// <summary>The value a <see cref="SharedValueStep"/> makes, made the first time it is read.</summary>
public sealed class SharedValue(SharedValueStep step) : BuildValue
{
    /// <summary>What makes the value.</summary>
    public SharedValueStep Step { get; } = step;
}
