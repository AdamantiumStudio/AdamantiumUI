using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace Adamantium.UI.Markup.CodeGeneration.Reflection;

public class ReflectionResolvedType : IResolvedType
{
    private const string DataTemplateSetTypeName = "Adamantium.UI.Core.Templates.DataTemplateSet";

    private static readonly Dictionary<Type, string> Keywords = new()
    {
        [typeof(bool)] = "bool",
        [typeof(byte)] = "byte",
        [typeof(sbyte)] = "sbyte",
        [typeof(char)] = "char",
        [typeof(decimal)] = "decimal",
        [typeof(double)] = "double",
        [typeof(float)] = "float",
        [typeof(int)] = "int",
        [typeof(uint)] = "uint",
        [typeof(long)] = "long",
        [typeof(ulong)] = "ulong",
        [typeof(short)] = "short",
        [typeof(ushort)] = "ushort",
        [typeof(object)] = "object",
        [typeof(string)] = "string",
    };

    private static readonly Dictionary<string, Type> KeywordTypes =
        Keywords.ToDictionary(pair => pair.Value, pair => pair.Key, StringComparer.Ordinal);

    private static readonly ConditionalWeakTable<Type, ReflectionResolvedType> Shared = new();

    /// <summary>The built-in type a C# keyword names in a plan (<c>int</c>, <c>object</c>), or null: the same table
    /// <see cref="QualifiedName"/> prints them by, read the other way.</summary>
    public static Type TypeOfKeyword(string keyword) => keyword != null && KeywordTypes.TryGetValue(keyword, out var type) ? type : null;

    private readonly Type _type;

    // What reflection answers about a type never changes, and every call of it allocates; each answer is kept.
    private string _name;
    private string _namespace;
    private string _fullName;
    private string _assemblyName;
    private string _qualifiedName;
    private Type[] _interfaces;
    private object[] _attributes;
    private IResolvedProperty[] _properties;
    private IResolvedMember[] _allMembers;
    private HashSet<string> _assignableTo;
    // Shared by every thread: an int is written whole, a nullable is not. -1 is "not worked out yet".
    private int _entityType = -1;
    private ConcurrentDictionary<string, IResolvedMember> _members;

    public ReflectionResolvedType(Type type) => _type = type;

    /// <summary>The one wrapper of <paramref name="type"/>, whose answers are kept across resolvers.</summary>
    public static ReflectionResolvedType Of(Type type) => Shared.GetValue(type, static t => new ReflectionResolvedType(t));

    /// <summary>The underlying runtime type (used by the instantiator to <c>Activator.CreateInstance</c>).</summary>
    public Type ClrType => _type;

    public string Name => _name ??= _type.Name;
    public string Namespace => _namespace ??= _type.Namespace ?? string.Empty;
    public string AssemblyName => _assemblyName ??= _type.Assembly.GetName().Name;
    public string FullName => _fullName ??= FullNameOf(_type);
    /// <summary>The name as Roslyn's fully qualified format writes it, so a plan made through reflection prints as the
    /// generator's does: keywords for the built-in types, nested types through a dot, type arguments spelled out.</summary>
    public string QualifiedName => _qualifiedName ??= QualifiedNameOf(_type);

    public bool IsNamedType => true;
    public bool IsGenericType => _type.IsGenericType;

    public bool IsCreatable => !_type.IsAbstract && !_type.IsInterface &&
                               (_type.IsValueType || _type.GetConstructor(Type.EmptyTypes) != null);

    public IEnumerable<IResolvedType> TypeArguments =>
        _type.GetGenericArguments().Select(t => (IResolvedType)Of(t));

    public IResolvedType BaseType => _type.BaseType != null ? Of(_type.BaseType) : null;

    public IEnumerable<IResolvedMember> Members => _allMembers ??=
        _type.GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
            .Where(m => m.MemberType is MemberTypes.Property or MemberTypes.Field or MemberTypes.Method)
            .Select(m => (IResolvedMember)new ReflectionResolvedMember(m, this))
            .ToArray();

    private Type[] Interfaces => _interfaces ??= _type.GetInterfaces();

    private object[] Attributes => _attributes ??= _type.GetCustomAttributes(false);

    public bool IsAssignableTo(string fullName) => AssignableTo.Contains(fullName);

    private HashSet<string> AssignableTo => _assignableTo ??= BuildAssignableTo();

    private HashSet<string> BuildAssignableTo()
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        for (var t = _type; t != null; t = t.BaseType)
        {
            names.Add(Of(t).FullName);
        }

        foreach (var i in Interfaces)
        {
            names.Add(Of(i).FullName);
        }

        return names;
    }

    public bool InheritsFrom(string baseTypeName)
    {
        for (var t = _type.BaseType; t != null; t = t.BaseType)
            if (Of(t).FullName == baseTypeName) return true;
        return false;
    }

    public bool HasAttribute(string attributeName)
    {
        foreach (var attribute in Attributes)
        {
            if (attribute.GetType().FullName?.EndsWith(attributeName, StringComparison.Ordinal) ?? false) return true;
        }

        return false;
    }

    public IResolvedAttribute GetAttribute(string fullName)
    {
        foreach (var attribute in Attributes)
        {
            if (attribute is Attribute attr && attr.GetType().FullName == fullName) return new ReflectionResolvedAttribute(attr);
        }

        return null;
    }

    public IEnumerable<IResolvedAttribute> GetAttributes() =>
        Attributes.OfType<Attribute>().Select(a => (IResolvedAttribute)new ReflectionResolvedAttribute(a));

    public IResolvedMember GetMemberByName(string memberName)
    {
        // Made on first use: most wrapped types are never asked for a member.
        var members = _members;
        if (members == null)
        {
            Interlocked.CompareExchange(ref _members, new ConcurrentDictionary<string, IResolvedMember>(StringComparer.Ordinal), null);
            members = _members;
        }

        if (members.TryGetValue(memberName, out var known))
        {
            return known;
        }

        var member = _type.GetMember(memberName, BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static);
        return members.GetOrAdd(memberName, member.Length > 0 ? new ReflectionResolvedMember(member[0], this) : null);
    }

    public IReadOnlyList<IResolvedProperty> GetAllProperties() => Properties;

    private IResolvedProperty[] Properties => _properties ??= _type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
        .Select(p => (IResolvedProperty)new ReflectionResolvedProperty(p)).ToArray();

    public bool ImplementsInterface(string interfaceName)
    {
        foreach (var i in Interfaces)
        {
            var name = i.Name;
            if (name == interfaceName
                || name.Length > interfaceName.Length && name[interfaceName.Length] == '`'
                   && name.StartsWith(interfaceName, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    public bool IsCollection() => ImplementsInterface("ICollection") || ImplementsInterface("IList");

    public IResolvedType GetInterface(string interfaceName)
    {
        foreach (var i in Interfaces)
        {
            if (i.Name == interfaceName) return Of(i);
        }

        return null;
    }

    public bool InheritsFromMarkupExtension(string fullyQualifiedName) => InheritsFrom(fullyQualifiedName);

    public bool FindPropertyWithAttribute(string attributeFullName, out IResolvedProperty property)
    {
        foreach (var candidate in Properties)
        {
            if (candidate.HasAttribute(attributeFullName))
            {
                property = candidate;
                return true;
            }
        }

        property = null;
        return false;
    }

    public ResolvedSpecialType SpecialType => _type switch
    {
        _ when _type == typeof(double) => ResolvedSpecialType.System_Double,
        _ when _type == typeof(short) => ResolvedSpecialType.System_Int16,
        _ when _type == typeof(int) => ResolvedSpecialType.System_Int32,
        _ when _type == typeof(long) => ResolvedSpecialType.System_Int64,
        _ when _type == typeof(ushort) => ResolvedSpecialType.System_UInt16,
        _ when _type == typeof(uint) => ResolvedSpecialType.System_UInt32,
        _ when _type == typeof(ulong) => ResolvedSpecialType.System_UInt64,
        _ when _type == typeof(float) => ResolvedSpecialType.System_Single,
        _ when _type == typeof(decimal) => ResolvedSpecialType.System_Decimal,
        _ when _type == typeof(sbyte) => ResolvedSpecialType.System_SByte,
        _ when _type == typeof(byte) => ResolvedSpecialType.System_Byte,
        _ when _type == typeof(string) => ResolvedSpecialType.System_String,
        _ when _type == typeof(object) => ResolvedSpecialType.System_Object,
        _ when _type == typeof(bool) => ResolvedSpecialType.System_Boolean,
        _ when _type.IsEnum => ResolvedSpecialType.System_Enum,
        _ when _type.IsArray => ResolvedSpecialType.System_Array,
        _ => ResolvedSpecialType.None
    };

    public ResolvedTypeKind TypeKind => _type switch
    {
        _ when _type.IsInterface => ResolvedTypeKind.Interface,
        _ when _type.IsEnum => ResolvedTypeKind.Enum,
        _ when _type.IsValueType => ResolvedTypeKind.Struct,
        _ when _type.IsClass => ResolvedTypeKind.Class,
        _ => ResolvedTypeKind.Unknown
    };

    public ResolvedMemberKind MemberKind => ResolvedMemberKind.Unknown;

    public EntityType EntityType
    {
        get
        {
            if (_entityType < 0)
            {
                _entityType = (int)KindOfEntity();
            }

            return (EntityType)_entityType;
        }
    }

    private EntityType KindOfEntity()
    {
        if (ImplementsInterface("IWindow")) return EntityType.Window;
        if (ImplementsInterface("IView")) return EntityType.View;
        if (ImplementsInterface("IUIApplication")) return EntityType.UIApplication;
        if (ImplementsInterface("ITheme")) return EntityType.Theme;
        if (ImplementsInterface("IResourceDictionary")) return EntityType.ResourceDictionary;
        if (ImplementsInterface("IStyleSet")) return EntityType.StyleSet;
        if (ImplementsInterface("IThemeVariant")) return EntityType.ThemeVariant;
        if (ImplementsInterface("IApplicationBlueprint"))
        {
            return EntityType.ApplicationBlueprint;
        }
        if (ImplementsInterface("IUIComponent"))
        {
            return EntityType.Control;
        }

        if (IsAssignableTo(DataTemplateSetTypeName))
        {
            return EntityType.DataTemplateSet;
        }

        return EntityType.Unknown;
    }

    private static string FullNameOf(Type t) => string.IsNullOrEmpty(t.Namespace) ? t.Name : $"{t.Namespace}.{t.Name}";

    private static string QualifiedNameOf(Type type)
    {
        if (Keywords.TryGetValue(type, out var keyword))
        {
            return keyword;
        }

        if (type.IsArray)
        {
            return $"{QualifiedNameOf(type.GetElementType())}[]";
        }

        if (Nullable.GetUnderlyingType(type) is { } underlying)
        {
            return $"{QualifiedNameOf(underlying)}?";
        }

        var name = type.Name;
        var tick = name.IndexOf('`');
        if (tick >= 0)
        {
            name = name.Substring(0, tick);
        }

        var owner = type.IsNested
            ? $"{QualifiedNameOf(type.DeclaringType)}."
            : string.IsNullOrEmpty(type.Namespace) ? "global::" : $"global::{type.Namespace}.";
        var ownArguments = type.GetGenericArguments().Skip(type.IsNested ? type.DeclaringType.GetGenericArguments().Length : 0).ToList();
        return ownArguments.Count == 0
            ? $"{owner}{name}"
            : $"{owner}{name}<{string.Join(", ", ownArguments.Select(QualifiedNameOf))}>";
    }

    public override string ToString() => FullName;
}
