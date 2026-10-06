namespace Adamantium.UI.LanguageServer;

internal static class TextPositions
{
    public static (int Line, int Character) LineAndCharacter(string text, int offset)
    {
        var line = 0;
        var lineStart = 0;
        for (var i = 0; i < offset && i < text.Length; i++)
        {
            if (text[i] == '\n')
            {
                line++;
                lineStart = i + 1;
            }
        }

        return (line, offset - lineStart);
    }
}
