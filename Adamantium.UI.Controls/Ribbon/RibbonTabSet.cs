using System.Collections.Generic;
using Adamantium.UI.Controls.Base;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Media;

namespace Adamantium.UI.Controls;

/// <summary>
/// What a module of a document brings to a <see cref="Ribbon"/>: its tabs, under a ledge of its own. A piece of the
/// module's view, built from <see cref="Ribbon.TabSetTemplate"/> for each item of <see cref="Ribbon.TabSetsSource"/>, so
/// the set and its tabs bind against that item - the module's own view model. Never drawn itself: the ribbon puts its
/// tabs in the strip.
/// </summary>
public class RibbonTabSet : UIComponent, IContainer
{
    private readonly List<RibbonTab> _tabs = [];

    public static readonly AdamantiumProperty HeaderProperty = AdamantiumProperty.Register(nameof(Header),
        typeof(object), typeof(RibbonTabSet), new PropertyMetadata(null));

    public static readonly AdamantiumProperty AccentProperty = AdamantiumProperty.Register(nameof(Accent),
        typeof(Brush), typeof(RibbonTabSet), new PropertyMetadata(default(Brush)));

    public static readonly AdamantiumProperty IsActiveProperty = AdamantiumProperty.Register(nameof(IsActive),
        typeof(bool), typeof(RibbonTabSet), new PropertyMetadata(true));

    /// <summary>The title of the ledge over the tabs.</summary>
    public object Header
    {
        get => GetValue(HeaderProperty);
        set => SetValue(HeaderProperty, value);
    }

    /// <summary>The color of the ledge and of the tabs under it.</summary>
    public Brush Accent
    {
        get => GetValue<Brush>(AccentProperty);
        set => SetValue(AccentProperty, value);
    }

    /// <summary>Whether the tabs are in the strip. Off hides them - the module stays in the document.</summary>
    public bool IsActive
    {
        get => GetValue<bool>(IsActiveProperty);
        set => SetValue(IsActiveProperty, value);
    }

    /// <summary>The tabs the module brings, in their order.</summary>
    public IReadOnlyList<RibbonTab> Tabs => _tabs;

    public void AddOrSetChildComponent(object component)
    {
        if (component is RibbonTab tab)
        {
            _tabs.Add(tab);
        }
    }

    public void RemoveAllChildComponents() => _tabs.Clear();

    public IReadOnlyList<object> GetChildComponents() => _tabs;

    public void InsertChildComponent(int index, object component)
    {
        if (component is RibbonTab tab)
        {
            _tabs.Insert(index, tab);
        }
    }

    public void RemoveChildComponentAt(int index) => _tabs.RemoveAt(index);
}
