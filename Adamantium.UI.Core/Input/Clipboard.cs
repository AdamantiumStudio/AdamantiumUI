namespace Adamantium.UI.Core.Input;

/// <summary>
/// Clipboard facade. Defaults to an in-process clipboard (copy/paste works within the app with no platform code),
/// which a platform replaces with the real OS clipboard via <see cref="Current"/> at startup. Controls call
/// <see cref="GetText"/> / <see cref="SetText"/>, or <see cref="SetData"/> / <see cref="GetData"/> for formats, and stay
/// platform-agnostic.
/// </summary>
public static class Clipboard
{
    private sealed class InProcessClipboard : IClipboard
    {
        private IDataPackage _data = new DataPackage();

        public string GetText() => _data.Get(DataFormats.Text) as string ?? string.Empty;

        public bool HasText() => GetText().Length > 0;

        public void SetText(string text)
        {
            _data = new DataPackage();
            _data.Set(DataFormats.Text, text ?? string.Empty);
        }

        public void SetData(IDataPackage package) => _data = package ?? new DataPackage();

        public bool Contains(string format) => _data.Contains(format);

        public object GetData(string format) => _data.Get(format);
    }

    /// <summary>The active provider. A platform sets this to the OS clipboard; until then an in-process store is used.</summary>
    public static IClipboard Current { get; set; } = new InProcessClipboard();

    public static string GetText() => Current?.GetText() ?? string.Empty;

    /// <summary>Whether there is text to paste, without reading it.</summary>
    public static bool HasText() => Current?.HasText() == true;

    public static void SetText(string text) => Current?.SetText(text ?? string.Empty);

    /// <summary>Puts several formats of one thing on the clipboard at once - text, HTML, a format of one's own.</summary>
    public static void SetData(IDataPackage package) => Current?.SetData(package);

    public static bool Contains(string format) => Current?.Contains(format) == true;

    public static object GetData(string format) => Current?.GetData(format);
}
