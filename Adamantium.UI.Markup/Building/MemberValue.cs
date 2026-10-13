namespace Adamantium.UI.Markup.Building;

/// <summary>A field or property of an object, or a static one of a <see cref="TypeValue"/>.</summary>
public sealed class MemberValue(BuildValue owner, string member) : BuildValue
{
    /// <summary>The object or type the member belongs to; null for the instance being built, written without
    /// <c>this.</c>.</summary>
    public BuildValue Owner { get; } = owner;

    /// <summary>The member's name.</summary>
    public string Member { get; } = member;
}
