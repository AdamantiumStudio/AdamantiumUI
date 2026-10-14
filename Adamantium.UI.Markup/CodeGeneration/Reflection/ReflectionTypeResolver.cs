using System.Reflection;
using System.Runtime.CompilerServices;

namespace Adamantium.UI.Markup.CodeGeneration.Reflection;

/// <summary>
/// Reflection-backed <see cref="ITypeResolver"/>: the runtime counterpart of <c>RoslynTypeResolver</c>. It lets
/// <c>DefaultAumlTransformer</c> resolve markup against the real, loaded engine/project assemblies, so the live
/// designer resolves types exactly like the compiler does.
/// </summary>
public class ReflectionTypeResolver : ITypeResolver
{
    private readonly List<Assembly> _assemblies;
    private readonly Dictionary<string, IResolvedAssembly> _resolvedAssembliesMap = new();
    private readonly List<IResolvedAssembly> _resolvedAssemblies = new();
    // One xmlns URI may map to several assemblies (controls + core both under "http://adamantium/ui"); unioned
    // via a CompositeResolvedAssembly on lookup.
    private readonly Dictionary<string, List<IResolvedAssembly>> _resolvedXmlAssemblies = new();
    // Resolve's answers, misses included: a miss asks every assembly. Dropped when an assembly joins.
    private static readonly char[] Slash = ['/'];

    private readonly Dictionary<string, IResolvedType> _resolved = new(StringComparer.Ordinal);
    // GetResolvedAssemblyByXmlDefinition's answers; a key is dropped when an assembly joins its xmlns.
    private readonly Dictionary<string, IResolvedAssembly> _xmlLookups = new(StringComparer.Ordinal);

    public ReflectionTypeResolver(IEnumerable<Assembly> assemblies = null)
    {
        _assemblies = (assemblies ?? AppDomain.CurrentDomain.GetAssemblies()).ToList();
    }

    public IReadOnlyList<IResolvedAssembly> ResolvedAssemblies => _resolvedAssemblies;

    public IReadOnlyCollection<string> XmlnsDefinitions => _resolvedXmlAssemblies.Keys.ToArray();

    public IResolvedAssembly GetResolvedAssembly(string assemblyName)
    {
        foreach (var assembly in _resolvedAssemblies)
            if (assembly.Name == assemblyName) return assembly;
        return ResolveAssembly(assemblyName);
    }

    public IResolvedAssembly GetResolvedAssemblyByXmlDefinition(string xmlDefinition)
    {
        var key = xmlDefinition.Trim(Slash);
        if (_xmlLookups.TryGetValue(key, out var known)) return known;
        if (!_resolvedXmlAssemblies.TryGetValue(key, out var assemblies) || assemblies.Count == 0)
            return null;
        return _xmlLookups[key] = assemblies.Count == 1 ? assemblies[0] : new CompositeResolvedAssembly(assemblies);
    }

    public IResolvedType Resolve(string metadataName)
    {
        if (_resolved.TryGetValue(metadataName, out var known)) return known;

        return _resolved[metadataName] = ResolveUncached(metadataName);
    }

    private IResolvedType ResolveUncached(string metadataName)
    {
        foreach (var assembly in _resolvedAssemblies)
        {
            var resolvedType = assembly.GetTypeByFullName(metadataName);
            if (resolvedType != null) return resolvedType;
        }

        foreach (var asm in _assemblies)
        {
            var type = ReflectionResolvedAssembly.TypeNamed(asm, metadataName);
            if (type != null) return ReflectionResolvedType.Of(type);
        }
        return null;
    }

    public IResolvedType ResolveByShortName(string metadataName, string assembly = "")
    {
        foreach (var resolvedAssembly in _resolvedAssemblies)
        {
            var type = resolvedAssembly.GetTypeByShortName(metadataName);
            if (type != null) return type;
        }

        foreach (var asm in _assemblies)
        {
            var type = ReflectionResolvedAssembly.TopLevelTypeNamed(asm, metadataName);
            if (type != null) return ReflectionResolvedType.Of(type);
        }
        return null;
    }

    public IResolvedAssembly ResolveAssembly(string assembly)
    {
        if (_resolvedAssembliesMap.TryGetValue(assembly, out var resolved)) return resolved;
        return GetOrCreateTypeContainerForAssembly(assembly);
    }

    public List<IResolvedAssembly> ScanXmlnsAttributes()
    {
        var result = new List<IResolvedAssembly>();
        foreach (var asm in _assemblies)
        {
            foreach (var (xmlNs, asmName) in asm.IsDynamic ? ReadXmlnsDefinitions(asm) : XmlnsDefinitionsOf.GetValue(asm, ReadXmlnsDefinitions))
            {
                var container = GetOrCreateTypeContainerForAssembly(asmName, xmlNs);
                if (container != null) result.Add(container);
            }
        }
        return result;
    }

    // An assembly's attributes do not change: read once per assembly.
    private static readonly ConditionalWeakTable<Assembly, (string XmlNamespace, string Assembly)[]> XmlnsDefinitionsOf = new();

    private static (string XmlNamespace, string Assembly)[] ReadXmlnsDefinitions(Assembly asm)
    {
        const string attrName = "Adamantium.UI.Core.Markup.XmlnsDefinitionAttribute";

        // Read the attribute as metadata (CustomAttributeData) rather than instantiating it: cheaper and
        // reflection-only-safe, and the raw "clr-namespace:...;assembly=..." string is parsed here.
        IList<CustomAttributeData> datas;
        try { datas = CustomAttributeData.GetCustomAttributes(asm); }
        catch { return []; }

        var definitions = new List<(string, string)>();
        foreach (var data in datas)
        {
            if (data.AttributeType.FullName != attrName || data.ConstructorArguments.Count < 2) continue;

            var xmlNs = data.ConstructorArguments[0].Value as string;
            var clrNsRaw = data.ConstructorArguments[1].Value as string;
            if (xmlNs == null || clrNsRaw == null) continue;

            definitions.Add((xmlNs, ParseClrNamespace(clrNsRaw).Assembly));
        }
        return definitions.ToArray();
    }

    // clr-namespace:Some.Ns;assembly=Some.Asm  (also tolerates an 'assembly:' separator).
    private static (string ClrNamespace, string Assembly) ParseClrNamespace(string value)
    {
        string clrNs = string.Empty, asm = string.Empty;
        foreach (var raw in value.Split(';'))
        {
            var p = raw.Trim();
            if (p.StartsWith("clr-namespace:")) clrNs = p.Substring("clr-namespace:".Length);
            else if (p.StartsWith("assembly=")) asm = p.Substring("assembly=".Length);
            else if (p.StartsWith("assembly:")) asm = p.Substring("assembly:".Length);
        }
        return (clrNs, asm);
    }

    public IResolvedAssembly GetOrCreateTypeContainerForAssembly(string assemblyName, string xmlNamespace = "")
    {
        if (_resolvedAssembliesMap.TryGetValue(assemblyName, out var resolved))
        {
            EnsureXmlDefinitionAssemblyAdded(resolved, xmlNamespace);
            return resolved;
        }

        var asm = _assemblies.FirstOrDefault(a => ReflectionResolvedAssembly.SimpleName(a) == assemblyName) ?? LoadByName(assemblyName);
        if (asm == null) return null;

        var container = new ReflectionResolvedAssembly(asm);
        _resolvedAssembliesMap[assemblyName] = container;
        _resolvedAssemblies.Add(container);
        _resolved.Clear();
        EnsureXmlDefinitionAssemblyAdded(container, xmlNamespace);
        return container;
    }

    private Assembly LoadByName(string assemblyName)
    {
        if (string.IsNullOrEmpty(assemblyName))
        {
            return null;
        }

        try
        {
            var assembly = Assembly.Load(new AssemblyName(assemblyName));
            _assemblies.Add(assembly);
            return assembly;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public IResolvedAssembly FindAssemblyByNamespace(string targetNamespace)
    {
        foreach (var assembly in _resolvedAssemblies)
            if (assembly is ReflectionResolvedAssembly reflected
                    ? reflected.HasNamespace(targetNamespace)
                    : assembly.Types.Any(t => t.Namespace == targetNamespace))
                return assembly;

        foreach (var asm in _assemblies)
        {
            if (ReflectionResolvedAssembly.DeclaresNamespace(asm, targetNamespace))
                return GetOrCreateTypeContainerForAssembly(ReflectionResolvedAssembly.SimpleName(asm));
        }
        return null;
    }

    public void RegisterGeneratedType(IResolvedType type) { /* no generated types at runtime */ }

    private void EnsureXmlDefinitionAssemblyAdded(IResolvedAssembly assembly, string xmlDefinition)
    {
        if (string.IsNullOrEmpty(xmlDefinition)) return;
        var key = xmlDefinition.Trim(Slash);
        if (!_resolvedXmlAssemblies.TryGetValue(key, out var assemblies))
            _resolvedXmlAssemblies[key] = assemblies = new List<IResolvedAssembly>();
        if (!assemblies.Contains(assembly))
        {
            assemblies.Add(assembly);
            _xmlLookups.Remove(key);
        }
    }
}
