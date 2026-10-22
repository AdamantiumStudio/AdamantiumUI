namespace Adamantium.UI.Core.Input;

/// <summary>The text selected last, which a middle click pastes: a second clipboard of Linux desktops.</summary>
public interface IPrimarySelection
{
    /// <summary>The selected text, from whichever application selected it; empty when there is none.</summary>
    string GetText();

    /// <summary>Makes <paramref name="text"/> the selected text.</summary>
    void SetText(string text);
}
