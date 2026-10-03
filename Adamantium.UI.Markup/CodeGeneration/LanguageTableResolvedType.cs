namespace Adamantium.UI.Markup.CodeGeneration;

/// <summary>A language table of the project being built, known from its base language file before the table's class is
/// generated - what lets markup name it: <c>{x:Static CanvasStrings.Current}</c> hands a control the table itself. Its
/// members are <c>Current</c> and the static strings.</summary>
internal sealed class LanguageTableResolvedType : IResolvedType
{
    private readonly List<IResolvedMember> _members;

    public LanguageTableResolvedType(LanguageTableShape shape, string assemblyName)
    {
        var dot = shape.FullName.LastIndexOf('.');
        Name = dot < 0 ? shape.FullName : shape.FullName.Substring(dot + 1);
        Namespace = dot < 0 ? string.Empty : shape.FullName.Substring(0, dot);
        FullName = shape.FullName;
        AssemblyName = assemblyName;

        _members = [new Member("Current", this, [], ResolvedMemberKind.Property)];
        foreach (var phrase in shape.Strings)
        {
            _members.Add(new Member(phrase.Key, this, phrase.Value,
                phrase.Value.Count == 0 ? ResolvedMemberKind.Property : ResolvedMemberKind.Method));
        }
    }

    public string Name { get; }
    public string Namespace { get; }
    public string FullName { get; }
    public string QualifiedName => "global::" + FullName;
    public string AssemblyName { get; }
    public bool IsNamedType => true;
    public bool IsGenericType => false;
    public EntityType EntityType => EntityType.Unknown;
    public IEnumerable<IResolvedType> TypeArguments => [];
    public IResolvedType BaseType => null;
    public IEnumerable<IResolvedMember> Members => _members;
    public ResolvedSpecialType SpecialType => ResolvedSpecialType.None;
    public ResolvedTypeKind TypeKind => ResolvedTypeKind.Class;
    public ResolvedMemberKind MemberKind => ResolvedMemberKind.Unknown;

    public IResolvedMember GetMemberByName(string memberName) => _members.FirstOrDefault(m => m.Name == memberName);

    public bool InheritsFrom(string baseTypeName) => false;
    public bool HasAttribute(string attributeName) => false;
    public IResolvedAttribute GetAttribute(string fullName) => null;
    public IEnumerable<IResolvedAttribute> GetAttributes() => [];
    public List<IResolvedProperty> GetAllProperties() => [];
    public bool IsAssignableTo(string fullName) => fullName == FullName;
    public bool ImplementsInterface(string interfaceName) => false;
    public bool IsCollection() => false;
    public IResolvedType GetInterface(string interfaceName) => null;
    public bool InheritsFromMarkupExtension(string fullyQualifiedName) => false;

    public bool FindPropertyWithAttribute(string attributeFullName, out IResolvedProperty property)
    {
        property = null;
        return false;
    }

    private sealed class Member(string name, IResolvedType type, IReadOnlyList<string> parameters, ResolvedMemberKind kind)
        : IResolvedMember
    {
        public string Name { get; } = name;
        public IResolvedType MemberType => null;
        public IResolvedType DeclaringType { get; } = type;
        public bool HasAttribute(string attributeMetadataName) => false;
        public bool HasSetter() => false;
        public bool IsStatic => true;
        public bool IsPublic => true;
        public IReadOnlyList<string> ParameterNames { get; } = parameters;
        public ResolvedMemberKind MemberKind { get; } = kind;
    }
}
