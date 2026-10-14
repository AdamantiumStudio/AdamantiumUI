namespace Adamantium.UI.Markup.Building;

/// <summary>A number or a boolean as written in C#; it takes the type of wherever it goes.</summary>
public sealed class LiteralValue(string text) : BuildValue
{
    /// <summary>The literal's text.</summary>
    public string Text { get; } = text;
}
