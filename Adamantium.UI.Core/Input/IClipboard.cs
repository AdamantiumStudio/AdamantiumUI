namespace Adamantium.UI.Core.Input;

/// <summary>Platform clipboard access. A platform (e.g. Win32) registers an implementation on
/// <see cref="Clipboard.Current"/>; controls use the <see cref="Clipboard"/> facade and never see the platform.</summary>
public interface IClipboard
{
    string GetText();

    /// <summary>Whether there is text to paste, without reading it: asked whenever a Paste row or button checks.</summary>
    bool HasText();

    void SetText(string text);

    /// <summary>Puts the package's formats on the clipboard in place of what was there: <see cref="DataFormats.Text"/>,
    /// <see cref="DataFormats.Html"/> and <see cref="DataFormats.Rtf"/> as strings, and formats of one's own as
    /// <c>byte[]</c> under their name, which another application that knows the name can read. Pictures, files and
    /// live objects stay behind; a package with none of the formats above leaves the clipboard as it was.</summary>
    void SetData(IDataPackage package);

    /// <summary>Whether the clipboard holds this format, without reading it.</summary>
    bool Contains(string format);

    /// <summary>The value of a format: a string for text, HTML (its fragment) and RTF, <c>byte[]</c> for one's own -
    /// which may run on past what was written with zeros; null when the clipboard has none.</summary>
    object GetData(string format);
}
