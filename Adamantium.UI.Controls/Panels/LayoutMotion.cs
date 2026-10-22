using System;
using Adamantium.Mathematics;
using Adamantium.UI.Controls.Base;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Media;
using Adamantium.UI.Core.Media.Animation;

namespace Adamantium.UI.Controls.Panels;

internal static class LayoutMotion
{
    internal static readonly TimeSpan Duration = TimeSpan.FromSeconds(0.18);

    internal static void Promote(UIComponent element, bool on)
    {
        if (element.IsRenderMotionNode == on)
        {
            return;
        }

        element.IsRenderMotionNode = on;
        RenderDirty.MarkTransform(element);
        RenderDirty.MarkSubtreeGeometry(element);
    }

    internal static void Slide(UIComponent element, Vector2 offset) => Morph(element, offset, 1, 1);

    internal static void Morph(UIComponent element, Vector2 offset, double scaleX, double scaleY, Action landed = null)
    {
        if (element.RenderTransform is not { } transform)
        {
            transform = new Transform();
            element.RenderTransform = transform;
        }

        Run(transform, Transform.TranslateXProperty, offset.X, 0, landed);
        Run(transform, Transform.TranslateYProperty, offset.Y, 0);
        if (scaleX != 1 || scaleY != 1)
        {
            Run(transform, Transform.ScaleXProperty, scaleX, 1);
            Run(transform, Transform.ScaleYProperty, scaleY, 1);
        }
    }

    private static void Run(Transform transform, AdamantiumProperty property, double from, double to, Action landed = null) =>
        transform.BeginAnimation(property,
            new DoubleAnimation { From = from, To = to, Duration = Duration, FillBehavior = FillBehavior.Stop }, landed);
}
