using Adamantium.UI;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Base;
using Adamantium.UI.Controls.Panels;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Behaviors;

namespace Adamantium.UI.Sandbox.Behaviors;

/// <summary>
/// Takes a tab dragged off its strip and gives it a window of its own. This lives in the APPLICATION, not in
/// TabControl, because what a torn-off tab deserves - chrome, size, whether it keeps a tab strip - is the
/// application's answer, and a docking host will answer it differently. The control only reports the gesture.
/// </summary>
public class TabTearOffBehavior : Behavior<TabControl>
{
    private TabControl _tabs;

    protected override void OnAttached(TabControl tabs)
    {
        _tabs = tabs;
        tabs.TabTornOff += OnTornOff;
    }

    protected override void OnDetached(TabControl tabs)
    {
        tabs.TabTornOff -= OnTornOff;
        _tabs = null;
    }
    private void OnTornOff(object sender, TabTearOffEventArgs e)
    {
        var items = _tabs?.ItemsSource as System.Collections.IList;
        if (items == null || !items.Contains(e.Item)) return;

        items.Remove(e.Item);

        var window = new Window
        {
            Title = (e.Item as ViewModels.TabPageViewModel)?.Header ?? "Tab",
            ClientWidth = 640,
            ClientHeight = 480,
            // A desktop point offset by a desktop distance - both physical, and the types say so. See PixelPoint.
            Position = e.ScreenPosition - new PixelPoint(60, 20)
        };

        // Windows are shown on the UI thread, so hop there without blocking the frame. Show before setting content (the
        // item plus the strip's selector) so it joins a live, themed window.
        UIAppContext.Current.Dispatcher.InvokeAsync(() =>
        {
            window.ContentTemplate = _tabs.ContentTemplate;
            window.ContentTemplateSelector = _tabs.ContentTemplateSelector;
            window.Content = e.Item;
            window.Show();

            // Put the caption under the live cursor position (the gesture's has moved on by now), converting the grab
            // offset with the new window's own scale, which may be another monitor's.
            var grab = PixelPoint.FromLogical(
                new Mathematics.Vector2((float)(window.ClientWidth / 2), (float)(window.TitleBarHeight / 2)),
                window.DpiScale);
            window.Position = Adamantium.UI.Core.Input.Mouse.ScreenCoordinates - grab;

            // Hand the still-held mouse button to the OS window-move loop: the window rides under the cursor until the
            // button comes up, WITH Aero Snap and edge snapping, because it is the platform's own move - not a position
            // we recompute per mouse event. This is what makes the tear-off one continuous gesture.
            window.DragMove();
        });

        // The strip keeps following the pointer for it: the window rides under the cursor until the button comes up.
        e.TornWindow = window;

        e.Handled = true;   // taken - the strip must not put it back
    }
}
