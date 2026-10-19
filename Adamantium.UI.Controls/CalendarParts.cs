using System;
using System.Collections.Generic;
using Adamantium.UI.Controls.Base;
using Adamantium.UI.Controls.Panels;

namespace Adamantium.UI.Controls;

internal static class CalendarParts
{
    public static void Fill<T>(Panel panel, List<T> made, int count, Func<int, T> make) where T : MeasurableUIComponent
    {
        made.Clear();
        if (panel == null)
        {
            return;
        }

        panel.Children.Clear();
        for (var i = 0; i < count; i++)
        {
            var element = make(i);
            made.Add(element);
            panel.Children.Add(element);
        }
    }
}
