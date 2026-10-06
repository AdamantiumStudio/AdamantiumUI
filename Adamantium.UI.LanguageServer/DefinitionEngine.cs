using Adamantium.UI.Markup.CodeGeneration;
using Adamantium.UI.Generators.Roslyn;
using Microsoft.CodeAnalysis;

namespace Adamantium.UI.LanguageServer;

/// <summary>
/// Resolves the declaration of what is under the caret (go-to-definition): a resource key goes to the markup that
/// declares it; a type - an element's, or one written as a value - to its C# file, or to its markup file when the build
/// makes the class from markup; an attribute to its C# property. In-repo types —
/// including the engine base controls, which the source graph compiles from source — resolve to their real
/// source file; types that live only in an external assembly are decompiled on demand (see
/// <see cref="MetadataDecompiler"/>). Reuses the hover token detector by placing the caret at the token end.
/// </summary>
public sealed class DefinitionEngine
{
    private const string FallbackXmlns = "http://adamantium/ui";

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
        if (TypeWrittenAt(text, offset, namespaces) is { } written)
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

    private IResolvedType TypeWrittenAt(string text, int offset, IReadOnlyDictionary<string, string> namespaces)
    {
        var tokens = SemanticTokensEngine.Tokenize(text, _model);
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
