using System;
using System.Collections;
using Adamantium.Mathematics;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Text;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Behaviors;
using Adamantium.UI.Core.Data;
using Adamantium.UI.Core.Extensions;
using Adamantium.UI.Core.Input;

namespace Adamantium.UI.Tabs;

/// <summary>
/// Gives a tab dragged off its strip a window of its own, and lets it be carried back. The tab's item leaves the
/// control's <c>ItemsSource</c> - a list it can change - and becomes the window's content and data context, at the size
/// the tab's content had; dragged over the strip of any tab control of the application whose items can take it, the
/// window goes clear and the tab stands there, and let go there it stays and the window closes. Attach:
/// <code>&lt;TabControl.Behaviors&gt;&lt;TabTearOffBehavior WindowTitle="{Binding Header}"/&gt;&lt;/TabControl.Behaviors&gt;</code>
/// The window is a <see cref="Window"/> wearing <see cref="WindowClasses"/>, for a style to find it by; <see
/// cref="WindowCreated"/> is for what markup cannot say. Its caption opens under the pointer and rides on with the button
/// still held; a gesture made inside the application - automation - leaves it where the gesture ended.
/// </summary>
public class TabTearOffBehavior : Behavior<TabControl>
{
    public static readonly AdamantiumProperty WindowClassesProperty = AdamantiumProperty.Register(nameof(WindowClasses),
        typeof(string), typeof(TabTearOffBehavior), new PropertyMetadata(null));

    public static readonly AdamantiumProperty WindowTitleProperty = AdamantiumProperty.Register(nameof(WindowTitle),
        typeof(BindingBase), typeof(TabTearOffBehavior), new PropertyMetadata(null));

    public static readonly AdamantiumProperty CarriesBackProperty = AdamantiumProperty.Register(nameof(CarriesBack),
        typeof(bool), typeof(TabTearOffBehavior), new PropertyMetadata(true));

    private static readonly Size<double> DefaultContentSize = new(640, 480);

    private TabControl _tabs;

    /// <summary>The classes the torn-off window wears, for a style to find it by.</summary>
    public string WindowClasses
    {
        get => GetValue<string>(WindowClassesProperty);
        set => SetValue(WindowClassesProperty, value);
    }

    /// <summary>The window's title, bound against the tab's item - the window's data context: a path of it, or a
    /// localized text it names. Null takes the header of a tab that is its own item.</summary>
    public BindingBase WindowTitle
    {
        get => GetValue<BindingBase>(WindowTitleProperty);
        set => SetValue(WindowTitleProperty, value);
    }

    /// <summary>Whether a torn-off window can be carried back into a tab strip. True by default.</summary>
    public bool CarriesBack
    {
        get => GetValue<bool>(CarriesBackProperty);
        set => SetValue(CarriesBackProperty, value);
    }

    /// <summary>Raised with each new window before it is shown.</summary>
    public event EventHandler<TabTearOffWindowEventArgs> WindowCreated;

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
        if (_tabs?.ItemsSource is not IList { IsReadOnly: false, IsFixedSize: false } items || !items.Contains(e.Item))
        {
            return;
        }

        items.Remove(e.Item);

        var content = e.ContentSize.Width > 0 && e.ContentSize.Height > 0 ? e.ContentSize : DefaultContentSize;
        var window = new Window
        {
            RemembersPlacement = false,
            ClientWidth = content.Width,
            DataContext = e.Item,
            Position = e.ScreenPosition
        };

        if (WindowClasses is { Length: > 0 } classes)
        {
            window.Classes = Core.Resources.Classes.Parse(classes);
        }

        if (WindowTitle is { } title)
        {
            window.SetBinding(WindowBase.TitleProperty, (BindingBase)title.Clone());
        }
        else
        {
            window.Title = HeaderText(e.Container);
        }

        WindowCreated?.Invoke(this, new TabTearOffWindowEventArgs(window, e.Item));

        if (CarriesBack)
        {
            _ = new TornTab(window, e.Item);
        }

        var gesture = new TornGesture(Mouse.ScreenCoordinates, Mouse.Platform?.Window != null);
        var tabs = _tabs;
        UIAppContext.Current.Dispatcher.InvokeAsync(() => Show(window, tabs, e.Item, content, gesture));

        e.TornWindow = window;
        e.Handled = true;
    }

    private static void Show(Window window, TabControl tabs, object item, Size<double> content, TornGesture gesture)
    {
        window.ContentTemplate = tabs.ContentTemplate;
        window.ContentTemplateSelector = tabs.ContentTemplateSelector;
        window.Content = item;
        window.Show();
        window.ClientHeight = content.Height + window.TitleBarHeight;

        var grab = PixelPoint.FromLogical(new Vector2<double>(window.ClientWidth / 2, window.TitleBarHeight / 2),
            window.DpiScale);
        window.Position = (gesture.IsMade ? gesture.Pointer : Mouse.ScreenCoordinates) - grab;
        if (!gesture.IsMade)
        {
            window.DragMove();
        }
    }

    private static string HeaderText(TabItem tab)
    {
        if (tab?.Header is string header)
        {
            return header;
        }

        string text = null;
        tab?.TraverseVisualTree(component =>
        {
            if (text == null && component is TextBlock { Text: { Length: > 0 } shown })
            {
                text = shown;
            }
        });
        return text;
    }
}
