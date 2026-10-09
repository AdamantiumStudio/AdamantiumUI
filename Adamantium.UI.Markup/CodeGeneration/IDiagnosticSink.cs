using Adamantium.Core;
using Adamantium.UI.Markup.AST;

namespace Adamantium.UI.Markup.CodeGeneration;

/// <summary>Where the transformer and the code generator say what is wrong with a document; <c>at</c>, when given, is
/// the place in the markup the problem is written - a build reports it there, an editor marks it there.</summary>
public interface IDiagnosticSink
{
    bool HasErrors { get; }

    void ReportError(string hintName, string message, IAumlLineInfo at = null);

    void ReportWarning(string hintName, string message, IAumlLineInfo at = null);

    void ReportInfo(string hintName, string message, IAumlLineInfo at = null);

    void ReportLogMessage(string hintName, LogMessage message);
}
