using System.Collections.Generic;
using System.Collections.Specialized;
using Adamantium.UI.Controls.Base;
using Adamantium.UI.Controls.Primitives;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Media;
using Adamantium.UI.Core.RoutedEvents;

namespace Adamantium.UI.Controls;

/// <summary>A group of properties under one folding header - one ECS component, one category of a class.
/// <para>It IS an <see cref="Expander"/>, not something that looks like one: a section wants a real header with its own
/// chrome, state and context menu, and a header ROW inside a flat list would only be impersonating that.</para></summary>
public class PropertySection : Expander
{
    public static readonly AdamantiumProperty CategoryProperty = AdamantiumProperty.Register(nameof(Category),
        typeof(String), typeof(PropertySection), new PropertyMetadata(null));

    public static readonly AdamantiumProperty TargetProperty = AdamantiumProperty.Register(nameof(Target),
        typeof(object), typeof(PropertySection), new PropertyMetadata(null));

    public static readonly AdamantiumProperty TargetsProperty = AdamantiumProperty.Register(nameof(Targets),
        typeof(IReadOnlyList<object>), typeof(PropertySection), new PropertyMetadata(null));

    /// <summary>A color the header is marked with along its leading edge - what kind of thing the section is, or that
    /// something is wrong with it. None by default.</summary>
    public static readonly AdamantiumProperty AccentProperty = AdamantiumProperty.Register(nameof(Accent),
        typeof(Brush), typeof(PropertySection), new PropertyMetadata(null));

    public static readonly AdamantiumProperty HasMenuProperty = AdamantiumProperty.RegisterReadOnly(nameof(HasMenu),
        typeof(bool), typeof(PropertySection), new PropertyMetadata(false));

    private ButtonBase _menuButton;

    public PropertySection()
    {
        // The properties JOIN the section's logical tree - that is what gives a definition a DataContext, and with it
        // {Binding}, {Ancestor} and every resource lookup on its own properties. Without this step a definition is an
        // object floating beside the tree, and everything written on it in markup would resolve against nothing.
        Properties.CollectionChanged += OnPropertiesChanged;
    }

    /// <summary>The properties in this section, in display order. [Content] - and it deliberately shadows the
    /// <see cref="Expander"/>'s own content property: what one writes inside a section is its properties, while the
    /// content an expander shows is the rows the grid builds from them.</summary>
    [Content]
    public PropertyDefinitions Properties { get; } = new();

    /// <summary>The object these properties read and write. Set by the grid from its own
    /// <see cref="PropertyGrid.SelectedObject"/> unless the section names its own - a section CAN inspect something
    /// else (a component of the selection). It says when it changes, so the section can bind to its object - its
    /// folding, say: <c>IsExpanded="{Self Target.IsExpanded, Mode=TwoWay}"</c>.</summary>
    public object Target
    {
        get => GetValue(TargetProperty);
        set => SetValue(TargetProperty, value);
    }

    /// <summary>Every object of the source this section is written for, when the grid points it at the items of
    /// <see cref="DataType"/>: all of them, edited together - the components of several selected entities - with
    /// <see cref="Target"/> the first.</summary>
    public IReadOnlyList<object> Targets
    {
        get => GetValue<IReadOnlyList<object>>(TargetsProperty);
        internal set => SetValue(TargetsProperty, value);
    }

    /// <summary>The type of object this section is written for. With <see cref="PropertyGrid.SectionsSource"/> set, the
    /// grid points the section at the items of that type, or leaves it out when there is none - so the sections
    /// keep the order they are written in, whatever order the items come in, and the same section, folded or not,
    /// serves every object of its type.</summary>
    public Type DataType { get; set; }

    /// <summary>What the section is called in the model - the component's type name, the [Category] attribute. Free
    /// text; the grid does not interpret it.</summary>
    public String Category
    {
        get => GetValue<String>(CategoryProperty);
        set => SetValue(CategoryProperty, value);
    }

    public Brush Accent
    {
        get => GetValue<Brush>(AccentProperty);
        set => SetValue(AccentProperty, value);
    }

    /// <summary>Whether the section has a <see cref="Base.InputUIComponent.ContextMenu"/>: its header then offers it on
    /// a "⋯" button as well as on a right click, as an editor's component headers do.</summary>
    public bool HasMenu
    {
        get => GetValue<bool>(HasMenuProperty);
        private set => SetValue(HasMenuProperty, value);
    }

    /// <summary>Opens the section's menu under its "⋯" button - under the header when the template has none - or shuts
    /// it when it is open. False when the section has no menu.</summary>
    public bool OpenMenu()
    {
        if (ContextMenu is not { } menu)
        {
            return false;
        }

        if (menu.IsOpen)
        {
            menu.IsOpen = false;
            return true;
        }

        menu.Placement = PlacementMode.Bottom;
        menu.HorizontalOffset = 0;
        menu.VerticalOffset = 0;
        menu.Open(_menuButton ?? (UIComponent)this);
        return true;
    }

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        if (_menuButton != null)
        {
            _menuButton.Click -= OnMenuButtonClick;
        }

        _menuButton = GetTemplateChild("PART_Menu") as ButtonBase;
        if (_menuButton != null)
        {
            _menuButton.Click += OnMenuButtonClick;
        }
    }

    public override void OnRemoveTemplate()
    {
        base.OnRemoveTemplate();
        if (_menuButton != null)
        {
            _menuButton.Click -= OnMenuButtonClick;
        }

        _menuButton = null;
    }

    protected override void OnPropertyChanged(AdamantiumPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.Property != ContextMenuProperty)
        {
            return;
        }

        if (e.OldValue is ContextMenu old)
        {
            old.Opening -= OnMenuOpening;
        }

        if (e.NewValue is ContextMenu menu)
        {
            menu.Opening += OnMenuOpening;
        }

        HasMenu = ContextMenu != null;
    }

    // A menu a style shares outlives the section: left hooked, it would keep every section that ever had it.
    protected override void OnDiscarded()
    {
        if (ContextMenu is { } menu)
        {
            menu.Opening -= OnMenuOpening;
        }

        base.OnDiscarded();
    }

    // Opened from the button, a second press on it shuts the menu; opened by a right click on the section, a press
    // anywhere on the section must put it away.
    private void OnMenuOpening(object sender, ContextMenuOpeningEventArgs e)
    {
        ((ContextMenu)sender).IgnoreTargetPress = _menuButton != null && ReferenceEquals(e.Target, _menuButton);
    }

    private void OnMenuButtonClick(object sender, RoutedEventArgs e)
    {
        if (OpenMenu())
        {
            e.Handled = true;
        }
    }

    private void OnPropertiesChanged(object sender, NotifyCollectionChangedEventArgs e)
    {
        foreach (var gone in e.OldItems?.OfType<PropertyDefinition>() ?? Enumerable.Empty<PropertyDefinition>())
            RemoveLogicalChild(gone);

        foreach (var added in e.NewItems?.OfType<PropertyDefinition>() ?? Enumerable.Empty<PropertyDefinition>())
            AddLogicalChild(added);
    }
}
