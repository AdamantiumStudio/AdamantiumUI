namespace Adamantium.UI.Markup.Building;

/// <summary>A local factory function: its steps run each time it is called, not where it is declared.</summary>
public sealed class FunctionStep(TypeValue returnType, string name) : BuildScope
{
    /// <summary>The type the function returns.</summary>
    public TypeValue ReturnType { get; } = returnType;

    /// <summary>The function's name, by which values refer to it.</summary>
    public string Name { get; } = name;
}
