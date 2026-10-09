using Adamantium.Core;
using Adamantium.UI.Markup.AST;
using Adamantium.UI.Markup.CodeGeneration;

namespace Adamantium.UI.Core.Markup;

/// <summary>Collects the transformer's diagnostics into a flat string list for the loader result.</summary>
internal sealed class ListDiagnosticSink : IDiagnosticSink
{
    private readonly List<string> _messages;
    public ListDiagnosticSink(List<string> messages) => _messages = messages;

    public bool HasErrors { get; private set; }

    public void ReportError(string hintName, string message, IAumlLineInfo at = null) { HasErrors = true; _messages.Add($"error: {message}{Where(at)}"); }
    public void ReportWarning(string hintName, string message, IAumlLineInfo at = null) => _messages.Add($"warning: {message}{Where(at)}");
    public void ReportInfo(string hintName, string message, IAumlLineInfo at = null) => _messages.Add($"info: {message}{Where(at)}");
    public void ReportLogMessage(string hintName, LogMessage message) => _messages.Add(message?.ToString());

    private static string Where(IAumlLineInfo at) => at == null || at.Line <= 0 ? string.Empty : $" ({at.Line},{at.Position})";
}
