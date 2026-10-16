namespace Adamantium.UI.Core.Text;

/// <summary>A stretch of a text: where it starts and how many UTF-16 characters it holds.</summary>
public readonly record struct TextRange(int Start, int Length)
{
    public int End => Start + Length;
}
