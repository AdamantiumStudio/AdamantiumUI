using System.Collections;
using System.Collections.Specialized;
using System.Runtime.CompilerServices;
using Adamantium.UI.Controls.Base;
using Adamantium.UI.Controls.Generators;
using Adamantium.UI.Controls.Panels;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Data;
using Adamantium.UI.Core.Resources;
using Adamantium.UI.Core.RoutedEvents;
using Adamantium.UI.Core.Templates;

namespace Adamantium.UI.Controls;

/// <summary>Presents <see cref="Items"/> or <see cref="ItemsSource"/> as templated containers laid out by
/// <see cref="ItemsPanel"/>. The template must contain an <see cref="ItemsPresenter"/> named <c>PART_ItemsPresenter</c>.</summary>
public class ItemsControl : Control, IContainer
{
    private static readonly ConditionalWeakTable<IUIComponent, ItemsControl> AuthoredIn = new();
    private static readonly ConditionalWeakTable<IUIComponent, object> Refused = new();

    private readonly ConditionalWeakTable<IUIComponent, object> _taken = new();
    private ItemsPresenter _presenter;

    public static readonly AdamantiumProperty ItemsSourceProperty = AdamantiumProperty.Register(nameof(ItemsSource),
        typeof(IEnumerable), typeof(ItemsControl), new PropertyMetadata(null, OnItemsSourceChanged));

    public static readonly AdamantiumProperty ItemTemplateProperty = AdamantiumProperty.Register(nameof(ItemTemplate),
        typeof(DataTemplate), typeof(ItemsControl), new PropertyMetadata(null, OnItemTemplateChanged));

    public static readonly AdamantiumProperty ItemTemplateSelectorProperty = AdamantiumProperty.Register(nameof(ItemTemplateSelector),
        typeof(DataTemplateSelector), typeof(ItemsControl), new PropertyMetadata(null, OnRegenerateContainers));

    /// <summary>Visual shown in a virtualized slot whose real item hasn't been (re)bound yet this frame (a fast fling or a
    /// big fill exceeds the per-frame bind budget). Null = the panel's built-in themed skeleton (a muted rounded tile).</summary>
    public static readonly AdamantiumProperty ItemSkeletonTemplateProperty = AdamantiumProperty.Register(nameof(ItemSkeletonTemplate),
        typeof(DataTemplate), typeof(ItemsControl), new PropertyMetadata(null));

    /// <summary>True while the panel shows skeleton placeholders for binds deferred past the frame budget; a theme keys
    /// one shared skeleton shimmer per list off it.</summary>
    public static readonly AdamantiumProperty IsLoadingItemsProperty = AdamantiumProperty.RegisterReadOnly(nameof(IsLoadingItems),
        typeof(bool), typeof(ItemsControl), new PropertyMetadata(false));

    public static readonly AdamantiumProperty ItemContainerStyleProperty = AdamantiumProperty.Register(nameof(ItemContainerStyle),
        typeof(Style), typeof(ItemsControl), new PropertyMetadata(null, OnRegenerateContainers));

    public static readonly AdamantiumProperty ItemsPanelProperty = AdamantiumProperty.Register(nameof(ItemsPanel),
        typeof(ItemsPanelTemplate), typeof(ItemsControl), new PropertyMetadata(null, OnItemsPanelChanged));

    public ItemsControl()
    {
        Items = new ItemCollection();
        Items.CollectionChanged += OnItemsCollectionChanged;
        ItemContainerGenerator = new ItemContainerGenerator(this);
    }

    /// <summary>The effective item list — markup-authored items, or a view over <see cref="ItemsSource"/>.</summary>
    [Content]
    public ItemCollection Items { get; }

    public ItemContainerGenerator ItemContainerGenerator { get; }

    /// <summary>The panel laying out the item containers, or null before the template is applied. Virtual for controls
    /// that split items across several panels.</summary>
    public virtual Panel ItemsHostPanel => _presenter?.Panel;

    public IEnumerable ItemsSource
    {
        get => GetValue<IEnumerable>(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    public DataTemplate ItemTemplate
    {
        get => GetValue<DataTemplate>(ItemTemplateProperty);
        set => SetValue(ItemTemplateProperty, value);
    }

    /// <summary>The card shown in the hole a drag opens at its insertion point - "what you are holding lands here".
    /// A DIFFERENT template from <see cref="ItemSkeletonTemplateProperty"/> on purpose: a loading skeleton says content
    /// is on its way, and dressing the two the same would merge two different messages. It also stays out of
    /// <see cref="IsLoadingItemsProperty"/>, so a drag never pulses the list as if it were fetching something.</summary>
    public static readonly AdamantiumProperty DropPlaceholderTemplateProperty = AdamantiumProperty.Register(
        nameof(DropPlaceholderTemplate), typeof(DataTemplate), typeof(ItemsControl), new PropertyMetadata(null));

    public DataTemplate DropPlaceholderTemplate
    {
        get => GetValue<DataTemplate>(DropPlaceholderTemplateProperty);
        set => SetValue(DropPlaceholderTemplateProperty, value);
    }

    /// <summary>Placeholder template for a not-yet-bound virtualized slot (see <see cref="ItemSkeletonTemplateProperty"/>).</summary>
    public DataTemplate ItemSkeletonTemplate
    {
        get => GetValue<DataTemplate>(ItemSkeletonTemplateProperty);
        set => SetValue(ItemSkeletonTemplateProperty, value);
    }

    /// <summary>Whether loading skeleton cards are on screen right now (see <see cref="IsLoadingItemsProperty"/>).</summary>
    public bool IsLoadingItems
    {
        get => GetValue<bool>(IsLoadingItemsProperty);
        internal set => SetValue(IsLoadingItemsProperty, value);
    }

    /// <summary>Picks the <see cref="DataTemplate"/> per item (when no <see cref="ItemTemplate"/> is set).</summary>
    public DataTemplateSelector ItemTemplateSelector
    {
        get => GetValue<DataTemplateSelector>(ItemTemplateSelectorProperty);
        set => SetValue(ItemTemplateSelectorProperty, value);
    }

    /// <summary>Style applied to every generated container (its real value is selection/state styling on the
    /// dedicated container types - ListBoxItem etc.; on the base it styles the ContentPresenter).</summary>
    public Style ItemContainerStyle
    {
        get => GetValue<Style>(ItemContainerStyleProperty);
        set => SetValue(ItemContainerStyleProperty, value);
    }

    public ItemsPanelTemplate ItemsPanel
    {
        get => GetValue<ItemsPanelTemplate>(ItemsPanelProperty);
        set => SetValue(ItemsPanelProperty, value);
    }

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        _presenter = GetTemplateChild("PART_ItemsPresenter") as ItemsPresenter;
        _presenter?.Connect(this);
    }

    /// <summary>Releases the items source, whose collection may outlive this control and would otherwise keep it
    /// alive.</summary>
    protected override void OnDiscarded()
    {
        base.OnDiscarded();
        Items?.SetSource(null);
    }

    /// <summary>Drops the presenter with its template; it holds the panel, every container and the recycle pool, which a
    /// theme swap would otherwise leak.</summary>
    public override void OnRemoveTemplate()
    {
        base.OnRemoveTemplate();
        _presenter = null;
    }

    /// <summary>Connect an items host that arrives AFTER OnApplyTemplate - e.g. a MenuItem whose PART_ItemsPresenter lives in
    /// its submenu Popup's lazily-built <see cref="Popup.ChildTemplate"/>, so GetTemplateChild couldn't find it up front.</summary>
    protected void ConnectPresenter(ItemsPresenter presenter)
    {
        _presenter = presenter;
        _presenter?.Connect(this);
    }

    private static void OnItemsSourceChanged(AdamantiumComponent a, AdamantiumPropertyChangedEventArgs e)
    {
        ((ItemsControl)a).ApplyItemsSource(e.NewValue as IEnumerable);
    }

    /// <summary>Applies a new ItemsSource to the effective item list. Base points <see cref="Items"/> straight at the
    /// source; overridden where the source is projected into a different backing list (TreeView flattens a tree into its
    /// rows and points Items at THOSE).</summary>
    protected virtual void ApplyItemsSource(IEnumerable newValue) => Items.SetSource(newValue);

    private static void OnItemTemplateChanged(AdamantiumComponent a, AdamantiumPropertyChangedEventArgs e)
    {
        ((ItemsControl)a).OnItemTemplateChangedCore();
        OnRegenerateContainers(a, e);
    }

    /// <summary>Called when ItemTemplate changes, before containers regenerate. Overridden where the template drives the
    /// backing list (TreeView resolves each node's children from the HierarchicalDataTemplate's ItemsSource path).</summary>
    protected virtual void OnItemTemplateChangedCore() { }

    // ItemTemplate / ItemTemplateSelector / ItemContainerStyle changed: existing containers were projected through the
    // old settings, so drop them and re-generate.
    private static void OnRegenerateContainers(AdamantiumComponent a, AdamantiumPropertyChangedEventArgs e)
    {
        ((ItemsControl)a)._presenter?.RegenerateContainers();
    }

    private static void OnItemsPanelChanged(AdamantiumComponent a, AdamantiumPropertyChangedEventArgs e)
    {
        // Rebuilding throws the live items panel away and builds an identical replacement - the containers follow the
        // new one while the visual tree carries on arranging the old. So only do it when the template ACTUALLY changed:
        // re-applying a style re-assigns this property, and the same template arriving twice must not cost a panel.
        if (ReferenceEquals(e.OldValue, e.NewValue)) return;

        ((ItemsControl)a)._presenter?.Rebuild();
    }

    private void OnItemsCollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
    {
        if (ItemsSource == null)
        {
            NoteAuthored(e);
        }

        _presenter?.OnItemsChanged(e);
        OnItemsChanged(e);
    }

    // An element written into Items belongs to this control, shown or not yet - a tab never opened still owns its buttons.
    private void NoteAuthored(NotifyCollectionChangedEventArgs e)
    {
        foreach (var old in e.OldItems?.OfType<IUIComponent>() ?? [])
        {
            if (AuthoredIn.TryGetValue(old, out var owner) && owner == this)
            {
                AuthoredIn.Remove(old);
            }
        }

        foreach (var item in e.NewItems?.OfType<IUIComponent>() ?? [])
        {
            AuthoredIn.AddOrUpdate(item, this);
        }
    }

    /// <summary>Whether <paramref name="item"/> may be shown here, remembering an element taken from the items source.
    /// An element that stands somewhere else - written into another items control, or already in the tree - is not:
    /// showing it would pull it out of where it stands. Its slot stays empty and the mistake is logged once.</summary>
    internal bool TryTake(object item)
    {
        if (item is not IUIComponent element)
        {
            return true;
        }

        var elsewhere = AuthoredIn.TryGetValue(element, out var owner) && (ShowsFor(owner) || owner.Items.Contains(element))
            ? !ShowsFor(owner)
            : !_taken.TryGetValue(element, out _) && (element.VisualParent != null || element.LogicalParent != null);
        if (!elsewhere)
        {
            _taken.AddOrUpdate(element, null);
            return true;
        }

        if (!Refused.TryGetValue(element, out _))
        {
            Refused.AddOrUpdate(element, null);
            Serilog.Log.Logger.Error(
                "{Control} was given {Element} to show, which already stands elsewhere in the tree; it is left where it is. An element has one place: give the list data and draw it with a template",
                GetType().Name, element.GetType().Name);
        }

        return false;
    }

    internal static ItemsControl AuthoredOwner(IUIComponent element)
    {
        return element != null && AuthoredIn.TryGetValue(element, out var owner) ? owner : null;
    }

    // The control an element was written into shows it - itself, or a list of its own template it hands its items to.
    private bool ShowsFor(ItemsControl owner)
    {
        for (IAdamantiumComponent node = this; node != null; node = (node as IFundamentalUIComponent)?.TemplatedParent)
        {
            if (node == owner)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The items collection changed. For a subclass that keeps something derived from it - a selection named
    /// before the items existed, say.</summary>
    protected virtual void OnItemsChanged(NotifyCollectionChangedEventArgs e) { }

    // --- Container seam (override in selectable controls, e.g. ListBox -> ListBoxItem) -----------------------------

    /// <summary>True if the item is already a ready-to-host UI element (used as its own container, no wrapping).</summary>
    protected internal virtual bool IsItemItsOwnContainer(object item) => item is IUIComponent;

    /// <summary>Creates the container for <paramref name="item"/>. Base = a <see cref="ContentPresenter"/> carrying the
    /// ItemContainerStyle (the item lets a subclass pick a different container - e.g. a Separator for an ISeparatorItem).</summary>
    protected internal virtual IUIComponent GetContainerForItem(object item)
    {
        var presenter = new ContentPresenter();
        // Into the Styles collection (a user style applied AFTER the theme), not AttachStyles which runs before the
        // container is themed and would be overwritten by it - see ListBox.GetContainerForItem.
        if (ItemContainerStyle != null) presenter.Styles.Add(ItemContainerStyle);
        return presenter;
    }

    /// <summary>Binds a (new or recycled) container to <paramref name="item"/>: DataContext + content + item template.
    /// <para>A container that IS its own item has nothing to bind - it carries what its author wrote - and binding it
    /// would make it its own content. Subclasses hooking their containers still get the call, and should guard only the
    /// binding half the same way.</para></summary>
    protected internal virtual void PrepareContainer(IUIComponent container, object item)
    {
        if (ReferenceEquals(container, item)) return;

        // A HierarchicalDataTemplate + a headered container (MenuItem / TreeViewItem) = one tree level: draw the item as
        // the header via the HDT, bind this container's OWN items to the node's children, and re-apply the SAME template to
        // them so the tree unrolls recursively. Set the child template + style BEFORE ItemsSource (setting the source is
        // what triggers child-container generation). See HierarchicalDataTemplate.
        if (ItemTemplate is HierarchicalDataTemplate hdt && container is IHeaderedItemsControl headered && container is ItemsControl childItems)
        {
            childItems.DataContext = item;
            headered.Header = item;
            headered.HeaderTemplate = hdt;
            childItems.ItemContainerStyle = hdt.ItemContainerStyle ?? ItemContainerStyle;
            childItems.ItemTemplate = hdt.ItemTemplate ?? hdt;
            if (hdt.ItemsSource != null) childItems.SetBinding(ItemsSourceProperty, (BindingBase)hdt.ItemsSource.Clone());
            return;
        }

        if (container is IHeaderedItemsControl row && container is ItemsControl rowItems)
        {
            rowItems.DataContext = item;
            row.Header = item;
            row.HeaderTemplate = ItemTemplate ?? ItemTemplateSelector?.SelectTemplate(item, (AdamantiumComponent)container);
            return;
        }

        if (container is ContentPresenter presenter)
        {
            presenter.DataContext = item;
            presenter.ContentTemplate = ItemTemplate;
            presenter.ContentTemplateSelector = ItemTemplateSelector;
            presenter.Content = item;
        }
    }

    /// <summary>Releases a container's item so it can be recycled. Crucially does NOT clear Content - that would tear
    /// down the item template's visual (and its GPU buffers); the container keeps its visual and is rebound to a new
    /// item (its DataContext) on reuse, so the buffers are reused, not recreated every scroll frame.</summary>
    protected internal virtual void ClearContainer(IUIComponent container)
    {
        if (container is ContentPresenter presenter)
            presenter.DataContext = null;
        else if (container is IHeaderedItemsControl and ItemsControl headered)
            headered.DataContext = null;
    }

    // IContainer: markup children flow into Items.
    void IContainer.AddOrSetChildComponent(object component) => Items.Add(component);

    void IContainer.RemoveAllChildComponents() => Items.Clear();

    IReadOnlyList<object> IContainer.GetChildComponents() => Items.ToList();

    void IContainer.InsertChildComponent(int index, object component) => Items.Insert(index, component);

    void IContainer.RemoveChildComponentAt(int index) => Items.RemoveAt(index);
}
