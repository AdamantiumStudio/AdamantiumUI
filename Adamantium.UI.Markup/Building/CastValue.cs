namespace Adamantium.UI.Markup.Building;

/// <summary>A value seen as another type.</summary>
public sealed class CastValue(TypeValue type, BuildValue inner, bool innerParenthesized = false) : BuildValue
{
    /// <summary>The type the value is seen as.</summary>
    public TypeValue Type { get; } = type;

    /// <summary>The value.</summary>
    public BuildValue Inner { get; } = inner;

    /// <summary>Whether the generated code puts the value in parentheses of its own.</summary>
    public bool InnerParenthesized { get; } = innerParenthesized;
}
