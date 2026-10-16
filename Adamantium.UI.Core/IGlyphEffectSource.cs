using System.Collections.Generic;
using Adamantium.Graphics.Fonts;

namespace Adamantium.UI.Core;

/// <summary>A component whose text is drawn with glyph effects that change apart from the text: it marks
/// <see cref="RenderDirty.MarkEffects"/>, and the renderer reads the effects as they are now, without drawing the text
/// again.</summary>
public interface IGlyphEffectSource
{
    /// <summary>Adds the effects the text is drawn with right now to <paramref name="effects"/>, in the order they
    /// apply.</summary>
    void ResolveEffects(List<GlyphEffect> effects);
}
