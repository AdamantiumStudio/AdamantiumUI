using System.Collections.Generic;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Media;

namespace Adamantium.UI.Controls.Text;

internal sealed class HeldBrushes(AdamantiumComponent owner)
{
    private readonly List<Brush> _wanted = [];
    private readonly List<Brush> _held = [];
    private int _releases;

    public bool Contains(Brush brush) => _wanted.Contains(brush);

    public void Clear() => _wanted.Clear();

    public void Add(Brush brush)
    {
        if (brush != null && !_wanted.Contains(brush))
        {
            _wanted.Add(brush);
        }
    }

    public void Take()
    {
        // Let go of with the rest since: not held any more.
        if (_releases != owner.RenderAttachmentReleases)
        {
            _releases = owner.RenderAttachmentReleases;
            _held.Clear();
        }

        if (owner.RenderAttachmentsReleased)
        {
            return;
        }

        foreach (var brush in _held)
        {
            if (!_wanted.Contains(brush))
            {
                ((IRenderAttachable)brush).DetachFrom(owner);
            }
        }

        foreach (var brush in _wanted)
        {
            if (!_held.Contains(brush))
            {
                ((IRenderAttachable)brush).AttachTo(owner);
            }
        }

        _held.Clear();
        _held.AddRange(_wanted);
    }

    public void Released() => _held.Clear();
}
