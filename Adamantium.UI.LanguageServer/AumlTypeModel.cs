using Adamantium.UI.Markup.AST;
using Adamantium.UI.Markup.CodeGeneration;
using Adamantium.UI.Generators.Roslyn;
using Adamantium.UI.Markup.Parsers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Adamantium.UI.LanguageServer;

/// <summary>A markup-settable property: the name written as an attribute, plus its CLR type.</summary>
public sealed record AumlPropertyInfo(string Name, IResolvedType Type);

/// <summary>
/// AUML type model: builds a Roslyn compilation from the target project's referenced
/// assemblies and reuses the engine's own <see cref="RoslynTypeResolver"/>, so completion
/// sees exactly the types the AUML source generator sees.
/// </summary>
public sealed class AumlTypeModel
{
    private readonly ITypeResolver _resolver;
    private readonly Dictionary<string, IReadOnlyList<IResolvedType>> _clrNamespaceCache = new(StringComparer.Ordinal);
    private readonly Dictionary<string, IResolvedType> _byShortName = new(StringComparer.Ordinal);
    // The project's own AUML views (<View>/<Window> roots), pre-registered from the .auml files so they're recognized
    // and complete like framework controls even though the source generator hasn't emitted their classes.
    private readonly List<IResolvedType> _localViews = new();
    private readonly Dictionary<string, IReadOnlyList<IResolvedType>> _derivedTypes = new(StringComparer.Ordinal);
    private readonly object _markupGate = new();
    private readonly Dictionary<string, List<MarkupResourceKey>> _keysByFile = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<string>> _markupFilesByName = new(StringComparer.Ordinal);
    private readonly List<string> _markupRoots = [];
    private IReadOnlyList<AumlResourceKey> _resourceKeys;
    private IReadOnlyList<LanguageTableInfo> _languageTables;

    private AumlTypeModel(ITypeResolver resolver, Compilation compilation)
    {
        _resolver = resolver;
        Compilation = compilation;
    }

    /// <summary>The backing compilation — go-to-definition uses it to map a metadata symbol back to its source dll.</summary>
    public Compilation Compilation { get; }

    /// <summary>The language tables <c>{Localize}</c> can name.</summary>
    public IReadOnlyList<LanguageTableInfo> LanguageTables => _languageTables ??= LanguageServer.LanguageTables.Collect(Compilation);

    public static AumlTypeModel Build(IEnumerable<string> assemblyPaths, IEnumerable<string> sourceFiles = null)
    {
        var references = new List<MetadataReference>();
        foreach (var path in assemblyPaths)
        {
            if (!path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)) continue;
            try { references.Add(MetadataReference.CreateFromFile(path)); }
            catch { /* native or otherwise non-managed dll — skip */ }
        }

        // The project's own C# source is compiled in as syntax trees so its types/properties resolve live
        // from source (no build needed) — its compiled dll is excluded from the references by the caller so a
        // type isn't defined twice. Type lookups go through Compilation.GetTypeByMetadataName, which is
        // source-aware, so bindings/property-type completion reflect saved source edits immediately.
        var syntaxTrees = new List<SyntaxTree>();
        if (sourceFiles is not null)
        {
            foreach (var file in sourceFiles)
            {
                try { syntaxTrees.Add(CSharpSyntaxTree.ParseText(File.ReadAllText(file), path: file)); }
                catch { /* unreadable/locked file — skip */ }
            }
        }

        var compilation = CSharpCompilation.Create(
            "AumlTooling",
            syntaxTrees,
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        return FromCompilation(compilation);
    }

    /// <summary>
    /// Wraps a ready compilation (e.g. the source-graph root from <see cref="SourceProjectGraph"/>). The
    /// optional <paramref name="xmlnsMappings"/> are xmlns -> assembly mappings read from sub-compilations
    /// (where their constructor arguments are materialized) and injected, since they can't be read back through
    /// a CompilationReference.
    /// </summary>
    public static AumlTypeModel FromCompilation(
        Compilation compilation, IEnumerable<(string XmlNamespace, string ClrSpec)> xmlnsMappings = null)
    {
        var resolver = new RoslynTypeResolver(compilation);
        resolver.ScanXmlnsAttributes();
        if (xmlnsMappings is not null)
            foreach (var (xmlNamespace, clrSpec) in xmlnsMappings)
                resolver.AddXmlnsMapping(xmlNamespace, clrSpec);
        return new AumlTypeModel(resolver, compilation);
    }

    /// <summary>
    /// Element types available under an xmlns: an [XmlnsDefinition] URI (e.g. "http://adamantium/ui")
    /// or a XAML-style "clr-namespace:Some.Ns[;assembly=Asm]" mapping onto a raw CLR namespace.
    /// </summary>
    public IReadOnlyList<IResolvedType> GetElements(string xmlns)
    {
        if (TryParseClrNamespace(xmlns, out var clrNamespace, out var assemblyName))
            return GetClrNamespaceTypes(xmlns, clrNamespace, assemblyName);

        var assembly = _resolver.GetResolvedAssemblyByXmlDefinition(xmlns);
        var framework = assembly is null
            ? Enumerable.Empty<IResolvedType>()
            : assembly.Types.Where(IsMarkupType);

        // The project's own AUML views are usable unprefixed alongside the framework controls (the default xmlns), so
        // surface them under an [XmlnsDefinition] namespace too - recognition + property completion for <ControlsView/>.
        return _localViews.Count == 0
            ? framework.ToList()
            : framework.Concat(_localViews).ToList();
    }

    /// <summary>
    /// Registers the project's own AUML views (<c>&lt;View&gt;</c>/<c>&lt;Window&gt;</c>/... roots) as types, so an
    /// embedded view (<c>&lt;ControlsView/&gt;</c>) is recognized and its inherited properties complete - mirroring the
    /// source generator's pre-registration - and every other class the build generates (style sets, resource
    /// dictionaries, themes), so <c>{x:Type}</c> finds them. Parsed straight from the .auml files, so it works with no build.
    /// </summary>
    public void RegisterViews(IEnumerable<string> aumlFiles, string rootNamespace, string projectDir)
    {
        var transformer = new DefaultAumlTransformer();
        foreach (var file in aumlFiles)
        {
            string content;
            try { content = File.ReadAllText(file); } catch { continue; }

            var document = AumlParser.Parse(content);
            if (document.HasErrors) continue;

            document.RelativeFilePath = file.Length > projectDir.Length
                ? file.Substring(projectDir.Length).TrimStart('\\', '/').Replace('\\', '/')
                : Path.GetFileName(file);
            document.RootNamespace = rootNamespace;

            try
            {
                if (transformer.PreRegisterDocument(document, _resolver) is { } view)
                {
                    _localViews.Add(view);
                }
                else
                {
                    transformer.PreRegisterDocument(document, _resolver, anyClass: true);
                }
            }
            catch { /* a malformed view is simply not offered; its own diagnostics surface the real error */ }
        }
    }

    /// <summary>Reads the keys the markup files declare - dictionaries, palettes, the blueprint's resources - straight from
    /// the files, so they are offered with no build: the project's own, and those of projects compiled from source here.</summary>
    public void RegisterResourceKeys(IEnumerable<string> aumlFiles)
    {
        foreach (var file in aumlFiles)
        {
            string content;
            try
            {
                content = File.ReadAllText(file);
            }
            catch (IOException)
            {
                continue;
            }

            UpdateMarkup(file, content);
        }
    }

    /// <summary>Counts the markup under <paramref name="directory"/> as this model's: see <see cref="TracksMarkup"/>.</summary>
    public void TrackMarkupIn(string directory)
    {
        var root = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                   + Path.DirectorySeparatorChar;
        lock (_markupGate)
        {
            if (!_markupRoots.Contains(root, StringComparer.OrdinalIgnoreCase))
            {
                _markupRoots.Add(root);
            }
        }
    }

    /// <summary>Whether <paramref name="file"/> is markup of this model's project or of a project compiled from source
    /// here - a file whose keys and classes the model reads.</summary>
    public bool TracksMarkup(string file)
    {
        var full = Path.GetFullPath(file);
        var sep = Path.DirectorySeparatorChar;
        if (!full.EndsWith(".auml", StringComparison.OrdinalIgnoreCase)
            || full.Contains($"{sep}obj{sep}", StringComparison.OrdinalIgnoreCase)
            || full.Contains($"{sep}bin{sep}", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        lock (_markupGate)
        {
            return _markupRoots.Any(root => full.StartsWith(root, StringComparison.OrdinalIgnoreCase));
        }
    }

    /// <summary>Takes a markup file's text as it stands now - typed in the editor or saved - so the keys it declares are
    /// known at once, with no build. A text that does not parse keeps the keys the file declared before.</summary>
    public void UpdateMarkup(string file, string text)
    {
        file = Path.GetFullPath(file);
        AumlDocument document;
        try
        {
            document = AumlParser.Parse(text ?? string.Empty);
        }
        catch (System.Xml.XmlException)
        {
            document = null;
        }

        List<MarkupResourceKey> keys = null;
        if (document is { HasErrors: false, Root: AumlAstObjectNode root })
        {
            var written = ResourceKeyOccurrences.Find(text).Where(o => o.IsDeclaration).ToList();
            keys = [];
            foreach (var resource in DeclaredResources.Of(root))
            {
                var reference = resource.Value.TypeReference;
                var at = written.FirstOrDefault(o => o.Key == resource.Key);
                var (line, character) = at == null ? (0, 0) : TextPositions.LineAndCharacter(text, at.Start);
                keys.Add(new MarkupResourceKey(resource.Key, reference?.Namespace ?? string.Empty, reference?.Name,
                    resource.PaletteColorAs == null ? null : DeclaredResources.ValueTypeOf(resource, null), file, line, character));
            }
        }

        lock (_markupGate)
        {
            if (keys != null)
            {
                _keysByFile[file] = keys;
            }

            var name = Path.GetFileNameWithoutExtension(file);
            if (!_markupFilesByName.TryGetValue(name, out var files))
            {
                _markupFilesByName[name] = files = [];
            }

            if (!files.Contains(file, StringComparer.OrdinalIgnoreCase))
            {
                files.Add(file);
            }

            _resourceKeys = null;
        }
    }

    /// <summary>Forgets a markup file that is gone: its keys and its class.</summary>
    public void RemoveMarkup(string file)
    {
        file = Path.GetFullPath(file);
        lock (_markupGate)
        {
            _keysByFile.Remove(file);
            if (_markupFilesByName.TryGetValue(Path.GetFileNameWithoutExtension(file), out var files))
            {
                files.RemoveAll(f => string.Equals(f, file, StringComparison.OrdinalIgnoreCase));
            }

            _resourceKeys = null;
        }
    }

    /// <summary>Every place the markup the model reads declares <paramref name="key"/> - the same key in several
    /// dictionaries included.</summary>
    public IReadOnlyList<AumlResourceKey> DeclarationsOf(string key)
    {
        lock (_markupGate)
        {
            return _keysByFile.Values.SelectMany(k => k)
                .Where(k => k.Key == key)
                .Select(k => new AumlResourceKey(k.Key, null, k.File, k.Line, k.Character))
                .ToList();
        }
    }

    /// <summary>The folder of the project a markup file belongs to - the deepest folder the model reads markup from that
    /// holds it; null when none does.</summary>
    public string MarkupRootOf(string file)
    {
        if (string.IsNullOrEmpty(file))
        {
            return null;
        }

        var full = Path.GetFullPath(file);
        lock (_markupGate)
        {
            return _markupRoots.Where(root => full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(root => root.Length)
                .FirstOrDefault();
        }
    }

    /// <summary>Every markup file the model reads.</summary>
    public IReadOnlyList<string> MarkupFiles
    {
        get
        {
            lock (_markupGate)
            {
                return _markupFilesByName.Values.SelectMany(f => f).ToList();
            }
        }
    }

    /// <summary>The markup file a class is made from, or null when it is not made from markup the model reads. Among
    /// files of the class's name, the one whose folders match the end of its namespace.</summary>
    public string MarkupFileOf(IResolvedType type)
    {
        if (type == null || string.IsNullOrEmpty(type.Name))
        {
            return null;
        }

        List<string> candidates;
        lock (_markupGate)
        {
            if (!_markupFilesByName.TryGetValue(type.Name, out var files) || files.Count == 0)
            {
                return null;
            }

            candidates = files.ToList();
        }

        var namespaceParts = (type.Namespace ?? string.Empty).Split('.', StringSplitOptions.RemoveEmptyEntries);
        var best = candidates.Select(f => (File: f, Score: SharedTail(f, namespaceParts))).OrderByDescending(c => c.Score).First();
        return best.Score > 0 || candidates.Count == 1 ? best.File : null;
    }

    private static int SharedTail(string file, string[] namespaceParts)
    {
        var folderParts = (Path.GetDirectoryName(file) ?? string.Empty)
            .Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar, '.'], StringSplitOptions.RemoveEmptyEntries);
        var shared = 0;
        while (shared < folderParts.Length && shared < namespaceParts.Length
               && string.Equals(folderParts[^(shared + 1)], namespaceParts[^(shared + 1)], StringComparison.OrdinalIgnoreCase))
        {
            shared++;
        }

        return shared;
    }

    /// <summary>Every resource key the project can reach, one per key: those its markup and the markup of projects
    /// compiled from source here declare, and those the build named in the assemblies it references.</summary>
    public IReadOnlyList<AumlResourceKey> ResourceKeys
    {
        get
        {
            var keys = _resourceKeys;
            if (keys == null)
            {
                keys = CollectResourceKeys();
                _resourceKeys = keys;
            }

            return keys;
        }
    }

    private IReadOnlyList<AumlResourceKey> CollectResourceKeys()
    {
        const string attributeName = "Adamantium.UI.Core.Resources.ResourceKeyAttribute";
        List<MarkupResourceKey> declared;
        lock (_markupGate)
        {
            declared = _keysByFile.Values.SelectMany(k => k).ToList();
        }

        var keys = new List<AumlResourceKey>();
        foreach (var key in declared)
        {
            var valueType = key.ValueTypeName != null
                ? _resolver.Resolve(key.ValueTypeName)
                : key.ElementName == null ? null : GetElement(key.ElementNamespace, key.ElementName) ?? ResolveShortName(key.ElementName);
            keys.Add(new AumlResourceKey(key.Key, valueType, key.File, key.Line, key.Character));
        }

        foreach (var assembly in Compilation.SourceModule.ReferencedAssemblySymbols)
        {
            if (assembly.Name != "Adamantium.UI.Core" && !assembly.Modules.SelectMany(m => m.ReferencedAssemblies).Any(a => a.Name == "Adamantium.UI.Core"))
            {
                continue;
            }

            foreach (var type in TypesOf(assembly.GlobalNamespace))
            {
                foreach (var attribute in type.GetAttributes())
                {
                    if (attribute.AttributeClass?.ToDisplayString() == attributeName && attribute.ConstructorArguments.Length == 2
                        && attribute.ConstructorArguments[0].Value is string key)
                    {
                        var valueType = attribute.ConstructorArguments[1].Value is ITypeSymbol symbol ? new RoslynResolvedType(symbol) : null;
                        keys.Add(new AumlResourceKey(key, valueType));
                    }
                }
            }
        }

        return keys.GroupBy(k => k.Key, StringComparer.Ordinal).Select(Merge).ToList();
    }

    private static AumlResourceKey Merge(IEnumerable<AumlResourceKey> sameKey)
    {
        var all = sameKey.ToList();
        var typed = all.FirstOrDefault(k => k.ValueType != null);
        var located = all.FirstOrDefault(k => k.File != null);
        if (located == null)
        {
            return typed ?? all[0];
        }

        return located.ValueType != null || typed == null ? located : located with { ValueType = typed.ValueType };
    }

    // Compiler-generated types (<Module>, <>c, <PrivateImplementationDetails>, <>z__ReadOnlyArray, …) are never
    // valid markup elements; their names start with '<' (inexpressible in C#), so drop them from completion.
    private static bool IsMarkupType(IResolvedType type) =>
        !string.IsNullOrEmpty(type.Name) && type.Name[0] != '<';

    /// <summary>
    /// Resolves the types of a <c>clr-namespace:</c> xmlns — scoped to <c>;assembly=</c> when given,
    /// otherwise the first referenced assembly that declares the CLR namespace. Cached per URI, since
    /// the no-assembly lookup may enumerate every referenced assembly.
    /// </summary>
    private IReadOnlyList<IResolvedType> GetClrNamespaceTypes(string uri, string clrNamespace, string assemblyName)
    {
        if (_clrNamespaceCache.TryGetValue(uri, out var cached)) return cached;

        var assembly = string.IsNullOrEmpty(assemblyName)
            ? _resolver.FindAssemblyByNamespace(clrNamespace)
            : _resolver.GetResolvedAssembly(assemblyName) ?? _resolver.ResolveAssembly(assemblyName);

        IReadOnlyList<IResolvedType> types = assembly is null
            ? []
            : assembly.Types.Where(t => t.Namespace == clrNamespace && IsMarkupType(t)).ToList();
        if (types.Count == 0 && !string.IsNullOrEmpty(assemblyName))
        {
            types = MergedNamespaceTypes(clrNamespace);
        }

        _clrNamespaceCache[uri] = types;
        return types;
    }

    private IReadOnlyList<IResolvedType> MergedNamespaceTypes(string clrNamespace)
    {
        INamespaceSymbol symbol = Compilation.GlobalNamespace;
        foreach (var part in clrNamespace.Split('.'))
        {
            symbol = symbol?.GetNamespaceMembers().FirstOrDefault(n => n.Name == part);
        }

        return symbol == null
            ? []
            : symbol.GetTypeMembers()
                .Where(t => t.DeclaredAccessibility == Accessibility.Public)
                .Select(t => (IResolvedType)new RoslynResolvedType(t))
                .Where(IsMarkupType)
                .ToList();
    }

    /// <summary>Parses <c>clr-namespace:Some.Ns[;assembly=Asm]</c>; false when the URI isn't a clr-namespace.</summary>
    private static bool TryParseClrNamespace(string uri, out string clrNamespace, out string assembly)
    {
        clrNamespace = "";
        assembly = "";
        const string scheme = "clr-namespace:";
        if (!uri.StartsWith(scheme, StringComparison.Ordinal)) return false;

        var rest = uri[scheme.Length..];
        int semicolon = rest.IndexOf(';');
        if (semicolon < 0)
        {
            clrNamespace = rest.Trim();
            return true;
        }

        clrNamespace = rest[..semicolon].Trim();
        const string assemblyKey = "assembly=";
        int key = rest.IndexOf(assemblyKey, semicolon, StringComparison.Ordinal);
        if (key >= 0) assembly = rest[(key + assemblyKey.Length)..].Trim();
        return true;
    }

    /// <summary>The type <paramref name="name"/> names under <paramref name="xmlns"/>: one of the namespace's, else - under
    /// an [XmlnsDefinition] namespace - the one the build falls back to by short name (<c>&lt;Thickness&gt;</c>).</summary>
    public IResolvedType GetElement(string xmlns, string name) =>
        InNamespace(xmlns, name)
        ?? (TryParseClrNamespace(xmlns, out _, out _) || !IsKnownNamespace(xmlns) ? null : ByShortName(name));

    private IResolvedType InNamespace(string xmlns, string name) => GetElements(xmlns).FirstOrDefault(t => t.Name == name);

    private IResolvedType ByShortName(string name)
    {
        if (!_byShortName.TryGetValue(name, out var type))
        {
            type = _resolver.ResolveByShortName(name) ?? OwnAssembly()?.GetTypeByShortName(name);
            _byShortName[name] = type;
        }
        return type;
    }

    /// <summary>
    /// Every registered xmlns whose assembly contains an element type with this simple name —
    /// the candidate namespaces to import for an unresolved element (the auto-import quick-fix).
    /// </summary>
    public IReadOnlyList<string> FindNamespacesContaining(string typeName)
    {
        var result = new List<string>();
        foreach (var xmlns in _resolver.XmlnsDefinitions)
            if (GetElement(xmlns, typeName) is not null)
                result.Add(xmlns);
        return result;
    }

    /// <summary>
    /// True if the xmlns is resolvable: a registered [XmlnsDefinition] namespace, or a
    /// <c>clr-namespace:</c> that maps onto a CLR namespace containing at least one type.
    /// </summary>
    public bool IsKnownNamespace(string xmlns) =>
        TryParseClrNamespace(xmlns, out _, out _)
            ? GetElements(xmlns).Count > 0
            : _resolver.GetResolvedAssemblyByXmlDefinition(xmlns) is not null;

    /// <summary>
    /// Markup-settable properties of an element type: public-settable, inherited included,
    /// de-duplicated by name, excluding explicit interface implementations (e.g. "IFoo.Bar").
    /// </summary>
    public IReadOnlyList<AumlPropertyInfo> GetProperties(IResolvedType element, bool includeReadOnlyCollections = false)
    {
        var result = new List<AumlPropertyInfo>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in element.GetAllProperties() ?? Enumerable.Empty<IResolvedProperty>())
        {
            if (property.Name.Contains('.')) continue;   // explicit interface implementation
            if (property.Name.Contains('[')) continue;   // indexer (e.g. "this[]") — never valid markup
            if (!seen.Add(property.Name)) continue;       // already added from a more-derived type
            var member = element.GetMemberByName(property.Name);
            if (member is not { MemberKind: ResolvedMemberKind.Property }) continue;

            // Settable (as an attribute), plus — for property-element syntax — read-only collections
            // that are populated by adding children rather than assigned, e.g. Theme.StyleIncludes.
            if (member.HasSetter() ||
                (includeReadOnlyCollections && property.PropertyType is { } type && type.IsCollection()))
                result.Add(new AumlPropertyInfo(property.Name, property.PropertyType));
        }
        return result;
    }

    public IResolvedType GetPropertyType(IResolvedType element, string propertyName) =>
        GetProperties(element).FirstOrDefault(p => p.Name == propertyName)?.Type;

    private const string ThemeTypeFullName = "Adamantium.UI.Core.Resources.Theme";

    /// <summary>
    /// The keys completable in <c>{ThemeResource Key}</c>: the <c>Brush</c>-typed properties of the framework
    /// <see cref="ThemeTypeFullName">Theme</see> class (the theme's runtime-mutable brushes - AccentFillColorDefault,
    /// FocusStrokeColorOuter, ...). These are NOT the static <c>{ResourceReference}</c> brushes, which live in a
    /// ResourceDictionary - hence offering the Theme's own brushes here instead of an arbitrary color list.
    /// </summary>
    public IReadOnlyList<string> GetThemeBrushKeys()
    {
        var theme = _resolver.Resolve(ThemeTypeFullName);
        if (theme is null) return [];
        return GetProperties(theme)
            .Where(p => p.Type?.Name is "Brush" or "IBrush")
            .Select(p => p.Name)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// Readable properties for binding-path completion (<c>{Binding ...}</c> against an <c>x:ViewModel</c>): every
    /// public instance property, inherited included, de-duplicated by name — unlike <see cref="GetProperties"/>
    /// this does not require a setter, since one-way bindings read get-only properties too.
    /// </summary>
    public IReadOnlyList<AumlPropertyInfo> GetBindableProperties(IResolvedType type)
    {
        var result = new List<AumlPropertyInfo>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in type.GetAllProperties() ?? Enumerable.Empty<IResolvedProperty>())
        {
            if (property.Name.Contains('.') || property.Name.Contains('[')) continue;   // explicit impl / indexer
            if (!seen.Add(property.Name)) continue;
            var member = type.GetMemberByName(property.Name);
            if (member is not { MemberKind: ResolvedMemberKind.Property }) continue;
            result.Add(new AumlPropertyInfo(property.Name, property.PropertyType));
        }
        foreach (var (name, memberType) in ViewModelMembers.Generated(type))
        {
            if (seen.Add(name))
            {
                result.Add(new AumlPropertyInfo(name, memberType));
            }
        }

        return result;
    }

    /// <summary>First element type with this simple name across all registered xmlns namespaces — used
    /// to resolve an attached-property owner written without an xmlns prefix (e.g. <c>ResourceContext</c>).</summary>
    public IResolvedType FindElement(string name)
    {
        foreach (var xmlns in _resolver.XmlnsDefinitions)
            if (InNamespace(xmlns, name) is { } type) return type;
        return null;
    }

    /// <summary>A type written without a prefix, found the way the build finds it: in a registered xmlns, else by its
    /// short name in the project or any assembly it references.</summary>
    public IResolvedType ResolveShortName(string name) =>
        string.IsNullOrEmpty(name) ? null : FindElement(name) ?? ByShortName(name);

    /// <summary>A type as markup writes it, <c>prefix:Name</c> or <c>Name</c>, found the way the build finds it;
    /// <paramref name="type"/> is null when it does not exist. False when it cannot be judged: an undeclared prefix or
    /// an unknown namespace, each of which has an error of its own.</summary>
    public bool TryResolveWritten(string typeText, IReadOnlyDictionary<string, string> namespaces, out IResolvedType type)
    {
        type = null;
        var colon = typeText.IndexOf(':');
        if (colon < 0)
        {
            type = ResolveShortName(typeText);
            return true;
        }

        if (!namespaces.TryGetValue(typeText[..colon], out var xmlns) || !IsKnownNamespace(xmlns))
        {
            return false;
        }

        type = GetElement(xmlns, typeText[(colon + 1)..]);
        return true;
    }

    /// <summary>Every public class of the project's references and source that derives from <paramref name="baseFullName"/>,
    /// abstract ones left out - what a <c>[TypeOf]</c> property can be given by name.</summary>
    public IReadOnlyList<IResolvedType> TypesDerivedFrom(string baseFullName)
    {
        if (_derivedTypes.TryGetValue(baseFullName, out var cached))
        {
            return cached;
        }

        var found = new List<IResolvedType>();
        foreach (var assembly in Compilation.SourceModule.ReferencedAssemblySymbols.Append(Compilation.Assembly))
        {
            foreach (var type in TypesOf(assembly.GlobalNamespace))
            {
                if (type.DeclaredAccessibility == Accessibility.Public && type.TypeKind == TypeKind.Class && !type.IsAbstract
                    && Derives(type, baseFullName))
                {
                    found.Add(new RoslynResolvedType(type));
                }
            }
        }

        _derivedTypes[baseFullName] = found;
        return found;
    }

    private static IEnumerable<INamedTypeSymbol> TypesOf(INamespaceSymbol @namespace)
    {
        foreach (var type in @namespace.GetTypeMembers())
        {
            yield return type;
        }

        foreach (var child in @namespace.GetNamespaceMembers())
        {
            foreach (var type in TypesOf(child))
            {
                yield return type;
            }
        }
    }

    private static bool Derives(INamedTypeSymbol type, string baseFullName)
    {
        for (var current = type.BaseType; current != null; current = current.BaseType)
        {
            if (current.ToDisplayString() == baseFullName)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The project's own types: the build finds them by name, with no prefix.</summary>
    public IReadOnlyList<IResolvedType> ProjectTypes => OwnAssembly()?.Types ?? [];

    private IResolvedAssembly OwnAssembly() => _resolver.ResolveAssembly(Compilation.AssemblyName);

    /// <summary>
    /// Attached properties an owner type declares (XAML <c>Owner.Property</c> syntax): a static
    /// <c>Get&lt;Name&gt;</c>/<c>Set&lt;Name&gt;</c> accessor pair (e.g. ResourceContext.Source from
    /// GetSource/SetSource). Only members declared on the owner — never inherited DP accessors like
    /// GetValue/SetValue — so it doesn't mistake instance plumbing for attached properties.
    /// </summary>
    public IReadOnlyList<AumlPropertyInfo> GetAttachedProperties(IResolvedType owner)
    {
        var members = owner.Members.Where(m => m.MemberKind == ResolvedMemberKind.Method && m.IsStatic && m.IsPublic).ToList();
        var setters = new HashSet<string>(
            members.Where(m => m.Name.StartsWith("Set", StringComparison.Ordinal) && m.Name.Length > 3 && m.ParameterNames.Count == 2)
                   .Select(m => m.Name[3..]),
            StringComparer.Ordinal);

        var result = new List<AumlPropertyInfo>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var getter in members)
        {
            if (!getter.Name.StartsWith("Get", StringComparison.Ordinal) || getter.Name.Length <= 3 || getter.ParameterNames.Count != 1) continue;
            var name = getter.Name[3..];
            if (!setters.Contains(name) || !seen.Add(name)) continue;
            result.Add(new AumlPropertyInfo(name, getter.MemberType));   // getter return type = the property's value type
        }
        return result;
    }

    /// <summary>Whether markup can write the type as an element: one the build can create - a class or struct, neither
    /// abstract nor static nor generic, with a public parameterless constructor, and no attribute, event data or
    /// exception - or the owner of attached properties, which a property element names
    /// (<c>&lt;ResourceContext.Resources&gt;</c>).</summary>
    public bool CanBeElement(IResolvedType type)
    {
        if (type is MetadataResolvedType)
        {
            return true;
        }

        if (type is not RoslynResolvedType { Symbol: INamedTypeSymbol symbol })
        {
            return false;
        }

        var creatable = symbol.TypeKind is TypeKind.Class or TypeKind.Struct
                        && !symbol.IsAbstract
                        && !symbol.IsStatic
                        && !symbol.IsGenericType
                        && (symbol.TypeKind == TypeKind.Struct
                            || symbol.InstanceConstructors.Any(c => c.Parameters.Length == 0 && c.DeclaredAccessibility == Accessibility.Public))
                        && !DerivesFrom(symbol, "System.Attribute", "System.EventArgs", "System.Exception");
        return creatable || GetAttachedProperties(type).Count > 0;
    }

    /// <summary>Whether markup can write the type by name as a markup extension: a public class, neither abstract, static
    /// nor generic, with a public constructor.</summary>
    public static bool CanBeExtension(IResolvedType type) =>
        type is not RoslynResolvedType { Symbol: INamedTypeSymbol symbol } ||
        symbol is { TypeKind: TypeKind.Class, IsAbstract: false, IsStatic: false, IsGenericType: false, DeclaredAccessibility: Accessibility.Public } &&
        symbol.InstanceConstructors.Any(c => c.DeclaredAccessibility == Accessibility.Public);

    /// <summary>Whether a type reference - {x:Type}, x:DataType, TargetType - can name the type: not a static class, nor
    /// a generic one markup has no way to give arguments to.</summary>
    public static bool CanBeReferenced(IResolvedType type) =>
        type is not RoslynResolvedType { Symbol: INamedTypeSymbol symbol } || symbol is { IsStatic: false, IsGenericType: false };

    /// <summary>Whether {x:Static} can read something of the type: an enum, or a type with a public static field or
    /// property.</summary>
    public static bool HasStaticValues(IResolvedType type)
    {
        if (type.TypeKind == ResolvedTypeKind.Enum)
        {
            return true;
        }

        for (var current = type; current != null; current = current.BaseType)
        {
            if (current.Members.Any(m => m.IsStatic && m.IsPublic && m.MemberKind is ResolvedMemberKind.Field or ResolvedMemberKind.Property))
            {
                return true;
            }
        }

        return false;
    }

    private static bool DerivesFrom(INamedTypeSymbol symbol, params string[] baseNames)
    {
        for (var current = symbol.BaseType; current != null; current = current.BaseType)
        {
            if (baseNames.Contains(current.ToDisplayString()))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// True if the element type has any member with this name (property, event, etc.). Used by
    /// diagnostics to flag only clearly-unknown attributes, never valid-but-unusual members.
    /// </summary>
    public bool IsKnownAttribute(IResolvedType element, string name) =>
        element.GetMemberByName(name) is not null;

    /// <summary>Value completions for an attribute's CLR type: enum members or boolean literals.</summary>
    public IReadOnlyList<string> GetValueCompletions(IResolvedType propertyType)
    {
        if (propertyType.TypeKind == ResolvedTypeKind.Enum)
            return propertyType.Members
                .Where(m => m.MemberKind == ResolvedMemberKind.Field && m.Name != "value__")
                .Select(m => m.Name)
                .ToList();

        if (propertyType.SpecialType == ResolvedSpecialType.System_Boolean)
            return ["true", "false"];

        if (propertyType.Name is "Brush" or "IBrush")
            return CommonColors;

        return [];
    }

    /// <summary>Names of usable markup extensions (<c>{Name ...}</c>): every type deriving from MarkupExtension,
    /// with the conventional "Extension" suffix stripped (e.g. BitmapImageExtension -> BitmapImage).</summary>
    public IReadOnlyList<string> GetMarkupExtensions()
    {
        const string baseFqn = "Adamantium.UI.Core.MarkupExtensions.MarkupExtension";
        var names = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var assembly in _resolver.ResolvedAssemblies)
            foreach (var type in assembly.Types)
            {
                if (!type.InheritsFromMarkupExtension(baseFqn) || !CanBeExtension(type)) continue;
                var name = type.Name;
                if (name.EndsWith("Extension") && name.Length > "Extension".Length) name = name[..^"Extension".Length];
                names.Add(name);
            }
        return names.ToList();
    }

    /// <summary>Resolves a markup-extension type from the name used in <c>{Name ...}</c>: matches either the exact type
    /// name or the conventional <c>NameExtension</c> form (so both <c>{Binding}</c> and <c>{BindingExtension}</c> work).</summary>
    public IResolvedType ResolveMarkupExtensionType(string localName)
    {
        const string baseFqn = "Adamantium.UI.Core.MarkupExtensions.MarkupExtension";
        foreach (var assembly in _resolver.ResolvedAssemblies)
            foreach (var type in assembly.Types)
            {
                if (type.Name == "MarkupExtension" || !type.InheritsFromMarkupExtension(baseFqn)) continue;
                if (type.Name == localName || type.Name == localName + "Extension") return type;
            }
        return null;
    }

    /// <summary>The markup extension's positional/default argument property (the one marked
    /// <c>[DefaultProperty]</c>, e.g. <c>Binding.Path</c>), or null when the extension has none.</summary>
    public IResolvedProperty GetDefaultProperty(IResolvedType extensionType) =>
        extensionType.FindPropertyWithAttribute("Adamantium.UI.Core.MarkupExtensions.DefaultPropertyAttribute", out var p) ? p : null;

    private static readonly string[] CommonColors =
    {
        "Transparent", "Black", "White", "Gray", "Silver", "Red", "Green", "Blue", "Yellow",
        "Orange", "Purple", "Pink", "Brown", "Cyan", "Magenta", "Lime", "Navy", "Teal", "Gold",
    };
}
