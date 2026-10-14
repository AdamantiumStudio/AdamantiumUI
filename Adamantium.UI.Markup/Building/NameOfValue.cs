namespace Adamantium.UI.Markup.Building;

/// <summary>A member's name, checked by the compiler, <c>nameof(T.Member)</c>.</summary>
public sealed class NameOfValue(MemberValue member) : BuildValue
{
    /// <summary>The member named.</summary>
    public MemberValue Member { get; } = member;
}
