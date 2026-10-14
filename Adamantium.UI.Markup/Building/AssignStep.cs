namespace Adamantium.UI.Markup.Building;

/// <summary>Sets a field or property through its setter.</summary>
public sealed class AssignStep(BuildValue target, string member, BuildValue value) : BuildStep
{
    /// <summary>The object or type the member belongs to; null for the instance being built, written without
    /// <c>this.</c>.</summary>
    public BuildValue Target { get; } = target;

    /// <summary>The member's name.</summary>
    public string Member { get; } = member;

    /// <summary>The value set.</summary>
    public BuildValue Value { get; } = value;
}
