using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace Adamantium.UI.LanguageServer;

/// <summary>A language file handed to the generator: the text on disk, or the editor's when the file is open.</summary>
public sealed class LanguageText : AdditionalText
{
    private readonly SourceText _text;

    public LanguageText(string path, string text)
    {
        Path = path;
        _text = SourceText.From(text);
    }

    public override string Path { get; }

    public override SourceText GetText(CancellationToken cancellationToken = default) => _text;
}
