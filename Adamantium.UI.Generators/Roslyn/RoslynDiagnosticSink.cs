using Adamantium.Core;
using Adamantium.UI.Markup.AST;
using Adamantium.UI.Markup.CodeGeneration;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace Adamantium.UI.Generators.Roslyn;

/// <summary>The generator's problems as the compiler's: at the place in the markup file each is written, when it is
/// known - a build and an editor point at it - and otherwise with the class it is about.</summary>
public class RoslynDiagnosticSink : IDiagnosticSink
{
    private readonly SourceProductionContext _context;
    private readonly string _file;

    public RoslynDiagnosticSink(SourceProductionContext context, string file = null)
    {
        _context = context;
        _file = file;
    }

    public bool HasErrors { get; private set; }

    public void ReportError(string hintName, string message, IAumlLineInfo at = null)
    {
        CreateDiagnostic(hintName, message, DiagnosticSeverity.Error, at);
        HasErrors = true;
    }

    public void ReportWarning(string hintName, string message, IAumlLineInfo at = null)
    {
        CreateDiagnostic(hintName, message, DiagnosticSeverity.Warning, at);
    }

    public void ReportInfo(string hintName, string message, IAumlLineInfo at = null)
    {
        CreateDiagnostic(hintName, message, DiagnosticSeverity.Info, at);
    }

    public void ReportLogMessage(string hintName, LogMessage message)
    {
        var severity = message.Type switch
        {
            LogMessageType.Info => DiagnosticSeverity.Info,
            LogMessageType.Warning => DiagnosticSeverity.Warning,
            _ => DiagnosticSeverity.Error
        };
        CreateDiagnostic(hintName, $"{message}", severity, null);
        HasErrors = true;
    }

    private void CreateDiagnostic(string className, string diagnosticText, DiagnosticSeverity severity, IAumlLineInfo at)
    {
        if (_file != null && at is { Line: > 0 })
        {
            var position = new LinePosition(at.Line - 1, System.Math.Max(0, at.Position - 1));
            var location = Location.Create(_file, default, new LinePositionSpan(position, position));
            _context.ReportDiagnostic(Diagnostic.Create(Descriptor(severity, "{0}"), location, diagnosticText));
            return;
        }

        _context.ReportDiagnostic(Diagnostic.Create(Descriptor(severity, "{0}: {1}"), Location.None, $"{className}.g.cs", diagnosticText));
    }

    private static DiagnosticDescriptor Descriptor(DiagnosticSeverity severity, string format) =>
        new(id: "AUM001", title: $"Auml {severity}", messageFormat: format, category: "Auml", severity, isEnabledByDefault: true);
}
