using Adamantium.Localization;

namespace Adamantium.UI.LanguageServer;

/// <summary>What the language server says itself - its own problems, quick fixes, hovers and completion details - in
/// the language of the current UI culture, which the client names when it starts the server; each method is one
/// message.</summary>
public static class ServerMessages
{
    /// <summary>The catalog the messages are read from.</summary>
    public static MessageCatalog Catalog { get; } = new(typeof(ServerMessages).Assembly, nameof(ServerMessages));

    public static string PrefixXUndeclared() => Say(nameof(PrefixXUndeclared));
    public static string ClrNamespaceNotFound(string uri) => Say(nameof(ClrNamespaceNotFound), new PhraseArgument("uri", uri));
    public static string UnknownElement(string name) => Say(nameof(UnknownElement), new PhraseArgument("name", name));
    public static string ElementNotInScope(string name) => Say(nameof(ElementNotInScope), new PhraseArgument("name", name));

    public static string UnknownProperty(string property, string element) =>
        Say(nameof(UnknownProperty), new PhraseArgument("property", property), new PhraseArgument("element", element));

    public static string InvalidEnumValue(string value, string type, string allowed) =>
        Say(nameof(InvalidEnumValue), new PhraseArgument("value", value), new PhraseArgument("type", type),
            new PhraseArgument("allowed", allowed));

    public static string NoProject() => Say(nameof(NoProject));
    public static string NotBuilt(string project) => Say(nameof(NotBuilt), new PhraseArgument("project", project));

    public static string ResourceKeyShadowed(string key, string where) =>
        Say(nameof(ResourceKeyShadowed), new PhraseArgument("key", key), new PhraseArgument("where", where));

    public static string QualifyWith(string name) => Say(nameof(QualifyWith), new PhraseArgument("name", name));

    public static string ImportForPrefix(string uri, string prefix) =>
        Say(nameof(ImportForPrefix), new PhraseArgument("uri", uri), new PhraseArgument("prefix", prefix));

    public static string DeclareDefaultXmlns(string uri) => Say(nameof(DeclareDefaultXmlns), new PhraseArgument("uri", uri));

    public static string ImportAs(string uri, string prefix) =>
        Say(nameof(ImportAs), new PhraseArgument("uri", uri), new PhraseArgument("prefix", prefix));

    public static string DeclareXmlns(string prefix, string uri) =>
        Say(nameof(DeclareXmlns), new PhraseArgument("prefix", prefix), new PhraseArgument("uri", uri));

    public static string AddString(string key, string table) =>
        Say(nameof(AddString), new PhraseArgument("key", key), new PhraseArgument("table", table));

    public static string AddStrings(int count, string table) =>
        Say(nameof(AddStrings), new PhraseArgument("count", count), new PhraseArgument("table", table));

    public static string RenameNeedsKey() => Say(nameof(RenameNeedsKey));
    public static string KeyNotDeclaredHere(string key) => Say(nameof(KeyNotDeclaredHere), new PhraseArgument("key", key));

    public static string KeyDeclaredInMany(string key, string files) =>
        Say(nameof(KeyDeclaredInMany), new PhraseArgument("key", key), new PhraseArgument("files", files));

    public static string And(string first, string second) =>
        Say(nameof(And), new PhraseArgument("first", first), new PhraseArgument("second", second));

    public static string KeyNameInvalid(string name) => Say(nameof(KeyNameInvalid), new PhraseArgument("name", name));

    public static string Element() => Say(nameof(Element));
    public static string Directive() => Say(nameof(Directive));
    public static string Inherits(string type) => Say(nameof(Inherits), new PhraseArgument("type", type));
    public static string On(string type) => Say(nameof(On), new PhraseArgument("type", type));

    public static string StringIn(string key, string table) =>
        Say(nameof(StringIn), new PhraseArgument("key", key), new PhraseArgument("table", table));

    public static string NoText() => Say(nameof(NoText));
    public static string LanguageTable() => Say(nameof(LanguageTable));
    public static string Placeholder() => Say(nameof(Placeholder));
    public static string DashGlyph() => Say(nameof(DashGlyph));
    public static string Folder() => Say(nameof(Folder));
    public static string File() => Say(nameof(File));
    public static string LanguageFileRoot() => Say(nameof(LanguageFileRoot));
    public static string TablePhrase() => Say(nameof(TablePhrase));
    public static string LanguageFormat() => Say(nameof(LanguageFormat));
    public static string OwnFormat(string language) => Say(nameof(OwnFormat), new PhraseArgument("language", language));

    private static string Say(string key, params PhraseArgument[] values) => Catalog.Say(key, values);
}
