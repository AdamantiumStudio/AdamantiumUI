using System.Reflection;

namespace Adamantium.UI.Markup.CodeGeneration.Reflection;

public class ReflectionResolvedMember : IResolvedMember
{
    private readonly MemberInfo _member;
    private object[] _attributes;
    private IReadOnlyList<string> _parameterNames;

    public ReflectionResolvedMember(MemberInfo member, IResolvedType declaringType)
    {
        _member = member;
        DeclaringType = declaringType;
    }

    public string Name => _member.Name;

    public IResolvedType MemberType => _member switch
    {
        PropertyInfo p => ReflectionResolvedType.Of(p.PropertyType),
        FieldInfo f => ReflectionResolvedType.Of(f.FieldType),
        MethodInfo m => ReflectionResolvedType.Of(m.ReturnType),
        EventInfo e => ReflectionResolvedType.Of(e.EventHandlerType),
        _ => null
    };

    public IResolvedType DeclaringType { get; }

    private object[] Attributes => _attributes ??= _member.GetCustomAttributes(false);

    public bool HasAttribute(string attributeMetadataName)
    {
        foreach (var attribute in Attributes)
        {
            if (attribute.GetType().FullName == attributeMetadataName) return true;
        }

        return false;
    }

    public IResolvedAttribute GetAttribute(string attributeMetadataName)
    {
        foreach (var attribute in Attributes)
        {
            if (attribute is Attribute attr && attr.GetType().FullName == attributeMetadataName) return new ReflectionResolvedAttribute(attr);
        }

        return null;
    }

    public bool HasSetter() => _member switch
    {
        PropertyInfo p => p.SetMethod is { IsPublic: true },
        FieldInfo f => f is { IsInitOnly: false, IsPublic: true },
        _ => false
    };

    public bool IsStatic => _member switch
    {
        FieldInfo f => f.IsStatic,
        PropertyInfo p => (p.GetMethod ?? p.SetMethod)?.IsStatic == true,
        MethodBase m => m.IsStatic,
        _ => false
    };

    public bool IsPublic => _member switch
    {
        FieldInfo f => f.IsPublic,
        PropertyInfo p => p.GetMethod?.IsPublic == true,
        MethodBase m => m.IsPublic,
        _ => false
    };

    public IReadOnlyList<string> ParameterNames =>
        _parameterNames ??= _member is MethodBase method ? method.GetParameters().Select(p => p.Name).ToArray() : [];

    public ResolvedMemberKind MemberKind => _member.MemberType switch
    {
        MemberTypes.Field => ResolvedMemberKind.Field,
        MemberTypes.Property => ResolvedMemberKind.Property,
        MemberTypes.Method => ResolvedMemberKind.Method,
        MemberTypes.Event => ResolvedMemberKind.Event,
        _ => ResolvedMemberKind.Unknown
    };
}
