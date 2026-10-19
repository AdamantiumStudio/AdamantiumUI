using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using Adamantium.Core.Commands;
using Adamantium.UI.Controls.Automation;
using Adamantium.UI.Controls.Panels;
using Adamantium.UI.Controls.Primitives;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Automation;
using Adamantium.UI.Core.Collections;
using Adamantium.UI.Core.Input;
using Adamantium.UI.Core.RoutedEvents;
using Adamantium.UI.Core.Templates;

namespace Adamantium.UI.Controls;

/// <summary>An in-group grid of visual choices. Items are data, since the band and the full drop-down both build cells
/// from <see cref="ItemsControl.ItemTemplate"/>.</summary>
public class RibbonGallery : Selector
{
    public static readonly AdamantiumProperty ColumnsProperty = AdamantiumProperty.Register(nameof(Columns),
        typeof(int), typeof(RibbonGallery), new PropertyMetadata(5, PropertyMetadataOptions.AffectsMeasure, OnShapeChanged));

    public static readonly AdamantiumProperty CompactColumnsProperty = AdamantiumProperty.Register(nameof(CompactColumns),
        typeof(int), typeof(RibbonGallery), new PropertyMetadata(3, PropertyMetadataOptions.AffectsMeasure, OnShapeChanged));

    public static readonly AdamantiumProperty RowsProperty = AdamantiumProperty.Register(nameof(Rows),
        typeof(int), typeof(RibbonGallery), new PropertyMetadata(1, PropertyMetadataOptions.AffectsMeasure, OnShapeChanged));

    public static readonly AdamantiumProperty DropDownColumnsProperty = AdamantiumProperty.Register(nameof(DropDownColumns),
        typeof(int), typeof(RibbonGallery), new PropertyMetadata(0));

    public static readonly AdamantiumProperty GroupMemberPathProperty = AdamantiumProperty.Register(nameof(GroupMemberPath),
        typeof(string), typeof(RibbonGallery), new PropertyMetadata(null, OnGroupMemberPathChanged));

    public static readonly AdamantiumProperty GroupHeaderTemplateProperty = AdamantiumProperty.Register(
        nameof(GroupHeaderTemplate), typeof(DataTemplate), typeof(RibbonGallery), new PropertyMetadata(null, OnShapeChanged));

    /// <summary>The path to what an item's group is. The items of <see cref="ItemsControl.ItemsSource"/> are shown
    /// gathered by it - the groups in the order they first appear, each in the source's order - in the band and in the
    /// drop-down, which titles each group with a <see cref="RibbonGalleryGroupHeader"/>. Items written into
    /// <see cref="ItemsControl.Items"/> keep the order they were written in. A grouped source is changed on the UI
    /// thread: the view that gathers it is not handed changes from another, as the items of an ungrouped one are.
    /// Null - the default - groups nothing.</summary>
    public string GroupMemberPath
    {
        get => GetValue<string>(GroupMemberPathProperty);
        set => SetValue(GroupMemberPathProperty, value);
    }

    /// <summary>How a group's title shows its key. Null shows the key as text.</summary>
    public DataTemplate GroupHeaderTemplate
    {
        get => GetValue<DataTemplate>(GroupHeaderTemplateProperty);
        set => SetValue(GroupHeaderTemplateProperty, value);
    }

    private string _groupReaderPath;
    private Func<object, object> _groupReader;
    private CollectionView _grouped;

    private static void OnGroupMemberPathChanged(AdamantiumComponent a, AdamantiumPropertyChangedEventArgs e)
    {
        if (a is not RibbonGallery gallery) return;

        if (gallery.ItemsSource != null) gallery.ApplyItemsSource(gallery.ItemsSource);
        gallery.ApplyShape();
    }

    // Grouped, the source is shown through a view that gathers each group's items - so the band, the drop-down, the
    // selection and the keyboard all see one order. What is followed here is let go of with the gallery.
    protected override void ApplyItemsSource(IEnumerable newValue)
    {
        if (newValue == null || GroupMemberPath is not { Length: > 0 })
        {
            base.ApplyItemsSource(newValue);
            LetGoOfGrouping();
            return;
        }

        // The old grouping goes before anything new listens, or on the same source the gallery's handler would come
        // first - ahead of the order it asks and the view it sorts.
        LetGoOfGrouping();
        var order = new GroupOrder(newValue, GroupOf);
        var view = new CollectionView { CustomSort = order };
        // The order hears of a change before the view places it, and the gallery after, to sort anew if the groups moved.
        Follow(nameof(GroupOrder), newValue as INotifyCollectionChanged, order.OnSourceChanged);
        view.Source = newValue;
        base.ApplyItemsSource(view);
        Follow(nameof(_grouped), view.LetGoOfSource);
        Follow(nameof(OnGroupedSourceChanged), newValue as INotifyCollectionChanged, OnGroupedSourceChanged);
        _grouped = view;
        _groupOrder = order;
        order.Moved();
    }

    private GroupOrder _groupOrder;

    private void LetGoOfGrouping()
    {
        Follow(nameof(GroupOrder), null);
        Follow(nameof(_grouped), null);
        Follow(nameof(OnGroupedSourceChanged), null);
        _grouped = null;
        _groupOrder = null;
    }

    private void OnGroupedSourceChanged(object sender, NotifyCollectionChangedEventArgs e)
    {
        // Only a change the view placed in place can leave it out of order; on the rest it was rebuilt whole.
        if (_groupOrder?.Moved() == true && e.Action is NotifyCollectionChangedAction.Add or NotifyCollectionChangedAction.Remove
                or NotifyCollectionChangedAction.Replace)
        {
            _grouped.Refresh();
        }
    }

    // Groups by where each first appears in the source; a group that comes later goes after the rest.
    private sealed class GroupOrder(IEnumerable source, Func<object, object> groupOf) : IComparer
    {
        private static readonly object NoGroup = new();
        private readonly Dictionary<object, int> _ranks = new();
        private readonly List<object> _laidOut = [];
        private bool _stale = true;

        public void OnSourceChanged(object sender, NotifyCollectionChangedEventArgs e) => _stale = true;

        public int Compare(object left, object right) => RankOf(left).CompareTo(RankOf(right));

        /// <summary>Whether the groups that were there stand in another order now - the view placed only what changed,
        /// and has to be sorted anew. A group that came or went moves no other.</summary>
        public bool Moved()
        {
            Rank();
            var now = new List<object>(_ranks.Count);
            foreach (var (key, _) in _ranks)
            {
                now.Add(key);
            }

            now.Sort((a, b) => _ranks[a].CompareTo(_ranks[b]));
            var kept = now.FindAll(_laidOut.Contains);
            var before = _laidOut.FindAll(_ranks.ContainsKey);
            _laidOut.Clear();
            _laidOut.AddRange(now);

            for (var i = 0; i < kept.Count; i++)
            {
                if (!Equals(kept[i], before[i]))
                {
                    return true;
                }
            }

            return false;
        }

        private int RankOf(object item)
        {
            Rank();
            return _ranks.TryGetValue(groupOf(item) ?? NoGroup, out var rank) ? rank : int.MaxValue;
        }

        private void Rank()
        {
            if (!_stale)
            {
                return;
            }

            _stale = false;
            _ranks.Clear();
            foreach (var item in source)
            {
                _ranks.TryAdd(groupOf(item) ?? NoGroup, _ranks.Count);
            }
        }
    }

    private object GroupOf(object item)
    {
        if (!string.Equals(_groupReaderPath, GroupMemberPath, StringComparison.Ordinal))
        {
            _groupReaderPath = GroupMemberPath;
            _groupReader = TreeChildResolver.ForValuePath(GroupMemberPath);
        }

        return _groupReader(item);
    }

    public static readonly AdamantiumProperty ItemCommandProperty = AdamantiumProperty.Register(nameof(ItemCommand),
        typeof(ICommand), typeof(RibbonGallery), new PropertyMetadata(null, OnItemCommandChanged));

    /// <summary>Makes the gallery run its choices rather than hold one, as a gallery of shapes to insert does: a picked
    /// item is passed to the command, and the gallery has no selection - one set on it is dropped. A choice the command
    /// cannot run is shown disabled. Null - the default - picks by selecting.</summary>
    public ICommand ItemCommand
    {
        get => GetValue<ICommand>(ItemCommandProperty);
        set => SetValue(ItemCommandProperty, value);
    }

    private CommandSourceListener<RibbonGallery> _itemCommandListener;

    private static void OnItemCommandChanged(AdamantiumComponent a, AdamantiumPropertyChangedEventArgs e)
    {
        if (a is not RibbonGallery gallery) return;

        var command = e.NewValue as ICommand;
        gallery._itemCommandListener ??= new CommandSourceListener<RibbonGallery>(gallery, static g => g.UpdateCanRun());
        gallery._itemCommandListener.Listen(command);
        if (command != null) gallery.SelectSingle(-1);
        gallery.UpdateCanRun();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _itemCommandListener?.Attached();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _itemCommandListener?.Detached();
    }

    private void UpdateCanRun()
    {
        foreach (var (container, item) in RealizedContainers())
        {
            if (container is RibbonGalleryItem cell)
            {
                cell.SetCurrentValue(IsEnabledProperty, CanRun(item));
            }
        }
    }

    private void ShowCanRun(RibbonGalleryItem cell, object item)
    {
        if (ItemCommand != null)
        {
            cell.SetCurrentValue(IsEnabledProperty, CanRun(item));
        }
    }

    private bool CanRun(object item) => CommandManager.QueryFrom(ItemCommand, item, this, null).CanExecute;

    protected override void OnSelectedIndexSet(int index)
    {
        if (ItemCommand != null)
        {
            SelectSingle(-1);
            return;
        }

        base.OnSelectedIndexSet(index);
    }

    // A selection named before the items came is taken when they do, past OnSelectedIndexSet.
    protected override void OnItemsChanged(NotifyCollectionChangedEventArgs e)
    {
        base.OnItemsChanged(e);
        if (ItemCommand != null && SelectedIndex >= 0) SelectSingle(-1);
    }

    protected override void OnSelectedItemSet(object item)
    {
        if (ItemCommand != null)
        {
            SelectSingle(-1);
            return;
        }

        base.OnSelectedItemSet(item);
    }

    /// <summary>How many choices stand side by side while the group is roomy.</summary>
    public int Columns
    {
        get => GetValue<int>(ColumnsProperty);
        set => SetValue(ColumnsProperty, value);
    }

    /// <summary>...and how many once the group has been narrowed to <see cref="RibbonSize.Medium"/>. Stated rather than
    /// derived: how few cells still read as a gallery is a fact about the thumbnails, which only their author knows.</summary>
    public int CompactColumns
    {
        get => GetValue<int>(CompactColumnsProperty);
        set => SetValue(CompactColumnsProperty, value);
    }

    /// <summary>How many rows stand in the band. The rest are reached with the arrows or in the drop-down.</summary>
    public int Rows
    {
        get => GetValue<int>(RowsProperty);
        set => SetValue(RowsProperty, value);
    }

    /// <summary>Columns in the dropped-down gallery. 0 = as many as <see cref="Columns"/>.</summary>
    public int DropDownColumns
    {
        get => GetValue<int>(DropDownColumnsProperty);
        set => SetValue(DropDownColumnsProperty, value);
    }

    // --- What the band's ladder does to it --------------------------------------------------------------------------
    //
    // The gallery rides the SAME ladder as every other command: the group hands it a RibbonSize, and it answers
    // in columns. Small is where a gallery stops being one - three thumbnails in a strip say nothing - so there it
    // becomes the chevron alone, and the whole set is one press away.

    public static readonly AdamantiumProperty EffectiveColumnsProperty = AdamantiumProperty.RegisterReadOnly(
        nameof(EffectiveColumns), typeof(int), typeof(RibbonGallery),
        new PropertyMetadata(5, PropertyMetadataOptions.AffectsMeasure));

    public static readonly AdamantiumProperty IsCollapsedProperty = AdamantiumProperty.RegisterReadOnly(
        nameof(IsCollapsed), typeof(bool), typeof(RibbonGallery),
        new PropertyMetadata(false, PropertyMetadataOptions.AffectsMeasure));

    /// <summary>Columns at the size the group is drawing it - what the items panel is laid out in.</summary>
    public int EffectiveColumns
    {
        get => GetValue<int>(EffectiveColumnsProperty);
        private set => SetValue(EffectiveColumnsProperty, value);
    }

    /// <summary>Nothing but the chevron: the band has no room for a grid of pictures.</summary>
    public bool IsCollapsed
    {
        get => GetValue<bool>(IsCollapsedProperty);
        private set => SetValue(IsCollapsedProperty, value);
    }

    public RibbonGallery()
    {
        Items.CollectionChanged += (_, _) =>
        {
            Clamp();
            RefreshDropDown();
        };

        PointerWheelChanged += OnWheel;
    }

    // The wheel steps the visible rows; at either end it stays unhandled so the surrounding strip scrolls, as in
    // ScrollViewer. Tilt passes through.
    private void OnWheel(object sender, Core.Input.PointerWheelEventArgs e)
    {
        if (e.IsHorizontal) return;

        var rows = Math.Max(1, Math.Abs(e.Delta) / 120);
        if (e.Delta > 0)
        {
            if (!CanScrollUp) return;
            FirstRow -= rows;
        }
        else
        {
            if (!CanScrollDown) return;
            FirstRow += rows;
        }

        e.Handled = true;
    }

    static RibbonGallery()
    {
        // Metadata is MERGED, so the base AffectsMeasure stays and this callback is added to it.
        Ribbon.SizeProperty.OverrideMetadata(typeof(RibbonGallery),
            new PropertyMetadata(RibbonSize.Large, PropertyMetadataOptions.AffectsMeasure, OnRibbonSizeChanged));
    }

    private static void OnRibbonSizeChanged(AdamantiumComponent a, AdamantiumPropertyChangedEventArgs e)
    {
        (a as RibbonGallery)?.ApplyShape();
    }

    private static void OnShapeChanged(AdamantiumComponent a, AdamantiumPropertyChangedEventArgs e)
    {
        (a as RibbonGallery)?.ApplyShape();
    }

    private void ApplyShape()
    {
        var size = Ribbon.GetSize(this);
        IsCollapsed = size == RibbonSize.Small;
        EffectiveColumns = Math.Max(1, size == RibbonSize.Large ? Columns : CompactColumns);
        Clamp();
        RefreshDropDown();
    }

    // --- Scrolling the rows -----------------------------------------------------------------------------------------

    public static readonly AdamantiumProperty FirstRowProperty = AdamantiumProperty.Register(nameof(FirstRow),
        typeof(int), typeof(RibbonGallery), new PropertyMetadata(0, OnFirstRowChanged));

    public static readonly AdamantiumProperty CanScrollUpProperty = AdamantiumProperty.RegisterReadOnly(
        nameof(CanScrollUp), typeof(bool), typeof(RibbonGallery), new PropertyMetadata(false));

    public static readonly AdamantiumProperty CanScrollDownProperty = AdamantiumProperty.RegisterReadOnly(
        nameof(CanScrollDown), typeof(bool), typeof(RibbonGallery), new PropertyMetadata(false));

    /// <summary>The topmost shown row.</summary>
    public int FirstRow
    {
        get => GetValue<int>(FirstRowProperty);
        set => SetValue(FirstRowProperty, value);
    }

    public bool CanScrollUp
    {
        get => GetValue<bool>(CanScrollUpProperty);
        private set => SetValue(CanScrollUpProperty, value);
    }

    public bool CanScrollDown
    {
        get => GetValue<bool>(CanScrollDownProperty);
        private set => SetValue(CanScrollDownProperty, value);
    }

    /// <summary>How many rows the items make at the columns currently drawn.</summary>
    public int RowCount => (Items.Count + Math.Max(1, EffectiveColumns) - 1) / Math.Max(1, EffectiveColumns);

    private static void OnFirstRowChanged(AdamantiumComponent a, AdamantiumPropertyChangedEventArgs e)
    {
        (a as RibbonGallery)?.Clamp();
    }

    public void ScrollUp() => FirstRow--;

    public void ScrollDown() => FirstRow++;

    private bool _clamping;

    private void Clamp()
    {
        if (_clamping) return;

        _clamping = true;
        try
        {
            // Rows that FIT, not rows asked for: a viewport a little shorter than two cells shows one whole row and
            // part of another, and clamping to the asked-for two left the last row permanently half cut with nowhere
            // further to scroll.
            var shown = (ItemsHostPanel as Panels.RibbonGalleryPanel)?.VisibleRows ?? Math.Max(1, Rows);
            var last = Math.Max(0, RowCount - shown);
            var clamped = Math.Min(Math.Max(0, FirstRow), last);
            if (clamped != FirstRow) FirstRow = clamped;

            CanScrollUp = clamped > 0;
            CanScrollDown = clamped < last;
        }
        finally
        {
            _clamping = false;
        }
    }

    // --- The drop-down: the whole set at once -----------------------------------------------------------------------

    public static readonly AdamantiumProperty IsDropDownOpenProperty = AdamantiumProperty.Register(nameof(IsDropDownOpen),
        typeof(bool), typeof(RibbonGallery), new PropertyMetadata(false, OnIsDropDownOpenChanged));

    public bool IsDropDownOpen
    {
        get => GetValue<bool>(IsDropDownOpenProperty);
        set => SetValue(IsDropDownOpenProperty, value);
    }

    private static void OnIsDropDownOpenChanged(AdamantiumComponent a, AdamantiumPropertyChangedEventArgs e)
    {
        if (a is not RibbonGallery gallery) return;

        var open = Equals(e.NewValue, true);
        if (open) gallery.RefreshDropDown();
        if (gallery._popup != null) gallery._popup.IsOpen = open;
        if (!open) gallery.RefreshDropDown();   // closed first, so the popup is never seen empty
    }

    private Popup _popup;
    private RibbonGalleryPanel _dropDownPanel;
    private readonly List<(IUIComponent Container, object Item)> _dropDownCells = [];

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();

        _popup = GetTemplateChild("PART_Popup") as Popup;
        if (_popup != null)
        {
            _popup.PlacementTarget = this;
            // WE own the close - the chevron's own press must not light-dismiss first, or the press would close and
            // re-open and read as doing nothing.
            _popup.IgnoreTargetPress = true;
            // Named handlers, not lambdas: a part outlives neither its template nor a theme swap, and what is subscribed
            // here has to be given back in OnRemoveTemplate - which a lambda makes impossible.
            _popup.Closed -= OnDropDownClosed;
            _popup.Closed += OnDropDownClosed;

            // The card is built on first open, so the panel is not in this template's namescope - it arrives with the
            // content, and the cells are filled in then.
            _popup.ContentBuilt -= OnDropDownContentBuilt;
            _popup.ContentBuilt += OnDropDownContentBuilt;
            _popup.IsOpen = IsDropDownOpen;
        }

        _dropDownPanel = null;

        if (GetTemplateChild("PART_ScrollUp") is Primitives.ButtonBase up) up.Click += (_, _) => ScrollUp();
        if (GetTemplateChild("PART_ScrollDown") is Primitives.ButtonBase down) down.Click += (_, _) => ScrollDown();
        if (GetTemplateChild("PART_More") is Primitives.ButtonBase more) more.Click += (_, _) => IsDropDownOpen = !IsDropDownOpen;

        RefreshDropDown();
    }

    // How many rows FIT is only known once the panel has been given its height, so the clamp is re-asked here.
    protected override Size ArrangeOverride(Size finalSize)
    {
        var size = base.ArrangeOverride(finalSize);
        Clamp();
        return size;
    }

    /// <summary>Let the template's parts go when the template does - see ScrollBar.OnRemoveTemplate.</summary>
    public override void OnRemoveTemplate()
    {
        base.OnRemoveTemplate();
        if (_popup != null)
        {
            _popup.Closed -= OnDropDownClosed;
            _popup.ContentBuilt -= OnDropDownContentBuilt;
        }

        _popup = null;
        _dropDownPanel = null;
    }

    private void OnDropDownClosed(object sender, EventArgs e) => SetCurrentValue(IsDropDownOpenProperty, false);

    private void OnDropDownContentBuilt(object sender, EventArgs e)
    {
        _dropDownPanel = ((Popup)sender).FindContentChild("PART_DropDownPanel") as RibbonGalleryPanel;
        RefreshDropDown();
    }

    // Cells exist only while the drop-down is down - a set nobody is looking at is a set of visuals nobody is looking at.
    private void RefreshDropDown()
    {
        if (_dropDownPanel == null) return;

        _dropDownPanel.Children.Clear();
        _dropDownCells.Clear();

        if (!IsDropDownOpen) return;

        _dropDownPanel.Columns = DropDownColumns > 0 ? DropDownColumns : Math.Max(1, Columns);

        var grouped = GroupMemberPath is { Length: > 0 };
        object group = null;
        var first = true;
        foreach (var item in Items)
        {
            var key = grouped ? GroupOf(item) : null;
            if (grouped && (first || !Equals(key, group)))
            {
                group = key;
                _dropDownPanel.Children.Add(new RibbonGalleryGroupHeader
                {
                    DataContext = group,
                    Content = group,
                    ContentTemplate = GroupHeaderTemplate
                });
            }

            first = false;
            var cell = new RibbonGalleryItem
            {
                Owner = this,
                DataContext = item,
                ContentTemplate = ItemTemplate,
                ContentTemplateSelector = ItemTemplateSelector,
                Content = item,
                IsSelected = IsItemSelected(item)
            };

            ShowCanRun(cell, item);
            if (ItemContainerStyle != null) cell.Styles.Add(ItemContainerStyle);

            _dropDownPanel.Children.Add(cell);
            _dropDownCells.Add((cell, item));
        }

        _dropDownPanel.Rows = Math.Max(1, _dropDownPanel.RowCount);
    }

    /// <summary>The dropped-down cells stand for the same items, so the selection has to reach them too - they are not
    /// this control's generator's, and the base would light up nothing there.</summary>
    protected override IEnumerable<(IUIComponent Container, object Item)> RealizedContainers()
    {
        foreach (var pair in base.RealizedContainers()) yield return pair;
        foreach (var pair in _dropDownCells) yield return pair;
    }

    // --- Items ------------------------------------------------------------------------------------------------------

    protected internal override IUIComponent GetContainerForItem(object item)
    {
        var cell = new RibbonGalleryItem { Owner = this };
        if (ItemContainerStyle != null) cell.Styles.Add(ItemContainerStyle);
        return cell;
    }

    protected internal override void PrepareContainer(IUIComponent container, object item)
    {
        if (container is not RibbonGalleryItem cell)
        {
            base.PrepareContainer(container, item);
            return;
        }

        if (!ReferenceEquals(cell, item))
        {
            cell.DataContext = item;
            cell.ContentTemplate = ItemTemplate;
            cell.ContentTemplateSelector = ItemTemplateSelector;
            cell.Content = item;
        }

        cell.IsSelected = IsItemSelected(item);
        ShowCanRun(cell, item);
    }

    protected internal override void ClearContainer(IUIComponent container)
    {
        if (container is RibbonGalleryItem cell)
        {
            cell.DataContext = null;
            cell.IsSelected = false;
            return;
        }

        base.ClearContainer(container);
    }

    /// <summary>A cell was picked - in the band or in the drop-down. Picking closes the drop-down: the choice is made.</summary>
    internal void PickFromContainer(RibbonGalleryItem cell)
    {
        var item = ItemFor(cell);
        var index = IndexOfItem(item);
        if (index < 0) return;

        if (ItemCommand is { } command)
        {
            if (!CanRun(item)) return;

            IsDropDownOpen = false;
            CommandManager.ExecuteFrom(command, item, this, null);
            return;
        }

        IsDropDownOpen = false;
        SelectSingle(index);
    }

    /// <summary>Whether the item this cell stands for is the current choice - asked by a cell realized after the fact.</summary>
    internal bool IsItemSelectedFor(RibbonGalleryItem cell)
    {
        var item = ItemFor(cell);
        return item != null && IsItemSelected(item);
    }

    private object ItemFor(RibbonGalleryItem cell)
    {
        foreach (var (container, item) in _dropDownCells)
        {
            if (ReferenceEquals(container, cell)) return item;
        }

        var index = ItemContainerGenerator?.IndexFromContainer(cell) ?? -1;
        return index >= 0 && index < Items.Count ? Items[index] : null;
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new RibbonGalleryAutomationPeer(this);
}
