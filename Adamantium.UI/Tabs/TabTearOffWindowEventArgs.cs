using System;
using Adamantium.UI.Controls;

namespace Adamantium.UI.Tabs;

/// <summary>The window a torn-off tab was given (<see cref="TabTearOffBehavior.WindowCreated"/>), before it is shown:
/// what an application would set on it that markup cannot say.</summary>
public class TabTearOffWindowEventArgs : EventArgs
{
    public TabTearOffWindowEventArgs(Window window, object item)
    {
        Window = window;
        Item = item;
    }

    /// <summary>The new window; its content and data context are <see cref="Item"/>.</summary>
    public Window Window { get; }

    /// <summary>The item the tab showed.</summary>
    public object Item { get; }
}
