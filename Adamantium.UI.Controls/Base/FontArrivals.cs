using System;
using System.Collections.Generic;
using Adamantium.Fonts;
using Adamantium.UI.Core;

namespace Adamantium.UI.Controls.Base;

internal static class FontArrivals
{
    private static readonly List<WeakReference<UIComponent>> Waiting = [];
    private static bool _subscribed;

    public static void Wait(UIComponent component, int loadsSeen)
    {
        lock (Waiting)
        {
            if (!_subscribed)
            {
                TypefaceStore.Loaded += OnLoaded;
                _subscribed = true;
            }

            if (TypefaceStore.LoadedCount != loadsSeen)
            {
                LoopSignal.Post(component.OnFontsArrived);
                return;
            }

            foreach (var waiting in Waiting)
            {
                if (waiting.TryGetTarget(out var known) && ReferenceEquals(known, component))
                {
                    return;
                }
            }

            Waiting.Add(new WeakReference<UIComponent>(component));
        }
    }

    private static void OnLoaded()
    {
        var arrived = new List<UIComponent>();
        lock (Waiting)
        {
            foreach (var waiting in Waiting)
            {
                if (waiting.TryGetTarget(out var component))
                {
                    arrived.Add(component);
                }
            }

            Waiting.Clear();
        }

        if (arrived.Count > 0)
        {
            LoopSignal.Post(() =>
            {
                foreach (var component in arrived)
                {
                    component.OnFontsArrived();
                }
            });
        }
    }
}
