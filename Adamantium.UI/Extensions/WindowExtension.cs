using Adamantium.Core;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Resources;

namespace Adamantium.UI.Extensions;

public static class WindowExtension
{
    /// <summary>One layout pass of the window: its content, then its popups against where their targets now stand - on
    /// the loop thread, like everything that measures. The popup stage only draws what this laid out.</summary>
    public static void Update(this IWindow window, IThemeManager themeManager, AppTime appTime)
    {
        LayoutManager.GetOrCreate(window).ExecuteLayoutPass();
        window.LayoutPopups();
    }

    /// <summary>Drives one layout pass over a subtree root (no IWindow/theme required). Used by layout tests that drive
    /// two frames to reproduce a dynamic-relayout bug. Resolves the same persistent manager invalidations enqueue into,
    /// so dirtying between passes is picked up.</summary>
    public static void UpdateTree(IUIComponent root)
    {
        LayoutManager.GetOrCreate(root).ExecuteLayoutPass();
    }
}
