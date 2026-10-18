using Adamantium.UI.Markup.CodeGeneration;
using Adamantium.UI.Generators.Localization;
using Adamantium.UI.Generators.Roslyn;
using Microsoft.CodeAnalysis;

namespace Adamantium.UI.LanguageServer;

/// <summary>
/// Resolves the declaration of what is under the caret (go-to-definition): a resource key goes to the markup that
/// declares it; a type - an element's, or one written as a value - to its C# file, or to its markup file when the build
/// makes the class from markup; an attribute to its C# property; the member of an <c>{x:Static}</c> to its declaration;
/// a <c>{Localize}</c> phrase to the line of the table's base language file that writes it. In-repo types —
/// including the engine base controls, which the source graph compiles from source — resolve to their real
/// source file; types that live only in an external assembly are decompiled on demand (see
/// <see cref="MetadataDecompiler"/>). Reuses the hover token detector by placing the caret at the token end.
/// </summary>
public sealed class DefinitionEngine
{
    private const string FallbackXmlns = "http://adamantium/ui";
    private const string LocalizeOpening = "{Localize";

    private readonly AumlTypeModel _model;

    public DefinitionEngine(AumlTypeModel model) => _model = model;

    /// <summary>Where what is at <paramref name="offset"/> is declared; null when nothing there is. A resource key
    /// declared more than once is taken from <paramref name="documentPath"/>'s own project first.</summary>
    public DefinitionLocation Definition(string text, int offset, string documentPath = null)
    {
        if (string.IsNullOrEmpty(text) || offset < 0 || offset > text.Length) return null;

        if (ResourceKeyOccurrences.At(text, offset) is { } key)
        {
            return key.IsDeclaration ? null : KeyDefinition(key.Key, documentPath);
        }

        int tokenEnd = offset;
        while (tokenEnd < text.Length && IsNameChar(text[tokenEnd])) tokenEnd++;

        var namespaces = AumlNamespaces.Scan(text);
        if (LocalizedAt(text, offset) is { } localized)
        {
            return PhraseDefinition(localized.Written, localized.OnKey, namespaces, documentPath);
        }

        var tokens = SemanticTokensEngine.Tokenize(text, _model);
        if (MemberWrittenAt(text, offset, tokens, namespaces) is { } member)
        {
            return Locate(member);
        }

        if (TypeWrittenAt(text, offset, tokens, namespaces) is { } written)
        {
            return LocateType(written);
        }

        var ctx = AumlCaretContext.Detect(text, tokenEnd);
        var symbol = ctx.Kind switch
        {
            AumlCompletionKind.ElementName => ElementSymbol(ctx.Prefix, namespaces),
            AumlCompletionKind.AttributeName => AttributeSymbol(ctx, namespaces),
            _ => null
        };
        return symbol is null ? null : Locate(symbol);
    }

    // An element name is a type ("controls:Border") or a property-element ("controls:Border.Child"); the latter
    // navigates to the property, not the type.
    private ISymbol ElementSymbol(string qualifiedName, IReadOnlyDictionary<string, string> namespaces)
    {
        var name = qualifiedName ?? "";
        int dot = name.IndexOf('.');
        if (dot < 0)
            return SymbolOf(ResolveElement(name, namespaces));

        var owner = ResolveElement(name[..dot], namespaces);
        return MemberSymbol(owner, name[(dot + 1)..]);
    }

    private ISymbol AttributeSymbol(AumlCompletionContext ctx, IReadOnlyDictionary<string, string> namespaces)
    {
        var (attrPrefix, local) = SplitName(ctx.Prefix ?? "");

        // x: directive — a markup language feature, no C# declaration to jump to.
        if (attrPrefix.Length > 0 && namespaces.TryGetValue(attrPrefix, out var ns) && ns == AumlXDirectives.Xmlns)
            return null;

        // Attached property "Owner.Prop" — the owner is the dotted prefix; jump to its Get<Prop> accessor.
        int dot = local.IndexOf('.');
        if (dot >= 0)
            return MemberSymbol(_model.FindElement(local[..dot]), local[(dot + 1)..], attached: true);

        return MemberSymbol(ResolveElement(ctx.ElementName, namespaces), local);
    }

    private DefinitionLocation KeyDefinition(string key, string documentPath)
    {
        var ownRoot = _model.MarkupRootOf(documentPath);
        var declared = _model.DeclarationsOf(key)
            .OrderBy(k => ownRoot != null && _model.MarkupRootOf(k.File) == ownRoot ? 0 : 1)
            .FirstOrDefault();
        return declared == null
            ? null
            : new DefinitionLocation(declared.File, declared.Line, declared.Character, declared.Line, declared.Character + key.Length);
    }

    private IResolvedType TypeWrittenAt(string text, int offset, IReadOnlyList<SemToken> tokens,
        IReadOnlyDictionary<string, string> namespaces)
    {
        for (var i = 0; i < tokens.Count; i++)
        {
            var token = tokens[i];
            if (token.TokenType != SemanticTokensEngine.Type || offset < token.Start || offset > token.Start + token.Length)
            {
                continue;
            }

            var start = token.Start;
            if (i > 0 && tokens[i - 1] is { TokenType: SemanticTokensEngine.Namespace } prefix
                      && prefix.Start + prefix.Length + 1 == token.Start && text[token.Start - 1] == ':')
            {
                start = prefix.Start;
            }

            var written = text[start..(token.Start + token.Length)];
            return _model.TryResolveWritten(written, namespaces, out var type) ? type : null;
        }

        return null;
    }

    // "Type.Member" written in a value - {x:Static local:Settings.Views} - names the member.
    private ISymbol MemberWrittenAt(string text, int offset, IReadOnlyList<SemToken> tokens,
        IReadOnlyDictionary<string, string> namespaces)
    {
        for (var i = 1; i < tokens.Count; i++)
        {
            var token = tokens[i];
            if (token.TokenType != SemanticTokensEngine.Property || offset < token.Start || offset > token.Start + token.Length)
            {
                continue;
            }

            var type = tokens[i - 1];
            if (type.TokenType != SemanticTokensEngine.Type || type.Start + type.Length + 1 != token.Start || text[token.Start - 1] != '.')
            {
                return null;
            }

            var start = type.Start;
            if (i > 1 && tokens[i - 2] is { TokenType: SemanticTokensEngine.Namespace } prefix
                      && prefix.Start + prefix.Length + 1 == type.Start && text[type.Start - 1] == ':')
            {
                start = prefix.Start;
            }

            return _model.TryResolveWritten(text[start..(type.Start + type.Length)], namespaces, out var owner) && owner != null
                ? MemberSymbol(owner, text.Substring(token.Start, token.Length))
                : null;
        }

        return null;
    }

    // The "[prefix:]Table.Key" of the {Localize} the caret is on, and whether it is on the key rather than the table.
    private static (string Written, bool OnKey)? LocalizedAt(string text, int offset)
    {
        var open = text.LastIndexOf(LocalizeOpening, Math.Min(offset, text.Length - 1), StringComparison.Ordinal);
        if (open < 0)
        {
            return null;
        }

        var start = open + LocalizeOpening.Length;
        if (start >= text.Length || !char.IsWhiteSpace(text[start]))
        {
            return null;
        }

        while (start < text.Length && char.IsWhiteSpace(text[start])) start++;
        var end = start;
        while (end < text.Length && IsNameChar(text[end])) end++;
        if (end == start || offset < start || offset > end)
        {
            return null;
        }

        var written = text[start..end];
        var dot = written.LastIndexOf('.');
        return (written, dot >= 0 && offset > start + dot);
    }

    // A table's phrase as its base language file writes it, or the file itself for the table; a table another assembly
    // makes goes to its code.
    private DefinitionLocation PhraseDefinition(string written, bool onKey, IReadOnlyDictionary<string, string> namespaces,
        string documentPath)
    {
        var (prefix, name) = SplitName(written);
        var dot = name.LastIndexOf('.');
        var tableName = dot < 0 ? name : name[..dot];
        var key = dot < 0 || !onKey ? null : name[(dot + 1)..];
        var tables = _model.TablesNamed(prefix.Length == 0 ? null : ResolveXmlns(prefix, namespaces), tableName).ToList();
        if (tables.Count == 0)
        {
            return null;
        }

        try
        {
            if (PhraseInBaseFile(tableName, key, tables, documentPath) is { } inFile)
            {
                return inFile;
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
        }

        var table = _model.Compilation.GetTypeByMetadataName(tables[0].FullName);
        ISymbol symbol = key == null ? table : table?.GetMembers(key).FirstOrDefault();
        return symbol == null ? null : Locate(symbol);
    }

    private static DefinitionLocation PhraseInBaseFile(string tableName, string key, IReadOnlyList<LanguageTableInfo> tables,
        string documentPath)
    {
        var project = documentPath == null ? null : LanguageProject.Of(documentPath);
        if (project != null)
        {
            var baseName = $"{tableName}.{project.NeutralLanguage}{LanguageFileParser.Extension}";
            foreach (var path in project.LanguageFiles())
            {
                if (!string.Equals(Path.GetFileName(path), baseName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var file = LanguageFileParser.Parse(path, File.ReadAllText(path), project.ProjectDir);
                var fullName = $"{LanguageFileParser.TableNamespace(file.Folder, project.RootNamespace)}.{file.Table}";
                if (tables.All(t => t.FullName != fullName))
                {
                    continue;
                }

                var entry = key == null ? null : file.Entries.FirstOrDefault(e => e.Key == key);
                return entry == null
                    ? new DefinitionLocation(path, 0, 0, 0, 0)
                    : new DefinitionLocation(path, entry.Line - 1, entry.Column - 1, entry.Line - 1, entry.Column - 1);
            }
        }

        return null;
    }

    private DefinitionLocation LocateType(IResolvedType type)
    {
        var symbol = SymbolOf(type);
        if (symbol != null && symbol.Locations.Any(IsHandWritten))
        {
            return Locate(symbol);
        }

        var markup = _model.MarkupFileOf(type);
        if (markup != null)
        {
            return new DefinitionLocation(markup, 0, 0, 0, 0);
        }

        return symbol == null ? null : Locate(symbol);
    }

    private static bool IsHandWritten(Location location) =>
        location.IsInSource && location.SourceTree is { FilePath: { Length: > 0 } path }
                            && !path.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase) && File.Exists(path);

    private DefinitionLocation Locate(ISymbol symbol)
    {
        foreach (var location in symbol.Locations)
        {
            if (!location.IsInSource || location.SourceTree is null) continue;
            var span = location.GetLineSpan();
            return new DefinitionLocation(location.SourceTree.FilePath,
                span.StartLinePosition.Line, span.StartLinePosition.Character,
                span.EndLinePosition.Line, span.EndLinePosition.Character);
        }

        // No source (external assembly) — decompile the type and land on the member.
        return MetadataDecompiler.Locate(symbol, _model.Compilation);
    }

    private IResolvedType ResolveElement(string qualifiedName, IReadOnlyDictionary<string, string> namespaces)
    {
        var (prefix, local) = SplitName(qualifiedName ?? "");
        var xmlns = ResolveXmlns(prefix, namespaces);
        return xmlns.Length == 0 ? null : _model.GetElement(xmlns, local);
    }

    private static ISymbol MemberSymbol(IResolvedType owner, string name, bool attached = false)
    {
        if (owner is null) return null;
        // Attached properties expose Get<Name>/Set<Name> accessors; prefer the getter's declaration.
        var member = (attached ? owner.GetMemberByName("Get" + name) : null)
                     ?? owner.GetMemberByName(name)
                     ?? owner.GetMemberByName("Get" + name);
        return SymbolOf(member);
    }

    private static ISymbol SymbolOf(IResolvedType type) => (type as RoslynResolvedType)?.Symbol;

    private static ISymbol SymbolOf(IResolvedMember member) => (member as RoslynResolvedMember)?.Symbol;

    private static string ResolveXmlns(string prefix, IReadOnlyDictionary<string, string> namespaces) =>
        namespaces.TryGetValue(prefix, out var uri) ? uri : prefix.Length == 0 ? FallbackXmlns : "";

    private static (string Prefix, string Local) SplitName(string name)
    {
        int colon = name.IndexOf(':');
        return colon < 0 ? ("", name) : (name[..colon], name[(colon + 1)..]);
    }

    private static bool IsNameChar(char c) => char.IsLetterOrDigit(c) || c is '_' or '-' or '.' or ':';
}
