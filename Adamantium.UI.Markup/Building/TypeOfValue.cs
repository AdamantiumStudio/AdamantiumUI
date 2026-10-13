namespace Adamantium.UI.Markup.Building;

/// <summary>A type as a value, <c>typeof(T)</c>.</summary>
public sealed class TypeOfValue(TypeValue type) : BuildValue
{
    /// <summary>The type.</summary>
    public TypeValue Type { get; } = type;
}
