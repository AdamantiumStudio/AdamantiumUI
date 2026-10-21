using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using Adamantium.Core.Collections;
using Adamantium.Mathematics;
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
/// given; where that is unbounded it asks only for its largest child, so it does not scroll.
/// </summary>
public class TiledPanel : Panel
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
        foreach (var child in Children)
        {
            if (child.Visibility != Visibility.Collapsed && _placed.TryGetValue(child, out var tile))
            {
                child.Arrange(tile);
            }
        }

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

    private void OnChildrenChanged(object sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems == null)
        {
            return;
        }

        foreach (var child in e.OldItems)
        {
            if (child is IAdamantiumComponent component && !Children.Contains(child as IMeasurableComponent))
            {
                component.ClearValue(VisibilityProperty);
            }
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
            _free.Add(child);
        }

        CollectLeaves(layout);
        foreach (var leaf in _leaves)
        {
            if (leaf is not Tile { Name: { Length: > 0 } name } tile)
            {
                continue;
            }

            for (var i = 0; i < _free.Count; i++)
            {
                if (_free[i] is IAdamantiumComponent child && GetTile(child) == name)
                {
                    _tileChildren[tile] = _free[i];
                    _free.RemoveAt(i);
                    break;
                }
            }
        }

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
            case Tile tile when _tileChildren.TryGetValue(tile, out var child):
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
