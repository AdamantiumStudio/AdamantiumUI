using System.Collections.Concurrent;
using System.Globalization;
using Adamantium.UI.Core.Localization;

namespace Adamantium.UI.Controls.Text;

internal static class TextCasing
{
    private const char CombiningYpogegrammeni = (char)0x0345;

    private static readonly ConcurrentDictionary<string, CultureInfo> Found = new();

    public static string Upper(string text, string language) => text?.ToUpper(CultureOf(language));

    public static string Lower(string text, string language) => text?.ToLower(CultureOf(language));

    public static CultureInfo CultureOf(string language)
    {
        var tag = language?.Trim();
        var own = string.IsNullOrEmpty(tag) ? null : Found.GetOrAdd(tag, LookUp);
        return own ?? LookUp(Languages.Current) ?? CultureInfo.InvariantCulture;
    }

    public static string Transform(string text, TextTransform transform, CultureInfo culture)
    {
        if (string.IsNullOrEmpty(text) || transform == TextTransform.None)
        {
            return text;
        }

        var shown = transform == TextTransform.Uppercase ? text.ToUpper(culture) : text.ToLower(culture);
        if (shown.Length != text.Length || text.IndexOf(CombiningYpogegrammeni) < 0)
        {
            return shown;
        }

        var kept = shown.ToCharArray();
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == CombiningYpogegrammeni)
            {
                kept[i] = CombiningYpogegrammeni;
            }
        }

        return new string(kept);
    }

    internal sealed class Shown
    {
        private string _from;
        private TextTransform _transform;
        private CultureInfo _culture;
        private string _shown;

        public string Of(string text, TextTransform transform, string language)
        {
            if (transform == TextTransform.None)
            {
                return text;
            }

            var culture = CultureOf(language);
            if (!ReferenceEquals(text, _from) || transform != _transform || !ReferenceEquals(culture, _culture))
            {
                _from = text;
                _transform = transform;
                _culture = culture;
                _shown = Transform(text, transform, culture);
            }

            return _shown;
        }
    }

    private static CultureInfo LookUp(string language)
    {
        for (var tag = language?.Trim() ?? string.Empty; tag.Length > 0;
             tag = tag.LastIndexOf('-') is > 0 and var dash ? tag[..dash] : string.Empty)
        {
            try
            {
                return Languages.CultureFor(tag);
            }
            catch (CultureNotFoundException)
            {
            }
        }

        return null;
    }
}
