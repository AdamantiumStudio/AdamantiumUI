using System.Collections.Generic;
using Adamantium.Mathematics;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Graphics;
using Adamantium.UI.Rendering.RenderUnits;
using Adamantium.Vulkan.Core;

namespace Adamantium.UI.Rendering;

public partial class RenderCache
{
    private readonly HashSet<TextRenderUnit> _bandedText = new();
    private readonly List<TextRenderUnit> _bandsDropped = new();

    private void NoteBandedText(IRenderUnit unit)
    {
        if (unit is TextRenderUnit { HasBand: true } text)
        {
            _bandedText.Add(text);
        }
    }

    private bool FitTextBands(Rect2D fullScissor)
    {
        if (_bandedText.Count == 0)
        {
            return false;
        }

        var window = new Rect(fullScissor.Offset.X / _renderScale, fullScissor.Offset.Y / _renderScale,
            fullScissor.Extent.Width / _renderScale, fullScissor.Extent.Height / _renderScale);
        var rebuilt = false;
        _bandsDropped.Clear();
        foreach (var text in _bandedText)
        {
            if (!text.HasBand)
            {
                _bandsDropped.Add(text);
                continue;
            }

            var shown = CumulativeClip(text.Component) is { } clip ? clip.Intersect(window) : window;
            var band = shown.Inflate(new Thickness(window.Width, window.Height));
            rebuilt |= text.FitBand(World(text.Component), shown, band);
        }

        foreach (var text in _bandsDropped)
        {
            _bandedText.Remove(text);
        }

        return rebuilt;
    }
}
