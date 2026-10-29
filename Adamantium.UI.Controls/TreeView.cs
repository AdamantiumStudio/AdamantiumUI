using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using Adamantium.Mathematics;
using Adamantium.UI.Controls.Automation;
using Adamantium.UI.Controls.Base;
using Adamantium.UI.Controls.Panels;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Automation;
using Adamantium.UI.Core.Data;
using Adamantium.UI.Core.Input;
using Adamantium.UI.Core.Resources;
using Adamantium.UI.Core.RoutedEvents;
using Adamantium.UI.Core.Templates;

namespace Adamantium.UI.Controls;

/// <summary>A hierarchical list flattened into one virtualized list of rows, one per visible node, so only on-screen rows
/// become <see cref="TreeViewItem"/>s. <see cref="SelectionMode"/> sets single or multiple selection.</summary>
public class TreeView : ItemsControl
{
    static TreeView()
    {
        // The same standing a list box has, and for the same reasons: the tree takes the keyboard (its arrows walk the
        // rows), but for Tab it is a DOORWAY, not a stop - entered once, onto a row, and the next Tab leaves the whole
        // tree rather than walking every node in it.
        FocusableProperty.OverrideMetadata(typeof(TreeView), new PropertyMetadata(true));
        KeyboardNavigation.IsTabStopProperty.OverrideMetadata(typeof(TreeView), new PropertyMetadata(false));
        KeyboardNavigation.TabNavigationProperty.OverrideMetadata(typeof(TreeView),
            new PropertyMetadata(KeyboardNavigationMode.Once));
    }

    // The DATA item behind the most-recently selected node (null = nothing selected), and it goes BOTH ways: clicking a
    // node writes it, and writing it selects that node - which is how a view-model says what is chosen, and how it
    // takes a choice back. A tree that only ever wrote it kept a selection nobody else could end: picking the same node
    // a second time was not a change, so whoever acted on the change never heard about it.
    public static readonly AdamantiumProperty SelectedItemProperty = AdamantiumProperty.Register(nameof(SelectedItem),
        typeof(object), typeof(TreeView), new PropertyMetadata(null, OnSelectedItemChanged));

    /// <summary>The member a node names its parent by - an entity's <c>Owner</c>. With it, a node written into
    /// <see cref="SelectedItem"/> from outside is revealed: the branches above it are opened if it sits in a closed one,
    /// and it is scrolled to. Without it such a node is selected only when its row is already shown.</summary>
    public static readonly AdamantiumProperty ParentPathProperty = AdamantiumProperty.Register(nameof(ParentPath),
        typeof(string), typeof(TreeView), new PropertyMetadata(null));

    /// <summary>Single (one node - the default), Multiple (each click toggles), or Extended (Ctrl/Shift like a list box).</summary>
    public static readonly AdamantiumProperty SelectionModeProperty = AdamantiumProperty.Register(nameof(SelectionMode),
        typeof(TreeViewSelectionMode), typeof(TreeView), new PropertyMetadata(TreeViewSelectionMode.Single));

    /// <summary>Whether double-clicking a node that has children toggles its expansion (true by default). A leaf does nothing.</summary>
    public static readonly AdamantiumProperty ExpandOnDoubleClickProperty = AdamantiumProperty.Register(nameof(ExpandOnDoubleClick),
        typeof(bool), typeof(TreeView), new PropertyMetadata(true));

    /// <summary>Runs on a double click on a leaf (or on a branch when <see cref="ExpandOnDoubleClick"/> is off), with the
    /// item as the parameter.</summary>
    public static readonly AdamantiumProperty ItemActivatedCommandProperty = AdamantiumProperty.Register(
        nameof(ItemActivatedCommand), typeof(ICommand), typeof(TreeView), new PropertyMetadata(null));

    public ICommand ItemActivatedCommand
    {
        get => GetValue<ICommand>(ItemActivatedCommandProperty);
        set => SetValue(ItemActivatedCommandProperty, value);
    }

    /// <summary>Raised on the same gesture, for whoever would rather have an event than a command.</summary>
    public event EventHandler<ItemActivatedEventArgs> ItemActivated;

    // WHAT WAS DOUBLE CLICKED is what is selected: the first of the two presses selected it, which is the rule a single
    // click follows and the one the list follows.
    internal void Activate()
    {
        if (SelectedItem is not { } item) return;

        ItemActivated?.Invoke(this, new ItemActivatedEventArgs(item));

        if (ItemActivatedCommand is { } command && command.CanExecute(item)) command.Execute(item);
    }

    /// <summary>The vertical scroll offset (px), two-way. Bind it to the view-model to PERSIST the scroll position across a
    /// view rebuild (a tab switch recreates this view) - or to DRIVE the scroll from the VM. Reflects live scrolling; setting
    /// it scrolls the tree (restored once the content's extent is known).</summary>
    public static readonly AdamantiumProperty VerticalOffsetProperty = AdamantiumProperty.Register(nameof(VerticalOffset),
        typeof(double), typeof(TreeView), new PropertyMetadata(0.0, OnVerticalOffsetChanged));

    // The tree projected flat. Rebuilt when the roots (ItemsSource) or the template (child-resolution path) change; the
    // base Items is pointed at its Rows, so the whole ItemsControl virtualization pipeline drives the flat list unchanged.
    private TreeFlattener _flattener;
    private IEnumerable _roots;
    // Selection lives on the ROWS (survives virtualization: a selected row scrolled out + back stays selected); realized
    // containers just mirror their row. The set is the currently-selected rows; the anchor is the Shift-range fixed end.
    private readonly HashSet<TreeRow> _selectedRows = [];
    private TreeRow _anchorRow;
    // Writes a node's selection onto the node itself (from the ItemContainerStyle's IsSelected binding path), so a selection
    // persists across a view rebuild and reaches OFF-SCREEN rows that have no container. No-op if the style doesn't bind it.
    private Action<object, bool> _setNodeSelected = static (_, _) => { };
    private Func<object, bool> _isChosen;

    // Writes a node's OWN IsExpanded (the ItemContainerStyle's IsExpanded binding path), so SyncRowExpansion can trigger a
    // lazy branch's load synchronously BEFORE the flattener reads its children - no reliance on binding-vs-callback order.
    private Action<object, bool> _setNodeExpanded = static (_, _) => { };

    // The containers' style as given and as applied, the member it opens rows by, and the nodes shown now - see
    // ContainerStyle. _writingNode is up while the tree writes that member itself.
    private Style _containerStyleOf;
    private Style _containerStyle;
    private string _expandMember;
    private Func<object, bool> _isNodeExpanded = static _ => false;
    private readonly Dictionary<TreeViewItem, INotifyPropertyChanged> _watched = new();
    private bool _writingNode;

    // Two-way scroll-offset plumbing: _scrollViewer is the template's ScrollViewer; _applyingOffset guards the property
    // <-> scrollbar echo from looping; a set offset that can't land yet (extent not measured) is kept _desired and retried
    // as the metrics settle.
    private ScrollViewer _scrollViewer;
    private bool _applyingOffset;
    private bool _hasDesiredOffset;
    private double _desiredOffset;

    /// <summary>The data item of the most-recently selected node, both ways: clicking a node writes it, and writing it
    /// selects that node - null selects nothing at all.</summary>
    public object SelectedItem { get => GetValue<object>(SelectedItemProperty); set => SetValue(SelectedItemProperty, value); }

    /// <summary>Every node selected, kept in step both ways: the tree writes into the list what is selected in it, and
    /// selects what is written into it - a node in a closed branch comes selected when its row does. A node written into
    /// <see cref="SelectedItem"/> that the list holds becomes the one chosen last without letting the rest go.</summary>
    public static readonly AdamantiumProperty SelectedItemsProperty = AdamantiumProperty.Register(nameof(SelectedItems),
        typeof(IList), typeof(TreeView), new PropertyMetadata(null, OnSelectedItemsChanged));

    public IList SelectedItems { get => GetValue<IList>(SelectedItemsProperty); set => SetValue(SelectedItemsProperty, value); }

    private readonly Dictionary<object, TreeRow> _rowsByNode = new();
    private Action _showSelectedItems;
    private bool _showingItems;
    private bool _showPending;
    private bool _writingItems;

    private static void OnSelectedItemsChanged(AdamantiumComponent component, AdamantiumPropertyChangedEventArgs e)
    {
        if (component is not TreeView tree)
        {
            return;
        }

        if (e.OldValue is INotifyCollectionChanged old)
        {
            old.CollectionChanged -= tree.OnSelectedItemsListChanged;
        }

        if (e.NewValue is INotifyCollectionChanged now)
        {
            now.CollectionChanged += tree.OnSelectedItemsListChanged;
        }

        tree.ShowSelectedItems();
    }

    private void OnSelectedItemsListChanged(object sender, NotifyCollectionChangedEventArgs e)
    {
        if (_writingItems || _showPending)
        {
            return;
        }

        _showPending = true;
        LoopSignal.Post(_showSelectedItems ??= ShowSelectedItems);
    }

    private void ShowSelectedItems()
    {
        _showPending = false;
        if (_flattener == null || SelectedItems is not { } list)
        {
            return;
        }

        var rows = RowsByNode();
        _showingItems = true;
        try
        {
            ClearSelection();
            foreach (var node in list)
            {
                if (node != null && rows.TryGetValue(node, out var row))
                {
                    Select(row);
                }
            }

            if (SelectedItem != null && rows.TryGetValue(SelectedItem, out var chosen))
            {
                _anchorRow = chosen;
            }

            SyncSelectionToContainers();
        }
        finally
        {
            _showingItems = false;
        }
    }

    private Dictionary<object, TreeRow> RowsByNode()
    {
        _rowsByNode.Clear();
        foreach (var row in _flattener.Rows)
        {
            _rowsByNode.TryAdd(row.Node, row);
        }

        return _rowsByNode;
    }

    private void WriteSelectedItems(bool replaced)
    {
        if (_showingItems || SelectedItems is not { IsReadOnly: false } list)
        {
            return;
        }

        var rows = RowsByNode();
        _writingItems = true;
        try
        {
            for (var i = list.Count - 1; i >= 0; i--)
            {
                var shown = list[i] != null && rows.TryGetValue(list[i], out var row) ? row : null;
                if (shown is { IsSelected: false } || (shown == null && replaced))
                {
                    list.RemoveAt(i);
                }
            }

            var kept = new HashSet<object>();
            foreach (var node in list)
            {
                kept.Add(node);
            }

            foreach (var row in _selectedRows)
            {
                if (kept.Add(row.Node))
                {
                    list.Add(row.Node);
                }
            }

            if (SelectedItem is { } chosen && list.Count > 1 && !Equals(list[^1], chosen) && list.Contains(chosen))
            {
                list.Remove(chosen);
                list.Add(chosen);
            }
        }
        finally
        {
            _writingItems = false;
        }
    }

    public string ParentPath { get => GetValue<string>(ParentPathProperty); set => SetValue(ParentPathProperty, value); }

    /// <summary>How many nodes may be selected and how clicks combine (Single, Multiple, Extended).</summary>
    public TreeViewSelectionMode SelectionMode { get => GetValue<TreeViewSelectionMode>(SelectionModeProperty); set => SetValue(SelectionModeProperty, value); }

    /// <summary>Whether a double-click on a branch toggles its expansion (default true).</summary>
    public bool ExpandOnDoubleClick { get => GetValue<bool>(ExpandOnDoubleClickProperty); set => SetValue(ExpandOnDoubleClickProperty, value); }

    /// <summary>The vertical scroll offset (px). Two-way - bind it to the view-model to persist / drive the scroll position.</summary>
    public double VerticalOffset { get => GetValue<double>(VerticalOffsetProperty); set => SetValue(VerticalOffsetProperty, value); }

    // The roots changed: keep them and (re)build the flat projection. Items ends up pointed at the flattener's Rows, NOT
    // the raw roots, so the generator/panel realize flat rows.
    protected override void ApplyItemsSource(IEnumerable newValue)
    {
        _roots = newValue;
        RebuildFlattener();
    }

    // The template carries the child-resolution path (HierarchicalDataTemplate.ItemsSource, e.g. {Binding Children}); a
    // new template can point elsewhere, so re-derive the resolver and re-flatten.
    protected override void OnItemTemplateChangedCore() => RebuildFlattener();

    private void RebuildFlattener()
    {
        Follow(nameof(_flattener), null);
        // Child path from the HierarchicalDataTemplate's ItemsSource binding (e.g. {Binding Children} -> "Children").
        var childPath = (ItemTemplate as HierarchicalDataTemplate)?.ItemsSource is Binding binding ? binding.Path?.Path : null;
        // Expansion + selection paths from the ItemContainerStyle's IsExpanded / IsSelected setter bindings. Letting a
        // rebuild RESTORE the branches/rows the view-model still marks (a tab switch recreates this view, but the VM - and
        // its expanded + selected state - persists). Read from the style's own setters, so there's no second place to declare them.
        var expandPath = MemberPath(nameof(TreeViewItem.IsExpanded));
        var selectPath = MemberPath(nameof(TreeViewItem.IsSelected));
        // Without a member to read it from, a row built again - its branch closed and opened - knows itself by SelectedItem.
        _flattener = new TreeFlattener(TreeChildResolver.ForPath(childPath),
            TreeChildResolver.ForBoolPath(expandPath),
            selectPath != null ? TreeChildResolver.ForBoolPath(selectPath) : _isChosen ??= IsChosen);
        _setNodeExpanded = TreeChildResolver.SetterForBoolPath(expandPath);
        _setNodeSelected = TreeChildResolver.SetterForBoolPath(selectPath);
        _flattener.SetRoots(_roots);
        var flattener = _flattener;
        flattener.Rows.CollectionChanged += OnRowsChanged;
        Follow(nameof(_flattener), () =>
        {
            flattener.Rows.CollectionChanged -= OnRowsChanged;
            flattener.Clear();
        });
        Items.SetSource(_flattener.Rows);   // the flat rows are now the effective item list -> virtualized directly
        RestoreSelectionFromRows();
        _anchorRow = null;
    }

    // The node property the ItemContainerStyle binds <paramref name="property"/> to (a persisted state member), or null if
    // the style doesn't bind it. Read from the style's own setter so there's no second place to declare the same path.
    private string MemberPath(string property)
    {
        if (ItemContainerStyle?.Setters is not { } setters)
        {
            return null;
        }

        foreach (var setter in setters)
        {
            if (setter is Setter s && s.Property == property && s.Value is Binding b)
            {
                return b.Path?.Path;
            }
        }

        return null;
    }

    // After a (re)build, rebuild the selected-row set from the rows the flattener restored as selected, and pick a primary.
    private bool IsChosen(object node) =>
        (SelectedItem is { } chosen && Equals(node, chosen)) || SelectedItems?.Contains(node) == true;

    // Rows built again stand in for the ones they replace, so the selection is held by the rows on screen.
    private void OnRowsChanged(object sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Reset)
        {
            _selectedRows.Clear();
            foreach (var row in _flattener.Rows)
            {
                if (row.IsSelected) _selectedRows.Add(row);
                ScrollIfAwaited(row);
            }

            return;
        }

        object movedAway = null;
        if (e.OldItems != null)
        {
            foreach (TreeRow row in e.OldItems)
            {
                _selectedRows.Remove(row);
                if (!_collapsing && IsChosen(row.Node))
                {
                    movedAway = row.Node;
                }
            }
        }

        if (e.NewItems != null)
        {
            foreach (TreeRow row in e.NewItems)
            {
                if (row.IsSelected) _selectedRows.Add(row);
                ScrollIfAwaited(row);
            }
        }

        // The chosen node went to another parent, maybe into a closed branch: followed there, once the rows settle.
        if (movedAway != null && !string.IsNullOrEmpty(ParentPath))
        {
            LoopSignal.Post(() => FollowMoved(movedAway));
        }
    }

    private bool _collapsing;

    private void FollowMoved(object node)
    {
        if (_flattener == null || !IsChosen(node))
        {
            return;
        }

        if ((RowOf(node) ?? Reveal(node)) is { } row)
        {
            if (!row.IsSelected)
            {
                Select(row);
                SyncSelectionToContainers();
            }

            ScrollToWhenMeasured(row);
            return;
        }

        _awaitedRow = node;
    }

    private void ScrollIfAwaited(TreeRow row)
    {
        if (_awaitedRow != null && Equals(row.Node, _awaitedRow))
        {
            _awaitedRow = null;
            ScrollToWhenMeasured(row);
        }
    }

    private void RestoreSelectionFromRows()
    {
        _selectedRows.Clear();
        object primary = null;
        foreach (var row in _flattener.Rows)
        {
            if (row.IsSelected)
            {
                _selectedRows.Add(row);
                primary ??= row.Node;
            }
        }

        // The node chosen before the rows were made sits in a closed branch: opened to, rather than let go of.
        if (primary == null && !string.IsNullOrEmpty(ParentPath) && SelectedItem is { } chosen
            && Reveal(chosen) is { } revealed)
        {
            HoldOnly(revealed);
            ScrollToWhenMeasured(revealed);
            return;
        }

        Chosen(primary);
    }

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        if (_scrollViewer != null)
        {
            _scrollViewer.ScrollChanged -= OnScrollChanged;
        }

        _scrollViewer = GetTemplateChild("PART_ScrollViewer") as ScrollViewer;
        if (_scrollViewer != null)
        {
            _scrollViewer.ScrollChanged += OnScrollChanged;
        }

        TryApplyDesiredOffset();   // a VerticalOffset bound before the template applied gets applied now (retried as the extent settles)
    }

    /// <summary>Let the template's parts go when the template does - see ScrollBar.OnRemoveTemplate.</summary>
    public override void OnRemoveTemplate()
    {
        base.OnRemoveTemplate();
        if (_scrollViewer != null) _scrollViewer.ScrollChanged -= OnScrollChanged;
        _scrollViewer = null;
    }

    // The scroll metrics moved: either publish the live offset to the bindable property (user scroll -> VM) or, while a
    // set offset is still pending (the extent wasn't tall enough yet), retry applying it now that the metrics grew.
    private void OnScrollChanged(object sender, EventArgs e)
    {
        if (_scrollViewer == null || _applyingOffset)
        {
            return;
        }

        if (_hasDesiredOffset)
        {
            TryApplyDesiredOffset();
            return;
        }

        _applyingOffset = true;
        VerticalOffset = _scrollViewer.ScrollOffset.Y;
        _applyingOffset = false;
    }

    // Apply the desired (VM-set) offset to the ScrollViewer; stop once it lands (or clamps at the bottom because the
    // content isn't that tall). Before the extent is measured the set clamps to 0, so we keep it pending and OnScrollChanged
    // retries as the metrics settle.
    private void TryApplyDesiredOffset()
    {
        if (_scrollViewer == null || !_hasDesiredOffset)
        {
            return;
        }

        _applyingOffset = true;
        _scrollViewer.SetScrollOffset(new Vector2<double>(0, _desiredOffset));
        var y = _scrollViewer.ScrollOffset.Y;
        _applyingOffset = false;
        // Give up ONLY when we actually reached the target, or when the content is measured (maxY > 0) yet genuinely too
        // short to reach it (clamped at the real bottom). Before the extent is measured maxY is 0 and the offset clamps to
        // 0 - that is NOT "reached", so keep the request pending and let the next metrics change (the list gaining height)
        // retry it. Without the maxY>0 guard, `0 >= maxY-1` was true at template time and abandoned every restore.
        var maxY = Math.Max(0, _scrollViewer.ExtentSize.Height - _scrollViewer.ViewportSize.Height);
        if (Math.Abs(y - _desiredOffset) < 1 || (maxY > 0 && y >= maxY - 1))
        {
            _hasDesiredOffset = false;
        }
    }

    private static void OnVerticalOffsetChanged(AdamantiumComponent a, AdamantiumPropertyChangedEventArgs e)
    {
        var tree = (TreeView)a;
        if (tree._applyingOffset)
        {
            return;   // our own echo of a live scroll, not an external (VM) set
        }

        tree._desiredOffset = (double)e.NewValue;
        tree._hasDesiredOffset = true;
        tree.TryApplyDesiredOffset();
    }

    // Every row is a TreeViewItem (flat and indented), whatever the template: the template draws the node.
    protected internal override IUIComponent GetContainerForItem(object item) => TreeViewItem.CreateContainer(ContainerStyle());

    // The container style without its IsExpanded binding, when that names one member: the tree keeps that member itself -
    // writes it as rows open and close, and follows it on the nodes that have it - so a node of another kind, a leaf
    // without the member, is not a binding that cannot resolve.
    private Style ContainerStyle()
    {
        if (ReferenceEquals(_containerStyleOf, ItemContainerStyle))
        {
            return _containerStyle;
        }

        _containerStyleOf = ItemContainerStyle;
        _containerStyle = _containerStyleOf;
        var path = MemberPath(nameof(TreeViewItem.IsExpanded));
        _expandMember = path != null && !path.Contains('.') ? path : null;
        _isNodeExpanded = TreeChildResolver.ForBoolPath(_expandMember);
        if (_expandMember == null)
        {
            return _containerStyle;
        }

        var copy = new Style { Selector = _containerStyleOf.Selector, BasedOn = _containerStyleOf.BasedOn };
        foreach (var setter in _containerStyleOf.Setters)
        {
            if (setter is not Setter { Property: nameof(TreeViewItem.IsExpanded), Value: Binding })
            {
                copy.Setters.Add(setter);
            }
        }

        foreach (var trigger in _containerStyleOf.Triggers)
        {
            copy.Triggers.Add(trigger);
        }

        _containerStyle = copy;
        return copy;
    }

    private void Watch(TreeViewItem container, object node)
    {
        if (_watched.Remove(container, out var old))
        {
            old.PropertyChanged -= OnNodePropertyChanged;
        }

        if (_expandMember != null && node is INotifyPropertyChanged observed)
        {
            observed.PropertyChanged += OnNodePropertyChanged;
            _watched[container] = observed;
        }
    }

    // A node's own member changed - by the application, not by the tree writing it: open or close its row to match.
    private void OnNodePropertyChanged(object sender, PropertyChangedEventArgs e)
    {
        if (_writingNode || (!string.IsNullOrEmpty(e.PropertyName) && e.PropertyName != _expandMember))
        {
            return;
        }

        TreeViewItem shown = null;
        foreach (var (container, node) in _watched)
        {
            if (ReferenceEquals(node, sender))
            {
                shown = container;
                break;
            }
        }

        shown?.SetCurrentValue(TreeViewItem.IsExpandedProperty, _isNodeExpanded(sender));
    }

    protected internal override void ClearContainer(IUIComponent container)
    {
        if (container is TreeViewItem item)
        {
            Watch(item, null);
        }

        base.ClearContainer(container);
    }

    // Bind a (new or recycled) container to a flat row: draw the node via the template header, indent by depth, mirror
    // the row's expand/selection state. The node is the DataContext so the header's {Binding}s resolve against it.
    protected internal override void PrepareContainer(IUIComponent container, object item)
    {
        if (container is TreeViewItem node && item is TreeRow row)
        {
            node.BindRow(row, ItemTemplate ?? ItemTemplateSelector?.SelectTemplate(row.Node, node));
            Watch(node, row.Node);

            // A node opened or closed while it had no row on screen catches up as it is shown.
            if (_expandMember != null && row.HasChildren && _isNodeExpanded(row.Node) != row.IsExpanded)
            {
                node.SetCurrentValue(TreeViewItem.IsExpandedProperty, !row.IsExpanded);
            }
        }
        else
        {
            base.PrepareContainer(container, item);
        }
    }

    // Expand/collapse a row (from its expander or a double-click). Just flip the container's IsExpanded: the change routes
    // through OnIsExpandedChanged -> SyncRowExpansion, the SINGLE splice point that also serves VM-driven expansion.
    internal void ToggleRow(TreeViewItem container)
    {
        if (container.Row is { HasChildren: true } row)
        {
            container.SetCurrentValue(TreeViewItem.IsExpandedProperty, !row.IsExpanded);
        }
    }

    // The single point where a container's IsExpanded change drives the flat list: its expander, a VM two-way binding, code,
    // or spring-load all land here (via OnIsExpandedChanged). Sync the node's OWN IsExpanded FIRST so a lazy branch reads its
    // contents BEFORE the flattener splices them - no placeholder flash - then splice/remove the subtree as one range edit.
    // No-op when the row is already in that state, so it's safe on every change: ToggleRow, restore-on-rebuild, recycling.
    internal void SyncRowExpansion(TreeViewItem container, bool expanded)
    {
        if (container.Row is not { } row) return;

        SetRowExpanded(row, expanded);
        if (expanded) container.SyncHasItems();   // a former leaf may have just become a branch -> reveal its expander
    }

    // The same splice, addressed by ROW rather than by container - what the keyboard has in hand, since it walks the flat
    // list and the row it wants to open may have no realized container at all.
    internal void SetRowExpanded(TreeRow row, bool expanded)
    {
        if (_flattener == null || row.IsExpanded == expanded)
        {
            return;
        }

        if (expanded)
        {
            WriteNodeExpanded(row.Node, true);   // trigger the lazy load before the flattener reads the children
            _flattener.Expand(row);
        }
        else
        {
            _collapsing = true;
            try
            {
                _flattener.Collapse(row);
            }
            finally
            {
                _collapsing = false;
            }

            WriteNodeExpanded(row.Node, false);
        }
    }

    private void WriteNodeExpanded(object node, bool expanded)
    {
        _writingNode = true;
        try
        {
            _setNodeExpanded(node, expanded);
        }
        finally
        {
            _writingNode = false;
        }
    }

    /// <summary>Up/Down move the selection; Left folds a branch or goes to the parent, Right opens a branch or steps into
    /// its first child.</summary>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled || Items.Count == 0) return;

        var index = CurrentRowIndex();
        var row = index >= 0 ? Items[index] as TreeRow : null;

        switch (e.Key)
        {
            case Key.DownArrow:
                GoTo(index + 1);   // nothing selected yet (-1) -> the first row
                break;

            case Key.UpArrow:
                GoTo(index - 1);
                break;

            case Key.Home:
                GoTo(0);
                break;

            case Key.End:
                GoTo(Items.Count - 1);
                break;

            case Key.RightArrow:
                if (row is { HasChildren: true, IsExpanded: false }) Expand(index, row, true);
                else GoTo(index + 1);
                break;

            case Key.LeftArrow:
                if (row is { IsExpanded: true }) Expand(index, row, false);
                else if (row != null) GoTo(ParentIndex(index, row.Depth));
                break;

            default:
                return;   // not the tree's key - leave it for whoever wants it
        }

        // Claimed whether or not it led anywhere: running out of rows must not hand the focus to a neighboring control,
        // and one arrow too many should not cost you your place in the tree.
        e.Handled = true;
    }

    // Expands or folds through the container when realized, so its glyph follows; an unrealized row is spliced directly.
    private void Expand(int index, TreeRow row, bool expanded)
    {
        if (ItemContainerGenerator.ContainerFromIndex(index) is TreeViewItem container)
        {
            container.SetCurrentValue(TreeViewItem.IsExpandedProperty, expanded);
            return;
        }

        SetRowExpanded(row, expanded);
    }

    private int CurrentRowIndex()
    {
        if (KeyboardIndex >= 0 && KeyboardIndex < Items.Count)
        {
            return KeyboardIndex;
        }

        if (FocusManager.Focused is TreeViewItem { Row: { } focused } && Items.IndexOf(focused) is var index && index >= 0)
        {
            return index;
        }

        for (var i = 0; i < Items.Count; i++)
        {
            if (Items[i] is TreeRow row && ReferenceEquals(row.Node, SelectedItem))
            {
                return i;
            }
        }

        return -1;
    }

    // The row this one hangs under: the nearest row ABOVE it that stands one level shallower.
    private int ParentIndex(int index, int depth)
    {
        for (var i = index - 1; i >= 0; i--)
        {
            if (Items[i] is TreeRow row && row.Depth < depth) return i;
        }

        return -1;
    }

    // Select the row at this index and take the view + the focus to it. Out of range = nothing to go to.
    private void GoTo(int index)
    {
        if (index < 0 || index >= Items.Count || Items[index] is not TreeRow row) return;

        SelectOnly(row);
        SyncSelectionToContainers();
        WriteSelectedItems(true);
        ShowRow(index);
    }

    /// <summary>Put the focus on the row and bring it into view. A row the panel has not realized has no visual to focus
    /// - but the panel can still say where it WILL be, and scrolling there materializes it (the same two-step the list
    /// makes, and for the same reason: a selection scrolled far out of the window otherwise goes unreachable).</summary>
    private void ShowRow(int index)
    {
        KeyboardIndex = index;

        var container = ItemContainerGenerator.ContainerFromIndex(index);
        if (container is IInputComponent focusable && container.Visibility == Visibility.Visible)
        {
            FocusManager.Focus(focusable, NavigationMethod.Directional);
        }

        // Ask the panel by index: a container's position lags one layout behind, which makes held arrow keys jitter.
        // The container path is for panels that cannot place by index.
        if (ItemsHostPanel is VirtualizingPanel panel && panel.TryGetItemRect(index, out var rect))
        {
            EnclosingScrollViewer()?.BringIntoView(rect);
            return;
        }

        (container as UIComponent)?.BringIntoView();
    }

    private ScrollViewer EnclosingScrollViewer()
    {
        for (IUIComponent node = ItemsHostPanel; node != null; node = node.VisualParent)
        {
            if (node is ScrollViewer viewer) return viewer;
        }

        return null;
    }

    // A node was clicked: apply the selection policy over the flat ROWS (Single/Multiple/Extended), then mirror the new
    // selection onto the realized containers.
    // A row asked for its menu: it alone becomes the choice, unless it is among the rows chosen already.
    internal void ChooseForMenu(TreeViewItem container)
    {        if (container.Row is { IsSelected: false })
        {
            OnItemClicked(container, InputModifiers.None);
        }
    }

    internal void OnItemClicked(TreeViewItem container, InputModifiers modifiers)
    {
        if (container.Row is not { } row)
        {
            return;
        }

        var replaced = true;
        switch (SelectionMode)
        {
            case TreeViewSelectionMode.Multiple:
                ToggleSelection(row);
                replaced = false;
                break;

            case TreeViewSelectionMode.Extended:
                var ctrl = (modifiers & (InputModifiers.LeftControl | InputModifiers.RightControl)) != 0;
                var shift = (modifiers & (InputModifiers.LeftShift | InputModifiers.RightShift)) != 0;
                if (shift && _anchorRow != null)
                {
                    SelectRange(_anchorRow, row);
                }
                else if (ctrl)
                {
                    ToggleSelection(row);
                    _anchorRow = row;
                    replaced = false;
                }
                else
                {
                    SelectOnly(row);
                    _anchorRow = row;
                }
                break;

            default: // Single
                SelectOnly(row);
                break;
        }

        SyncSelectionToContainers();
        WriteSelectedItems(replaced);
    }

    // WRITTEN FROM OUTSIDE: a node to select, or null for none. What the tree wrote itself is skipped - the rows are
    // already what it says.
    private static void OnSelectedItemChanged(AdamantiumComponent component, AdamantiumPropertyChangedEventArgs e)
    {
        if (component is not TreeView tree || tree._choosing) return;

        tree.Choose(e.NewValue);
    }

    private bool _choosing;

    private void Choose(object node)
    {
        _awaitedRow = null;
        if (node == null)
        {
            if (_selectedRows.Count == 0) return;

            ClearSelection();
            SyncSelectionToContainers();
            return;
        }

        if (_flattener == null) return;

        var revealing = !string.IsNullOrEmpty(ParentPath);
        if ((RowOf(node) ?? (revealing ? Reveal(node) : null)) is not { } row)
        {
            // A node with no row yet - added a moment ago, its row still to come: the old choice goes now, and the row
            // comes marked when it is built, by SelectedItem or by the node's own member, and is scrolled to.
            if (_selectedRows.Count > 0)
            {
                ClearSelection();
                SyncSelectionToContainers();
            }

            _setNodeSelected(node, true);
            if (revealing) _awaitedRow = node;
            return;
        }

        HoldOnly(row);
        if (revealing) ScrollToWhenMeasured(row);
    }

    // The node chosen before its row came, to be scrolled to when it does.
    private object _awaitedRow;

    // The row alone selected, for a choice made outside - not told back. Asked of the selection, not of the row: a row
    // built for a node its view-model already marks selected comes marked, and is still not the one the tree holds.
    private void HoldOnly(TreeRow row)
    {
        if (_selectedRows.Count == 1 && _selectedRows.Contains(row)) return;

        if (SelectedItems is { } list && list.Contains(row.Node))
        {
            if (!row.IsSelected)
            {
                Select(row);
            }

            SyncSelectionToContainers();
            return;
        }

        ClearSelection();
        Select(row);
        SyncSelectionToContainers();
    }

    // Branches opened just now are not measured yet, and a scroll now is held to the list as it was: it goes at the
    // start of the next frame, after the layout that measures them.
    private void ScrollToWhenMeasured(TreeRow row) => LoopSignal.Post(() => ScrollIntoView(row));

    private TreeRow RowOf(object node)
    {
        foreach (var row in _flattener.Rows)
        {
            if (Equals(row.Node, node)) return row;
        }

        return null;
    }

    // Opens the branches above a node that has no row, from the nearest ancestor that has one down to the node's own
    // parent. Null when no ancestor has a row: the node is not under these roots.
    private TreeRow Reveal(object node)
    {
        var parentOf = TreeChildResolver.ForValuePath(ParentPath);
        var above = new Stack<object>();
        TreeRow row = null;
        for (var parent = parentOf(node); parent != null && row == null; parent = parentOf(parent))
        {
            // A chain that comes round again has no top to open from.
            if (Equals(parent, node) || above.Contains(parent)) return null;

            above.Push(parent);
            row = RowOf(parent);
        }

        if (row == null) return null;

        above.Pop();
        Open(row);
        while (above.Count > 0)
        {
            if (RowOf(above.Pop()) is not { } next) return null;
            Open(next);
        }

        return RowOf(node);
    }

    // As the keyboard opens one: through its container when it has one, so the expander shows it open and closes it.
    private void Open(TreeRow row) => Expand(_flattener.Rows.IndexOf(row), row, true);

    // What the TREE decided, said without being told it back. A CURRENT value: a plain set lands at Local, which
    // outranks the binding this is usually read through - and a view-model that answered the pick and let it go could
    // then never give the tree that answer, because its write would land under one the tree had already made.
    private void Chosen(object node)
    {
        _choosing = true;

        try
        {
            SetCurrentValue(SelectedItemProperty, node);
        }
        finally
        {
            _choosing = false;
        }
    }

    // Exactly one row selected - clear every other, select this (Single, and Extended's plain click).
    private void SelectOnly(TreeRow row)
    {
        ClearSelection();
        Select(row);
        Chosen(row.Node);
    }

    /// <summary>Automation's three: this row alone; this one as well; not this one - whatever the mode, which a click
    /// reads as toggle or replace.</summary>
    internal void SelectOnlyRow(TreeRow row)
    {
        SelectOnly(row);
        _anchorRow = row;
        SyncSelectionToContainers();
        WriteSelectedItems(true);
    }

    internal void AddRowToSelection(TreeRow row)
    {
        if (row.IsSelected) return;

        if (SelectionMode == TreeViewSelectionMode.Single && _selectedRows.Count > 0)
        {
            throw new InvalidOperationException("One node is selected here at a time; select it instead.");
        }

        ToggleSelection(row);
        SyncSelectionToContainers();
        WriteSelectedItems(false);
    }

    internal void RemoveRowFromSelection(TreeRow row)
    {
        if (!row.IsSelected) return;

        ToggleSelection(row);
        SyncSelectionToContainers();
        WriteSelectedItems(false);
    }

    // Flip one row, leaving the rest untouched (Multiple, and Extended's Ctrl+click).
    private void ToggleSelection(TreeRow row)
    {
        if (row.IsSelected)
        {
            row.IsSelected = false;
            _setNodeSelected(row.Node, false);
            _selectedRows.Remove(row);
            if (ReferenceEquals(SelectedItem, row.Node))
            {
                Chosen(null);
            }
        }
        else
        {
            Select(row);
            Chosen(row.Node);
        }
    }

    // Select the run between the anchor and the clicked row (inclusive) in flat (on-screen) order, clearing everything
    // else - the list-box Shift+click. The flat list IS the visible order, so the range is a contiguous slice of it.
    private void SelectRange(TreeRow anchor, TreeRow row)
    {
        var rows = _flattener.Rows;
        var a = rows.IndexOf(anchor);
        var b = rows.IndexOf(row);
        if (a < 0 || b < 0)
        {
            SelectOnly(row);
            return;
        }

        if (a > b)
        {
            (a, b) = (b, a);
        }

        ClearSelection();
        for (var i = a; i <= b; i++)
        {
            Select(rows[i]);
        }

        Chosen(row.Node);
    }

    // Mark a row selected - on the row (the container mirror) AND on its node (persisted, reaches off-screen rows).
    private void Select(TreeRow row)
    {
        row.IsSelected = true;
        _setNodeSelected(row.Node, true);
        _selectedRows.Add(row);
    }

    private void ClearSelection()
    {
        foreach (var r in _selectedRows)
        {
            r.IsSelected = false;
            _setNodeSelected(r.Node, false);
        }

        _selectedRows.Clear();
    }

    // Push each realized container's row selection onto the container (its IsSelected trigger paints the highlight).
    // Only the viewport's containers exist, so this is O(viewport); freshly realized/recycled rows are synced in BindRow.
    private void SyncSelectionToContainers()
    {
        if (ItemsHostPanel is not { } panel)
        {
            return;
        }

        foreach (var child in panel.VisualChildren)
        {
            if (child is not TreeViewItem tvi || tvi.Row is not { } row) continue;

            // _selectedRows is the truth: a recycled container's two-way IsSelected binding can mark a row outside it.
            tvi.SetCurrentValue(TreeViewItem.IsSelectedProperty, row.IsSelected);
        }
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new TreeViewAutomationPeer(this);
}
