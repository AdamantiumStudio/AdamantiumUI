using Adamantium.UI.Markup.AST;

namespace Adamantium.UI.Markup.Parsers;

/// <summary>What makes a document unreadable as AUML, and where it is written; <see cref="At"/> is null when the place
/// is not known.</summary>
public sealed class AumlParseError
{
    public AumlParseError(string message, IAumlLineInfo at)
    {
        Message = message;
        At = at;
    }

    public string Message { get; }

    public IAumlLineInfo At { get; }
}
