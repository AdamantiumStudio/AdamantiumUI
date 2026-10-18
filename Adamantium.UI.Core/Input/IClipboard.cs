namespace Adamantium.UI.Core.Input;

/// <summary>Platform text-clipboard access. A platform (e.g. Win32) registers an implementation on
/// <see cref="Clipboard.Current"/>; controls use the <see cref="Clipboard"/> facade and never see the platform.</summary>
public interface IClipboard
{
    string GetText();

    /// <summary>Whether there is text to paste, without reading it: asked whenever a Paste row or button checks.</summary>
    bool HasText();

    void SetText(string text);
}
