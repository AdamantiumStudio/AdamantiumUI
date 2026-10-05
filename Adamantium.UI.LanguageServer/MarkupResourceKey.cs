namespace Adamantium.UI.LanguageServer;

/// <summary>A key a markup file declares, as read from its text: what makes it - an element, or a palette color's type -
/// and where the key is written.</summary>
internal sealed record MarkupResourceKey(string Key, string ElementNamespace, string ElementName, string ValueTypeName,
    string File, int Line, int Character);
