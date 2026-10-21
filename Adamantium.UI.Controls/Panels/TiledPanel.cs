using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using Adamantium.Core.Collections;
using Adamantium.Mathematics;
using Adamantium.UI.Controls.Base;
using Adamantium.UI.Core;
using Adamantium.UI.Core.RoutedEvents;
using Adamantium.UI.Core.Input;

namespace Adamantium.UI.Controls.Panels;

/// <summary>
/// Lays its children out as tiles in the tree of regions <see cref="Layout"/> holds: splits into parts by share, single
/// tiles, and flows of tiles for any number of children (<see cref="TileLayouts"/> has the usual layouts). A tile takes
/// the child whose <see cref="TileProperty"/> is its name, else the next child in order; a flow takes the children left
/// over; a child nothing takes is collapsed. <see cref="MaximizedItem"/> fills the panel alone. The gaps of the splits
/// (<see cref="Spacing"/>) can be dragged, gaps in line moving together; a double click on one shares its splits evenly
/// again, <see cref="ShareEvenly"/> every split. The panel owns its children's visibility. It fills the room it is
/// given; where that is unbounded it asks only for its largest child, so it does not scroll. Hosting a list's items, it
/// opens a gap where a dragged item would drop: the items dragged out of it leave their tiles, the others move aside.
/// </summary>
public class TiledPanel : Panel, IDropGapPanel
{
    private const double MinTileSize = 32;
    private const double GripSlack = 3;

    private readonly TileFlow _everyChild = new();
    private readonly List<IMeasurableComponent> _free = new();
    private readonly Dictionary<Tile, IMeasurableComponent> _tileChildren = new();
    private readonly Dictionary<TileFlow, (int Start, int Count)> _flowRanges = new();
    private readonly List<IMeasurableComponent> _flowChildren = new();
    private readonly List<TileRegion> _leaves = new();
    private readonly Dictionary<IMeasurableComponent, Rect> _placed = new();
    private readonly List<Grip> _grips = new();
    private readonly List<Grip> _dragged = new();
    private TileRegion _watched;
    private readonly List<IMeasurableComponent> _sequence = new();
    private readonly Dictionary<IMeasurableComponent, Rect> _lastRects = new();
    private readonly HashSet<UIComponent> _moving = new();
    private int _gapIndex = -1;
    private IList _lifted;
    private UIComponent _gapCard;
    private bool _animateMoves;

    public TiledPanel()
    {
        Children.CollectionChanged += OnChildrenChanged;
    }

    private readonly record struct Grip(TileSplit Split, int Index, bool Vertical, Rect Bar, double Start, double Room);

    public static readonly AdamantiumProperty LayoutProperty = AdamantiumProperty.Register(nameof(Layout),
        typeof(TileRegion), typeof(TiledPanel),
        new PropertyMetadata(null, PropertyMetadataOptions.AffectsMeasure, LayoutChangedCallback));

    public static readonly AdamantiumProperty MaximizedItemProperty = AdamantiumProperty.Register(nameof(MaximizedItem),
        typeof(object), typeof(TiledPanel), new PropertyMetadata(null, PropertyMetadataOptions.AffectsMeasure));

    public static readonly AdamantiumProperty SpacingProperty = AdamantiumProperty.Register(nameof(Spacing),
        typeof(double), typeof(TiledPanel), new PropertyMetadata(4.0, PropertyMetadataOptions.AffectsMeasure));

    public static readonly AdamantiumProperty TileProperty = AdamantiumProperty.RegisterAttached("Tile",
        typeof(string), typeof(AdamantiumComponent), new PropertyMetadata(null, PropertyMetadataOptions.AffectsParentMeasure));

    /// <summary>The regions the children are laid out in; null lays every child in a <see cref="TileFlow"/>.</summary>
    public TileRegion Layout
    {
        get => GetValue<TileRegion>(LayoutProperty);
        set => SetValue(LayoutProperty, value);
    }

    /// <summary>The child that fills the panel alone, the others collapsed - the child itself or its data; null lays
    /// them all out as <see cref="Layout"/> says.</summary>
    public object MaximizedItem
    {
        get => GetValue(MaximizedItemProperty);
        set => SetValue(MaximizedItemProperty, value);
    }

    /// <summary>The gap between tiles; the gaps of splits are what is dragged.</summary>
    public double Spacing
    {
        get => GetValue<double>(SpacingProperty);
        set => SetValue(SpacingProperty, value);
    }

    /// <summary>The name of the <see cref="Tile"/> a child takes.</summary>
    public static string GetTile(IAdamantiumComponent element) => element.GetValue<string>(TileProperty);

    /// <summary>Sets the name of the <see cref="Tile"/> a child takes.</summary>
    public static void SetTile(IAdamantiumComponent element, string value) => element.SetValue(TileProperty, value);

    /// <summary>Gives every part of every split an even share of its room again.</summary>
    public void ShareEvenly() => Layout?.ShareEvenly();

    /// <summary>Whether the panel hosts a list's items, so a drag over it opens a gap.</summary>
    public bool SupportsDropGap => Owner != null;

    /// <summary>The index in the owner's items a drop at <paramref name="point"/> lands before, so that the gap takes the
    /// tile under it: that tile, or past the gap the one after it, with the <paramref name="lifted"/> items left out;
    /// the count for the end.</summary>
    public bool TryGetDropSlot(Vector2 point, IList lifted, out int index)
    {
        index = -1;
        var items = Owner?.Items;
        if (items == null)
        {
            return false;
        }

        var gap = _gapCard != null && _gapIndex >= 0 ? _sequence.IndexOf((IMeasurableComponent)_gapCard) : -1;
        var reach = Spacing / 2;
        for (var q = 0; q < _sequence.Count; q++)
        {
            var entry = _sequence[q];
            if (!_placed.TryGetValue(entry, out var rect) || !Near(rect, reach, point))
            {
                continue;
            }

            if (q == gap)
            {
                index = _gapIndex;
                return true;
            }

            var anchor = gap >= 0 ? (q < gap ? _sequence[q] : Next(q + 1)) : Unlifted(q, lifted);
            index = anchor != null ? IndexOf(items, anchor) : items.Count;
            if (index < 0)
            {
                index = items.Count;
            }

            return true;
        }

        index = _gapIndex;
        return gap >= 0;
    }

    /// <summary>Opens the gap before the item at <paramref name="index"/>, or closes it at -1; the
    /// <paramref name="lifted"/> items leave their tiles while it is open.</summary>
    public void ShowDropGap(int index, IList lifted)
    {
        lifted = index < 0 ? null : lifted;
        if (index == _gapIndex && ReferenceEquals(lifted, _lifted))
        {
            return;
        }

        _gapIndex = index;
        _lifted = lifted;
        _animateMoves = true;
        InvalidateMeasure();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Watch(Layout);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        Watch(null);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var finite = !double.IsInfinity(availableSize.Width) && !double.IsInfinity(availableSize.Height);
        Lay(finite ? availableSize : default);
        if (_gapCard != null)
        {
            var open = _placed.TryGetValue((IMeasurableComponent)_gapCard, out var gapTile);
            _gapCard.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
            if (open)
            {
                ((IMeasurableComponent)_gapCard).Measure(finite ? gapTile.Size : availableSize);
            }
        }

        var desired = default(Size);
        foreach (var child in Children)
        {
            var shown = _placed.TryGetValue(child, out var tile);
            child.Visibility = shown ? Visibility.Visible : Visibility.Collapsed;
            if (!shown)
            {
                continue;
            }

            child.Measure(finite ? tile.Size : availableSize);
            desired = new Size(Math.Max(desired.Width, child.DesiredSize.Width),
                Math.Max(desired.Height, child.DesiredSize.Height));
        }

        return finite ? availableSize : desired;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        Lay(finalSize);
        if (_gapIndex < 0 && !_animateMoves && _moving.Count > 0)
        {
            foreach (var element in _moving)
            {
                LayoutMotion.Promote(element, false);
            }

            _moving.Clear();
        }

        foreach (var child in Children)
        {
            if (child.Visibility == Visibility.Collapsed || !_placed.TryGetValue(child, out var tile))
            {
                _lastRects.Remove(child);
                continue;
            }

            child.Arrange(tile);
            if (_animateMoves && _lastRects.TryGetValue(child, out var before) && before != tile
                && child is UIComponent element && tile.Width > 0 && tile.Height > 0)
            {
                LayoutMotion.Promote(element, true);
                _moving.Add(element);
                LayoutMotion.Morph(element, before.Location - tile.Location, before.Width / tile.Width,
                    before.Height / tile.Height, () => Landed(element));
            }

            _lastRects[child] = tile;
        }

        if (_gapCard != null && _placed.TryGetValue((IMeasurableComponent)_gapCard, out var gapTile))
        {
            ((IMeasurableComponent)_gapCard).Arrange(gapTile);
        }

        _animateMoves = false;
        return finalSize;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!ReferenceEquals(e.OriginalSource, this) || e.ChangedButton != MouseButtons.Left)
        {
            return;
        }

        GripsAt(e.GetPosition(this), _dragged);
        if (_dragged.Count == 0)
        {
            return;
        }

        e.Handled = true;
        if (e.ClickCount >= 2)
        {
            foreach (var grip in _dragged)
            {
                grip.Split.ShareOwnEvenly();
            }

            _dragged.Clear();
            return;
        }

        e.Pointer.Capture(this);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var position = e.GetPosition(this);
        if (_dragged.Count > 0)
        {
            e.Handled = true;
            foreach (var grip in _dragged)
            {
                Move(Current(grip), grip.Vertical ? position.X : position.Y);
            }

            return;
        }

        var (vertical, horizontal) = ReferenceEquals(e.OriginalSource, this) ? GripKindsAt(position) : (false, false);
        ShowGripCursor(vertical, horizontal);
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        if (_dragged.Count == 0)
        {
            ShowGripCursor(false, false);
        }
    }

    protected override void OnPointerCaptureLost(PointerEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        if (!IsPointerCaptured)
        {
            _dragged.Clear();
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_dragged.Count == 0)
        {
            return;
        }

        e.Handled = true;
        _dragged.Clear();
        e.Pointer.Capture(null);
    }

    private static void LayoutChangedCallback(AdamantiumComponent a, AdamantiumPropertyChangedEventArgs e)
    {
        var panel = (TiledPanel)a;
        if (panel.IsAttachedToVisualTree)
        {
            panel.Watch(e.NewValue as TileRegion);
        }
    }

    private void Watch(TileRegion layout)
    {
        if (ReferenceEquals(_watched, layout))
        {
            return;
        }

        if (_watched != null)
        {
            _watched.Changed -= OnLayoutChanged;
        }

        _watched = layout;
        if (layout != null)
        {
            layout.Changed += OnLayoutChanged;
        }
    }

    private void OnLayoutChanged(object sender, EventArgs e) => InvalidateMeasure();

    private ItemsControl Owner => (VisualParent as ItemsPresenter)?.Owner;

    private bool IsLifted(IMeasurableComponent child) =>
        _lifted != null && child is IFundamentalUIComponent { DataContext: { } data } && _lifted.Contains(data);

    private void OpenGap()
    {
        var items = Owner?.Items;
        if (_gapIndex < 0 || items == null)
        {
            return;
        }

        var card = GapCard();
        var at = _free.Count;
        for (var i = _gapIndex; i < items.Count; i++)
        {
            if (_lifted?.Contains(items[i]) == true)
            {
                continue;
            }

            for (var f = 0; f < _free.Count; f++)
            {
                if (_free[f] is IFundamentalUIComponent { DataContext: { } data } && Equals(data, items[i]))
                {
                    at = f;
                    break;
                }
            }

            break;
        }

        _free.Insert(at, card);
    }

    private IMeasurableComponent GapCard()
    {
        if (_gapCard == null)
        {
            _gapCard = Owner?.DropPlaceholderTemplate?.Build(this).RootComponent as UIComponent ?? new Decorators.Border();
            _gapCard.IsHitTestVisible = false;
            AddVisualChild(_gapCard);
        }

        return (IMeasurableComponent)_gapCard;
    }

    private IMeasurableComponent Next(int from) => from < _sequence.Count ? _sequence[from] : null;

    private IMeasurableComponent Unlifted(int position, IList lifted)
    {
        var seen = 0;
        foreach (var entry in _sequence)
        {
            if (lifted != null && entry is IFundamentalUIComponent { DataContext: { } data } && lifted.Contains(data))
            {
                continue;
            }

            if (seen++ == position)
            {
                return entry;
            }
        }

        return null;
    }

    private static bool Near(Rect rect, double reach, Vector2 point) =>
        point.X >= rect.X - reach && point.X <= rect.Right + reach && point.Y >= rect.Y - reach
        && point.Y <= rect.Bottom + reach;

    private static int IndexOf(ItemCollection items, IMeasurableComponent entry) =>
        entry is IFundamentalUIComponent { DataContext: { } data } ? items.IndexOf(data) : -1;

    private void OnChildrenChanged(object sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Reset)
        {
            _lastRects.Clear();
            _sequence.Clear();
            _free.Clear();
            _placed.Clear();
        }

        if (e.OldItems == null)
        {
            return;
        }

        foreach (var child in e.OldItems)
        {
            if (child is not IMeasurableComponent measurable || Children.Contains(measurable))
            {
                continue;
            }

            _lastRects.Remove(measurable);
            _sequence.Remove(measurable);
            ((IAdamantiumComponent)measurable).ClearValue(VisibilityProperty);
        }
    }

    private void Landed(UIComponent element)
    {
        if (_gapIndex < 0 && _moving.Remove(element))
        {
            LayoutMotion.Promote(element, false);
        }
    }

    private Grip Current(Grip grip)
    {
        foreach (var laid in _grips)
        {
            if (ReferenceEquals(laid.Split, grip.Split) && laid.Index == grip.Index)
            {
                return laid;
            }
        }

        return grip;
    }

    private void ShowGripCursor(bool vertical, bool horizontal)
    {
        if (vertical || horizontal)
        {
            Cursor = vertical && horizontal ? Cursors.SizeAll : vertical ? Cursors.SizeEWE : Cursors.SizeNS;
        }
        else if (Cursor != null)
        {
            ClearValue(CursorProperty);
        }
    }

    private void Lay(Size size)
    {
        _placed.Clear();
        _grips.Clear();
        var maximized = Maximized();
        if (maximized != null)
        {
            _placed[maximized] = new Rect(0, 0, size.Width, size.Height);
            return;
        }

        var layout = Layout ?? _everyChild;
        Assign(layout);
        LayRegion(layout, new Rect(0, 0, size.Width, size.Height));
    }

    private void Assign(TileRegion layout)
    {
        _free.Clear();
        _tileChildren.Clear();
        _flowRanges.Clear();
        _flowChildren.Clear();
        _leaves.Clear();
        foreach (var child in Children)
        {
            if (!IsLifted(child))
            {
                _free.Add(child);
            }
        }

        CollectLeaves(layout);
        foreach (var leaf in _leaves)
        {
            if (leaf is not Tile { Name: { Length: > 0 } name } tile)
            {
                continue;
            }

            foreach (var child in Children)
            {
                if (child is not IAdamantiumComponent named || GetTile(named) != name)
                {
                    continue;
                }

                _tileChildren[tile] = IsLifted(child) ? null : child;
                _free.Remove(child);
                break;
            }
        }

        OpenGap();
        _sequence.Clear();
        _sequence.AddRange(_free);

        foreach (var leaf in _leaves)
        {
            if (leaf is Tile tile && !_tileChildren.ContainsKey(tile) && _free.Count > 0)
            {
                _tileChildren[tile] = _free[0];
                _free.RemoveAt(0);
            }
        }

        foreach (var leaf in _leaves)
        {
            if (leaf is TileFlow flow)
            {
                var count = (int)Math.Min(_free.Count, Capacity(flow));
                _flowRanges[flow] = (_flowChildren.Count, count);
                for (var i = 0; i < count; i++)
                {
                    _flowChildren.Add(_free[i]);
                }

                _free.RemoveRange(0, count);
            }
        }
    }

    private void CollectLeaves(TileRegion region)
    {
        if (region is TileSplit split)
        {
            foreach (var part in split.Children)
            {
                CollectLeaves(part);
            }
        }
        else if (region != null)
        {
            _leaves.Add(region);
        }
    }

    private static long Capacity(TileFlow flow) =>
        flow.MaxColumns > 0 && flow.MaxRows > 0 ? (long)flow.MaxColumns * flow.MaxRows : long.MaxValue;

    private void LayRegion(TileRegion region, Rect room)
    {
        switch (region)
        {
            case Tile tile when _tileChildren.TryGetValue(tile, out var child) && child != null:
                _placed[child] = room;
                break;
            case TileFlow flow when _flowRanges.TryGetValue(flow, out var range):
                LayFlow(flow, range.Start, range.Count, room);
                break;
            case TileSplit split:
                LaySplit(split, room);
                break;
        }
    }

    private void LaySplit(TileSplit split, Rect room)
    {
        var count = split.Children.Count;
        if (count == 0)
        {
            return;
        }

        var gap = Spacing;
        var vertical = split.Orientation == Orientation.Horizontal;
        var start = vertical ? room.X : room.Y;
        var length = Math.Max(0, (vertical ? room.Width : room.Height) - (count - 1) * gap);
        var at = start;
        for (var i = 0; i < count; i++)
        {
            var part = length * split.FractionOf(i);
            LayRegion(split.Children[i], vertical
                ? new Rect(at, room.Y, part, room.Height)
                : new Rect(room.X, at, room.Width, part));
            at += part;
            if (i < count - 1)
            {
                var bar = vertical ? new Rect(at, room.Y, gap, room.Height) : new Rect(room.X, at, room.Width, gap);
                _grips.Add(new Grip(split, i, vertical, bar, start, length));
                at += gap;
            }
        }
    }

    private void LayFlow(TileFlow flow, int first, int count, Rect room)
    {
        if (count == 0)
        {
            return;
        }

        if (room.Width <= 0 || room.Height <= 0)
        {
            for (var i = 0; i < count; i++)
            {
                _placed[_flowChildren[first + i]] = new Rect(room.X, room.Y, 0, 0);
            }

            return;
        }

        var gap = Spacing;
        var aspect = flow.AspectRatio > 0 ? flow.AspectRatio : 1;
        var maxColumns = flow.MaxColumns > 0 ? Math.Min(flow.MaxColumns, count) : count;
        var maxRows = flow.MaxRows > 0 ? flow.MaxRows : count;
        var columns = maxColumns;
        var best = -1.0;
        for (var c = 1; c <= maxColumns; c++)
        {
            var r = (count + c - 1) / c;
            if (r > maxRows)
            {
                continue;
            }

            var cellWidth = (room.Width - (c - 1) * gap) / c;
            var cellHeight = (room.Height - (r - 1) * gap) / r;
            var width = Math.Max(0, Math.Min(cellWidth, cellHeight * aspect));
            if (width * width / aspect > best + 1e-9)
            {
                best = width * width / aspect;
                columns = c;
            }
        }

        var rows = (count + columns - 1) / columns;
        var tileWidth = Math.Max(0, (room.Width - (columns - 1) * gap) / columns);
        var tileHeight = Math.Max(0, (room.Height - (rows - 1) * gap) / rows);
        if (flow.AspectRatio > 0)
        {
            tileWidth = Math.Min(tileWidth, tileHeight * aspect);
            tileHeight = tileWidth / aspect;
        }

        var blockWidth = columns * tileWidth + (columns - 1) * gap;
        var blockHeight = rows * tileHeight + (rows - 1) * gap;
        var left = room.X + (room.Width - blockWidth) / 2;
        var top = room.Y + (room.Height - blockHeight) / 2;
        for (var row = 0; row < rows; row++)
        {
            var inRow = Math.Min(columns, count - row * columns);
            var width = tileWidth;
            var x = left;
            if (inRow < columns)
            {
                switch (flow.LastRowAlignment)
                {
                    case HorizontalAlignment.Center:
                        x += (columns - inRow) * (tileWidth + gap) / 2;
                        break;
                    case HorizontalAlignment.Right:
                        x += (columns - inRow) * (tileWidth + gap);
                        break;
                    case HorizontalAlignment.Stretch:
                        width = (blockWidth - (inRow - 1) * gap) / inRow;
                        break;
                }
            }

            var y = top + row * (tileHeight + gap);
            for (var column = 0; column < inRow; column++)
            {
                _placed[_flowChildren[first + row * columns + column]] = new Rect(x + column * (width + gap), y, width, tileHeight);
            }
        }
    }

    private void Move(Grip grip, double at)
    {
        var split = grip.Split;
        var count = split.Children.Count;
        if (grip.Index + 1 >= count || grip.Room <= 0)
        {
            return;
        }

        var before = 0.0;
        for (var i = 0; i < grip.Index; i++)
        {
            before += grip.Room * split.FractionOf(i);
        }

        var gap = Spacing;
        var pair = grip.Room * (split.FractionOf(grip.Index) + split.FractionOf(grip.Index + 1));
        if (pair <= 2 * MinTileSize)
        {
            return;
        }

        var partStart = grip.Start + before + grip.Index * gap;
        var first = Math.Clamp(at - gap / 2 - partStart, MinTileSize, pair - MinTileSize);
        var shares = new TrackingCollection<double>();
        for (var i = 0; i < count; i++)
        {
            var part = i == grip.Index ? first
                : i == grip.Index + 1 ? pair - first
                : grip.Room * split.FractionOf(i);
            shares.Add(part / grip.Room * 100);
        }

        split.SetCurrentValue(TileSplit.SharesProperty, shares);
    }

    private (bool Vertical, bool Horizontal) GripKindsAt(Vector2 point)
    {
        var vertical = false;
        var horizontal = false;
        foreach (var grip in _grips)
        {
            if (Near(grip, point))
            {
                vertical |= grip.Vertical;
                horizontal |= !grip.Vertical;
            }
        }

        return (vertical, horizontal);
    }

    private void GripsAt(Vector2 point, List<Grip> grips)
    {
        grips.Clear();
        AddInLine(point, true, grips);
        AddInLine(point, false, grips);
    }

    private void AddInLine(Vector2 point, bool vertical, List<Grip> grips)
    {
        var from = grips.Count;
        foreach (var grip in _grips)
        {
            if (grip.Vertical == vertical && Near(grip, point))
            {
                grips.Add(grip);
                break;
            }
        }

        if (grips.Count == from)
        {
            return;
        }

        var line = Line(grips[from]);
        var added = true;
        while (added)
        {
            added = false;
            foreach (var grip in _grips)
            {
                if (grip.Vertical != vertical || Math.Abs(Line(grip) - line) > 0.5 || grips.Contains(grip))
                {
                    continue;
                }

                for (var i = from; i < grips.Count; i++)
                {
                    if (Touch(grip, grips[i]))
                    {
                        grips.Add(grip);
                        added = true;
                        break;
                    }
                }
            }
        }
    }

    private static double Line(Grip grip) => grip.Vertical ? grip.Bar.X + grip.Bar.Width / 2 : grip.Bar.Y + grip.Bar.Height / 2;

    private bool Touch(Grip a, Grip b)
    {
        var reach = Spacing + GripSlack;
        return a.Vertical
            ? a.Bar.Y <= b.Bar.Bottom + reach && b.Bar.Y <= a.Bar.Bottom + reach
            : a.Bar.X <= b.Bar.Right + reach && b.Bar.X <= a.Bar.Right + reach;
    }

    private static bool Near(Grip grip, Vector2 point)
    {
        var bar = grip.Bar;
        return point.X >= bar.X - GripSlack && point.X <= bar.Right + GripSlack
            && point.Y >= bar.Y - GripSlack && point.Y <= bar.Bottom + GripSlack;
    }

    private IMeasurableComponent Maximized()
    {
        var item = MaximizedItem;
        if (item == null)
        {
            return null;
        }

        foreach (var child in Children)
        {
            if (ReferenceEquals(child, item) || (child is IFundamentalUIComponent { DataContext: { } data } && Equals(data, item)))
            {
                return child;
            }
        }

        return null;
    }
}
