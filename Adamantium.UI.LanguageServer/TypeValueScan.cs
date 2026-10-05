namespace Adamantium.UI.LanguageServer;

internal sealed class TypeValueScan(AumlTypeModel model, string text, IReadOnlyDictionary<string, string> namespaces,
    List<AumlDiagnostic> diagnostics)
{
    public AumlTypeModel Model { get; } = model;

    public string Text { get; } = text;

    public IReadOnlyDictionary<string, string> Namespaces { get; } = namespaces;

    public List<AumlDiagnostic> Diagnostics { get; } = diagnostics;

    public int Cursor { get; set; }
}
