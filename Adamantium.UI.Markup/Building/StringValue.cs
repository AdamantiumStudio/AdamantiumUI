namespace Adamantium.UI.Markup.Building;

/// <summary>A string.</summary>
public sealed class StringValue(string text, bool escaped = true) : BuildValue
{
    /// <summary>The string itself.</summary>
    public string Text { get; } = text;

    /// <summary>Whether the generated code escapes quotes, backslashes and line breaks in it.</summary>
    public bool Escaped { get; } = escaped;
}
