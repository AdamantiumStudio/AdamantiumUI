namespace Adamantium.UI.Core.Resources;

/// <summary>An instance of this type belongs to one owner, so markup never shares one: a keyed resource or a setter
/// value of it is built per target, as if marked <c>x:Shared="False"</c>.</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class PerTargetAttribute : Attribute
{
}
