namespace Adamantium.UI.Markup.CodeGeneration;

/// <summary>What a binding path can read on a view-model type: its public properties, and those the Adamantium.MVVM
/// generator makes, which another generator cannot see - property X of a [Bindable] field _x, command MCommand of a
/// [Command] method M, or the name the attribute gives it.</summary>
public static class ViewModelMembers
{
    private const string CommandAttribute = "Adamantium.MVVM.CommandAttribute";
    private const string BindableAttribute = "Adamantium.MVVM.BindableAttribute";

    /// <summary>Whether <paramref name="type"/> has <paramref name="name"/> to read; <paramref name="memberType"/> is its
    /// type, null when it is not known - a generated command.</summary>
    public static bool TryFind(IResolvedType type, string name, out IResolvedType memberType)
    {
        if (type.GetMemberByName(name) is { MemberKind: ResolvedMemberKind.Property or ResolvedMemberKind.Field } member &&
            member.IsPublic && !member.IsStatic)
        {
            memberType = member.MemberType;
            return true;
        }

        foreach (var (generated, generatedType) in Generated(type))
        {
            if (generated == name)
            {
                memberType = generatedType;
                return true;
            }
        }

        memberType = null;
        return false;
    }

    /// <summary>The members the Adamantium.MVVM generator makes on <paramref name="type"/> and its base types, with
    /// their types - null for a command.</summary>
    public static IEnumerable<(string Name, IResolvedType Type)> Generated(IResolvedType type)
    {
        for (var declaring = type; declaring != null; declaring = declaring.BaseType)
        {
            foreach (var member in declaring.Members ?? [])
            {
                if (member.MemberKind == ResolvedMemberKind.Method && member.GetAttribute(CommandAttribute) is { } command)
                {
                    yield return (command.NamedArguments.TryGetValue("Name", out var name) && name is string { Length: > 0 } given
                        ? given
                        : member.Name + "Command", null);
                }
                else if (member.MemberKind == ResolvedMemberKind.Field && member.HasAttribute(BindableAttribute) &&
                         PropertyName(member.Name) is { } property)
                {
                    yield return (property, member.MemberType);
                }
            }
        }
    }

    /// <summary>The property the MVVM generator makes of a [Bindable] field: without a leading "m_" or underscores, the
    /// first letter upper-case - "_title" and "m_title" give "Title"; null when that is the field's own name.</summary>
    public static string PropertyName(string fieldName)
    {
        var name = fieldName.StartsWith("m_", StringComparison.Ordinal) ? fieldName.Substring(2) : fieldName;
        name = name.TrimStart('_');
        if (name.Length == 0)
        {
            return null;
        }

        var property = char.ToUpperInvariant(name[0]) + name.Substring(1);
        return property == fieldName ? null : property;
    }
}
