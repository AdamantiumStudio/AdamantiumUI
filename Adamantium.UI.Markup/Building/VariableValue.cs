namespace Adamantium.UI.Markup.Building;

/// <summary>A variable, a field, <c>this</c> or a local function, by name.</summary>
public sealed class VariableValue(string name) : BuildValue
{
    /// <summary>The instance being built.</summary>
    public static VariableValue This { get; } = new("this");

    /// <summary>The name as written in the generated code.</summary>
    public string Name { get; } = name;
}
