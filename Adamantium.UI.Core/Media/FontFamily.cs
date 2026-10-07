using System;
using System.Collections.Generic;
using Adamantium.Fonts;

namespace Adamantium.UI.Core.Media;

public class FontFamily
{
    private readonly string _name;
    private readonly Dictionary<(FontWeight Weight, FontStyle Style, FontStretch Stretch), IFont> _faces = new();

    /// <summary>A family of the system's fonts, by its name ("Segoe UI"); its faces are picked by weight, slant and
    /// width (<see cref="GetFont"/>).</summary>
    public FontFamily(string fontName)
    {
        _name = fontName;
        Typeface = TypefaceStore.GetTypeface(fontName, true);
    }

    /// <summary>The one font of a file: every weight and slant draws with it.</summary>
    public FontFamily(Uri fontPath)
    {
        Typeface = TypefaceStore.GetTypeface(fontPath.OriginalString);
    }

    public Typeface Typeface { get; }

    public IReadOnlyList<IFont> Fonts => Typeface.Fonts;

    public double LineGap => Typeface.GetFont(0).LineGap;

    /// <summary>The face of the family nearest to the weight, slant and width asked for, picked as CSS picks it (the
    /// width first, then the slant, then the weight): with no bold face, a bold request gets the heaviest there is. A
    /// family made from a file answers with its one font.</summary>
    public IFont GetFont(FontWeight weight, FontStyle style, FontStretch stretch)
    {
        if (_name == null)
        {
            return Fonts[0];
        }

        lock (_faces)
        {
            if (_faces.TryGetValue((weight, style, stretch), out var font))
            {
                return font;
            }

            var face = FontCollection.System.Match(_name, weight, style, stretch);
            font = face == null ? Fonts[0] : FontCollection.Load(face);
            _faces[(weight, style, stretch)] = font;
            return font;
        }
    }
}
