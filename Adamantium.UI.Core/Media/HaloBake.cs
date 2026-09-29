using System.Collections.Generic;
using Adamantium.Mathematics;

namespace Adamantium.UI.Core.Media;

/// <summary>Turns an element's <see cref="Aura"/> and <see cref="Shadow"/> into the list of bands the renderer
/// draws.</summary>
public static class HaloBake
{
    /// <summary>The living aura for one element, or null; drawn by its own pass so still glows skip the noise.</summary>
    public static LivingBand? Living(Aura aura, PackPalette packPalette)
    {
        if (aura is not { IsLiving: true }) return null;

        var color = Premultiplied(aura.Color, aura.Opacity);
        if (color.W <= 0f) return null;

        var softness = (float)System.Math.Max(0.0, aura.Radius);
        if (softness <= 0f) return null;

        Vector4F[] palette = null;
        float[] offsets = null;
        var stops = 0;
        if (aura.Palette is { Count: > 1 } && packPalette != null)
        {
            stops = packPalette(aura.Palette, color.W, out palette, out offsets);
        }

        return new LivingBand(
            (float)aura.Spread, softness, aura.Inner,
            (float)System.Math.Clamp(aura.Turbulence, 0.0, 4.0),
            (float)aura.Flow,
            (float)System.Math.Max(0.25, aura.Detail),
            color, palette, offsets, stops);
    }

    /// <summary>How a palette is packed. Injected because the packer lives with the renderer (it is the same one every
    /// gradient uses) while this bake lives with the public types - and there is to be exactly one packer.</summary>
    public delegate int PackPalette(GradientStopCollection stops, float alpha, out Vector4F[] colors, out float[] offsets);

    /// <summary>The bands for one element, or null when it wears none. Called on the RECORD thread. A LIVING aura is not
    /// among them - see <see cref="Living"/>.</summary>
    public static HaloBand[] From(Aura aura, Shadow shadow)
    {
        // A disabled band is baked transparent, not dropped, so toggling keeps the record count (see HaloBand.IsEmpty).
        // A living aura has its own pass and is dropped here.
        if (aura is { IsLiving: true }) aura = null;
        if (aura == null && shadow == null) return null;

        // SHADOW first, then aura - the order they are drawn in. A shadow falls on what lies BEHIND the element, while an
        // aura is light coming off the element itself, so the aura is the nearer of the two and belongs on top. Baked the
        // other way round, a dark shadow sits over the glow and eats it.
        List<HaloBand> bands = null;
        Add(ref bands, FromShadow(shadow));
        Add(ref bands, FromAura(aura));
        return bands?.ToArray();
    }

    private static void Add(ref List<HaloBand> bands, HaloBand band)
    {
        if (band.IsEmpty) return;
        bands ??= [];
        bands.Add(band);
    }

    // An aura has no direction, and its Radius is the whole reach - it is the SOFTNESS, with Spread the solid rim the
    // fade starts outside of. (A shadow splits the same distance differently, which is why they are baked apart.)
    private static HaloBand FromAura(Aura aura)
    {
        if (aura == null) return default;
        // Switched off = alpha zero, NOT absent: the band keeps its geometry and so keeps its record.
        var opacity = aura.IsEnabled ? aura.Opacity : 0.0;
        return new HaloBand(Vector2F.Zero, (float)System.Math.Max(0.0, aura.Spread),
            (float)System.Math.Max(0.0, aura.Radius), Premultiplied(aura.Color, opacity), aura.Inner);
    }

    // A shadow's Spread may be NEGATIVE - a shadow that only peeks out from under one edge - so it is not clamped the
    // way an aura's rim is; only the softness has to stay positive, or the band would be a hard silhouette.
    private static HaloBand FromShadow(Shadow shadow)
    {
        if (shadow == null) return default;
        // Switched off = alpha zero, NOT absent - see FromAura.
        var opacity = shadow.IsEnabled ? shadow.Opacity : 0.0;
        return new HaloBand(new Vector2F((float)shadow.OffsetX, (float)shadow.OffsetY), (float)shadow.Spread,
            (float)System.Math.Max(0.0, shadow.BlurRadius), Premultiplied(shadow.Color, opacity), shadow.Inner);
    }

    private static Vector4F Premultiplied(Color color, double opacity)
    {
        var c = color.ToVector4();
        c.W *= (float)System.Math.Clamp(opacity, 0.0, 1.0);
        return c;
    }
}
