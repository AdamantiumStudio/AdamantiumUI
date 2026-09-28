namespace Adamantium.UI.Core.Input;

/// <summary>Platform-neutral names of the standard formats an <see cref="IDataPackage"/> carries across processes; each
/// platform maps them to its own. In-app CLR objects travel under their type name.</summary>
public static class DataFormats
{
    /// <summary>Unicode text. Value type: <see cref="string"/>.</summary>
    public const string Text = "Text";

    /// <summary>A file/folder drop. Value type: <c>string[]</c> of absolute paths.</summary>
    public const string Files = "Files";

    /// <summary>HTML markup. Value type: <see cref="string"/> - write the fragment you mean, the platform wraps it in
    /// whatever its own format demands (Windows <c>CF_HTML</c> with its byte-offset header, macOS
    /// <c>NSPasteboardTypeHTML</c>, Linux <c>text/html</c>).</summary>
    public const string Html = "Html";

    /// <summary>Rich Text Format. Value type: <see cref="string"/> holding the RTF source.</summary>
    public const string Rtf = "Rtf";

    /// <summary>A picture as encoded <c>byte[]</c> in its original format, never re-encoded; decode by content with an
    /// image loader.</summary>
    public const string Image = "Image";

    // Anything else is a format of your own: name it what you like and store a byte[]. It crosses as a registered
    // platform format under that same name, so two applications that agree on the name interoperate without the engine
    // knowing anything about the payload.
}
