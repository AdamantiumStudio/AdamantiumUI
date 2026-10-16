using System;
using System.Collections.Generic;
using Adamantium.UI.Core.Media;

namespace Adamantium.UI.Controls.Text;

/// <summary>Splits one effect into an effect a character, as WPF's text effect resolver, so the characters can be moved
/// one by one: a wave through a title, letters arriving in turn.</summary>
public static class TextEffectResolver
{
    /// <summary>Adds to <paramref name="effects"/> an effect for each of <paramref name="count"/> characters from
    /// <paramref name="start"/>, in order. Each takes the <paramref name="template"/>'s foreground and origin and a
    /// transform of its own holding the template's values, to be animated apart from the others.</summary>
    public static void Resolve(TextEffect template, int start, int count, ICollection<TextEffect> effects)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(effects);
        for (var i = 0; i < count; i++)
        {
            effects.Add(new TextEffect
            {
                PositionStart = start + i,
                PositionCount = 1,
                Foreground = template.Foreground,
                TransformOrigin = template.TransformOrigin,
                Transform = Copy(template.Transform),
            });
        }
    }

    private static Transform Copy(Transform transform)
    {
        if (transform == null)
        {
            return new Transform();
        }

        return new Transform
        {
            ScaleX = transform.ScaleX,
            ScaleY = transform.ScaleY,
            RotationAngle = transform.RotationAngle,
            RotationX = transform.RotationX,
            RotationY = transform.RotationY,
            Perspective = transform.Perspective,
            RotationCenterX = transform.RotationCenterX,
            RotationCenterY = transform.RotationCenterY,
            TranslateX = transform.TranslateX,
            TranslateY = transform.TranslateY,
            ShiftX = transform.ShiftX,
            ShiftY = transform.ShiftY,
            SkewX = transform.SkewX,
            SkewY = transform.SkewY,
        };
    }
}
