namespace Adamantium.UI.LanguageServer;

internal sealed class FormatNode
{
    public string Name { get; init; }

    public string Raw { get; init; }

    public bool IsText { get; init; }

    public List<(string Name, string Value)> Attributes { get; } = [];

    public List<FormatNode> Children { get; } = [];

    public int Start { get; init; }

    public int TagEnd { get; set; }

    public int ContentEnd { get; set; }

    public int End { get; set; }

    public bool IsSelfClosing { get; set; }

    public bool BlankLineBefore { get; set; }

    public bool BlankLineBeforeEnd { get; set; }
}
