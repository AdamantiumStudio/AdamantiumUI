namespace Adamantium.UI.LanguageServer;

internal sealed record MarkupResourceKey(string Key, string ElementNamespace, string ElementName, string ValueTypeName,
    string File, int Line, int Character);
