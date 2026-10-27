using System;
using System.Collections.Generic;
using System.Linq;
using Adamantium.Core.Fonts;
using Adamantium.Fonts;
using Adamantium.UI.Core.Resources;

namespace Adamantium.UI.Core.Media;

public class FontFamily
{
    private static Typeface _standIn;

    private readonly string _name;
    private readonly Typeface _typeface;
    private readonly Dictionary<(FontWeight Weight, FontStyle Style, FontStretch Stretch), IFont> _faces = new();

    /// <summary>A family of the system's fonts, by its name ("Segoe UI"); its faces are picked by weight, slant and
    /// width (<see cref="GetFont"/>).</summary>
    public FontFamily(string fontName)
    {
        _name = fontName;
        _typeface = TypefaceStore.GetTypeface(fontName, true);
    }

    /// <summary>The one font of a file: every weight and slant draws with it.</summary>
    public FontFamily(Uri fontPath)
    {
        _typeface = TypefaceStore.GetTypeface(fontPath.OriginalString);
    }

    /// <summary>A family of the system's fonts by its name, as markup writes it: FontFamily="Bahnschrift".</summary>
    public static FontFamily Parse(string text)
    {
        return new FontFamily(text.Trim());
    }

    /// <summary>Whether the system has the family. One it does not have keeps its name and draws in the system's UI
    /// font (<see cref="Theme.SystemDefaultFontFamily"/>), or failing that in any font the system has.</summary>
    public bool IsInstalled => _typeface != null;

    public Typeface Typeface => _typeface ?? StandIn;

    /// <summary>The family's name, as WPF's: the one it was made with, or for a family made from a file the name its
    /// font gives - what a font box shows for it.</summary>
    public string Source => _name ?? (Fonts.Count > 0 ? Fonts[0].FontFamily : null);

    public IReadOnlyList<IFont> Fonts => Typeface.Fonts;

    public double LineGap => Typeface.GetFont(0).LineGap;

    /// <summary>The face of the family nearest to the weight, slant and width asked for, picked as CSS picks it (the
    /// width first, then the slant, then the weight): with no bold face, a bold request gets the heaviest there is; a
    /// variable face is set to the weight, width and slant asked for. A family made from a file answers with its one
    /// font, at the weight, width and slant asked for when the font is variable.</summary>
    public IFont GetFont(FontWeight weight, FontStyle style, FontStretch stretch)
    {
        Find(weight, style, stretch, true, out var font);
        return font;
    }

    /// <summary>The face <see cref="GetFont"/> gives, when its file is already parsed. Otherwise false, with the face
    /// the family was made with standing in, and the file is parsed on a worker that raises
    /// <see cref="TypefaceStore.Loaded"/> when done. Never waits for a file.</summary>
    public bool TryGetFont(FontWeight weight, FontStyle style, FontStretch stretch, out IFont font)
    {
        return Find(weight, style, stretch, false, out font);
    }

    private bool Find(FontWeight weight, FontStyle style, FontStretch stretch, bool wait, out IFont font)
    {
        lock (_faces)
        {
            if (_faces.TryGetValue((weight, style, stretch), out font))
            {
                return true;
            }
        }

        if (_name == null)
        {
            font = Fonts[0].GetInstance(FontVariation.For(weight, stretch, style, Fonts[0]));
        }
        else
        {
            var face = FontCollection.System.Match(_name, weight, style, stretch);
            if (face == null)
            {
                font = Fonts[0];
            }
            else if (wait)
            {
                font = FontCollection.Load(face, weight, stretch, style);
            }
            else if (!FontCollection.TryLoad(face, weight, stretch, style, out font))
            {
                font = Fonts[0];
                return false;
            }
        }

        lock (_faces)
        {
            _faces[(weight, style, stretch)] = font;
        }

        return true;
    }

    private static Typeface StandIn => _standIn ??= Theme.SystemDefaultFontFamily._typeface ??
                                                    FontCollection.System.Families.Select(name =>
                                                        TypefaceStore.GetTypeface(name, true)).FirstOrDefault(
                                                        typeface => typeface != null);
}
