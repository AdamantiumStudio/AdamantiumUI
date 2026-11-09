using System;
using System.Collections.Generic;
using Adamantium.Graphics.Fonts;
using Adamantium.Mathematics;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Media;

namespace Adamantium.UI.Controls.Text;

public abstract partial class TextBoxBase
{
    private readonly HeldBrushes _effectBrushes;
    private readonly List<GlyphEffect> _resolvedEffects = [];
    private GlyphEffect[] _drawnEffects = [];
    private TextEffectCollection _textEffects;

    /// <summary>Ranges of the text drawn apart from the rest, as a <see cref="TextBlock"/>'s: moved, turned, scaled or
    /// recolored. Their positions count the box's text, a line break one. Only the drawing changes - the caret,
    /// selection and hit testing stay where the layout put them - so changing them draws the text again without laying
    /// it out.</summary>
    public TextEffectCollection TextEffects
    {
        get
        {
            if (_textEffects == null)
            {
                _textEffects = new TextEffectCollection(this);
                _textEffects.Changed += OnTextEffectsChanged;
            }

            return _textEffects;
        }
    }

    private void OnTextEffectsChanged(object sender, EventArgs e) => InvalidateSurface();

    /// <summary>Discards the effects with the box: they are not in the visual tree, so nothing else reaches them.
    /// </summary>
    protected override void OnDiscarded()
    {
        base.OnDiscarded();
        if (_textEffects == null)
        {
            return;
        }

        foreach (var effect in _textEffects)
        {
            DiscardedVisuals.Publish(effect);
        }
    }

    internal override void OnRenderValueChanged(object sender, EventArgs e)
    {
        if (sender is Brush brush && _effectBrushes.Contains(brush))
        {
            InvalidateSurface();
        }

        base.OnRenderValueChanged(sender, e);
    }

    // The effects as drawn at the text's origin in the surface, the same array while they stay the same.
    private GlyphEffect[] DrawnEffects(Vector2F area)
    {
        _effectBrushes.Clear();
        _resolvedEffects.Clear();
        var length = _textLayout?.Text?.Length ?? 0;
        for (var i = 0; i < (_textEffects?.Count ?? 0); i++)
        {
            var effect = _textEffects[i];
            if (effect.TryResolve(0, length, area, _presenter?.RenderSize ?? default, out var resolved))
            {
                _resolvedEffects.Add(resolved);
                _effectBrushes.Add(effect.Foreground);
            }
        }

        _effectBrushes.Take();
        if (!SameAsDrawn())
        {
            _drawnEffects = [.. _resolvedEffects];
        }

        return _drawnEffects;
    }

    private bool SameAsDrawn()
    {
        if (_drawnEffects.Length != _resolvedEffects.Count)
        {
            return false;
        }

        for (var i = 0; i < _drawnEffects.Length; i++)
        {
            if (_drawnEffects[i] != _resolvedEffects[i])
            {
                return false;
            }
        }

        return true;
    }
}
