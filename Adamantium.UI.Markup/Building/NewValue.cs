namespace Adamantium.UI.Markup.Building;

/// <summary>A new instance of a type, made with the given constructor arguments.</summary>
public sealed class NewValue(TypeValue type, params BuildValue[] arguments) : BuildValue
{
    /// <summary>The type to create.</summary>
    public TypeValue Type { get; } = type;

    /// <summary>The constructor's arguments.</summary>
    public IReadOnlyList<BuildValue> Arguments { get; } = arguments;
}
