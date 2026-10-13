namespace Adamantium.UI.Markup.Building;

/// <summary>An enum value: one member, or several combined as flags.</summary>
public sealed class EnumValue(TypeValue type, IReadOnlyList<string> members) : BuildValue
{
    /// <summary>The enum type.</summary>
    public TypeValue Type { get; } = type;

    /// <summary>The members, combined with <c>|</c>.</summary>
    public IReadOnlyList<string> Members { get; } = members;
}
