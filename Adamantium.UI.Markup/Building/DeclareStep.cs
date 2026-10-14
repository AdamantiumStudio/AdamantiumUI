namespace Adamantium.UI.Markup.Building;

/// <summary>Gives a name to a value: a local variable, or a field of the generated class.</summary>
public sealed class DeclareStep(string variable, BuildValue value, bool isField = false) : BuildStep
{
    /// <summary>The name later steps refer to.</summary>
    public string Variable { get; } = variable;

    /// <summary>The value named.</summary>
    public BuildValue Value { get; } = value;

    /// <summary>Whether the name is a field of the generated class rather than a local.</summary>
    public bool IsField { get; } = isField;
}
