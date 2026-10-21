using System;
using Adamantium.Mathematics;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Input;

namespace Adamantium.UI.Controls.Panels;

/// <summary>
/// Lays its children out as tiles in the scheme <see cref="Layout"/> names - one, two side by side or stacked, a large one
/// with two or three beside it, four in a grid - in the order they come; a child the scheme has no tile for is collapsed.
/// <see cref="MaximizedItem"/> fills the panel alone. The gaps between tiles (<see cref="SplitterThickness"/>) can be
/// dragged, moving <see cref="ColumnSplit"/> and <see cref="RowSplit"/>; a double click on one shares the room evenly
/// again across it, <see cref="ShareEvenly"/> across both. The panel owns its children's visibility.
/// </summary>
public class TiledPanel : Panel
{
    private const double MinTileSize = 32;
    private const double GripSlack = 3;

    private readonly Rect[] _tiles = new Rect[4];
    private int _tileCount;
    private bool _draggingColumn;
    private bool _draggingRow;

    public static readonly AdamantiumProperty LayoutProperty = AdamantiumProperty.Register(nameof(Layout),
        typeof(TileLayout), typeof(TiledPanel), new PropertyMetadata(TileLayout.Four, PropertyMetadataOptions.AffectsMeasure));

    public static readonly AdamantiumProperty MaximizedItemProperty = AdamantiumProperty.Register(nameof(MaximizedItem),
        typeof(object), typeof(TiledPanel), new PropertyMetadata(null, PropertyMetadataOptions.AffectsMeasure));

    public static readonly AdamantiumProperty ColumnSplitProperty = AdamantiumProperty.Register(nameof(ColumnSplit),
        typeof(double), typeof(TiledPanel),
        new PropertyMetadata(0.5, PropertyMetadataOptions.AffectsMeasure | PropertyMetadataOptions.BindsTwoWayByDefault));

    public static readonly AdamantiumProperty RowSplitProperty = AdamantiumProperty.Register(nameof(RowSplit),
        typeof(double), typeof(TiledPanel),
        new PropertyMetadata(0.5, PropertyMetadataOptions.AffectsMeasure | PropertyMetadataOptions.BindsTwoWayByDefault));

    public static readonly AdamantiumProperty SplitterThicknessProperty = AdamantiumProperty.Register(
        nameof(SplitterThickness), typeof(double), typeof(TiledPanel),
        new PropertyMetadata(4.0, PropertyMetadataOptions.AffectsMeasure));

    /// <summary>The scheme the children are laid out in.</summary>
    public TileLayout Layout
    {
        get => GetValue<TileLayout>(LayoutProperty);
        set => SetValue(LayoutProperty, value);
    }

    /// <summary>The child that fills the panel alone, the others collapsed - the child itself or its data; null lays
    /// them all out as <see cref="Layout"/> says.</summary>
    public object MaximizedItem
    {
        get => GetValue(MaximizedItemProperty);
        set => SetValue(MaximizedItemProperty, value);
    }

    /// <summary>Where the gap between the left and the right tiles stands, as a share of the width, from 0 to 1.</summary>
    public double ColumnSplit
    {
        get => GetValue<double>(ColumnSplitProperty);
        set => SetValue(ColumnSplitProperty, value);
    }

    /// <summary>Where the gap between the top and the bottom tiles stands, as a share of the height, from 0 to 1.</summary>
    public double RowSplit
    {
        get => GetValue<double>(RowSplitProperty);
        set => SetValue(RowSplitProperty, value);
    }

    /// <summary>How wide the gaps between tiles are; they are what is dragged.</summary>
    public double SplitterThickness
    {
        get => GetValue<double>(SplitterThicknessProperty);
        set => SetValue(SplitterThicknessProperty, value);
    }

    /// <summary>Gives every tile an even share of the room again: both gaps back to the middle.</summary>
    public void ShareEvenly()
    {
        SetCurrentValue(ColumnSplitProperty, 0.5);
        SetCurrentValue(RowSplitProperty, 0.5);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var finite = !double.IsInfinity(availableSize.Width) && !double.IsInfinity(availableSize.Height);
        var maximized = Maximized();
        LayTiles(finite ? availableSize : default, maximized != null);

        var desired = default(Size);
        var tile = 0;
        foreach (var child in Children)
        {
            var shown = maximized != null ? ReferenceEquals(child, maximized) : tile < _tileCount;
            child.Visibility = shown ? Visibility.Visible : Visibility.Collapsed;
            if (!shown)
            {
                continue;
            }

            var room = finite ? _tiles[maximized != null ? 0 : tile].Size : availableSize;
            child.Measure(room);
            desired = new Size(Math.Max(desired.Width, child.DesiredSize.Width),
                Math.Max(desired.Height, child.DesiredSize.Height));
            tile++;
        }

        return finite ? availableSize : desired;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var maximized = Maximized();
        LayTiles(finalSize, maximized != null);

        var tile = 0;
        foreach (var child in Children)
        {
            if (child.Visibility == Visibility.Collapsed)
            {
                continue;
            }

            child.Arrange(_tiles[maximized != null ? 0 : Math.Min(tile, _tileCount - 1)]);
            tile++;
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

        var (column, row) = GripsAt(e.GetPosition(this));
        if (!column && !row)
        {
            return;
        }

        e.Handled = true;
        if (e.ClickCount >= 2)
        {
            if (column) SetCurrentValue(ColumnSplitProperty, 0.5);
            if (row) SetCurrentValue(RowSplitProperty, 0.5);
            return;
        }

        _draggingColumn = column;
        _draggingRow = row;
        e.Pointer.Capture(this);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var position = e.GetPosition(this);
        if (_draggingColumn || _draggingRow)
        {
            e.Handled = true;
            var gap = SplitterThickness;
            if (_draggingColumn) SetCurrentValue(ColumnSplitProperty, Share(position.X, ActualWidth, gap));
            if (_draggingRow) SetCurrentValue(RowSplitProperty, Share(position.Y, ActualHeight, gap));
            return;
        }

        var (column, row) = ReferenceEquals(e.OriginalSource, this) ? GripsAt(position) : (false, false);
        ShowGripCursor(column, row);
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        if (!_draggingColumn && !_draggingRow)
        {
            ShowGripCursor(false, false);
        }
    }

    protected override void OnPointerCaptureLost(PointerEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        if (!IsPointerCaptured)
        {
            _draggingColumn = false;
            _draggingRow = false;
        }
    }

    private void ShowGripCursor(bool column, bool row)
    {
        if (column || row)
        {
            Cursor = column && row ? Cursors.SizeAll : column ? Cursors.SizeEWE : Cursors.SizeNS;
        }
        else if (Cursor != null)
        {
            ClearValue(CursorProperty);
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (!_draggingColumn && !_draggingRow)
        {
            return;
        }

        e.Handled = true;
        _draggingColumn = false;
        _draggingRow = false;
        e.Pointer.Capture(null);
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

    private static double Share(double at, double length, double gap)
    {
        var room = length - gap;
        if (room <= 2 * MinTileSize)
        {
            return 0.5;
        }

        return Math.Clamp((at - gap / 2) / room, MinTileSize / room, 1 - MinTileSize / room);
    }

    private (bool Column, bool Row) GripsAt(Vector2 point)
    {
        if (Maximized() != null)
        {
            return (false, false);
        }

        var size = new Size(ActualWidth, ActualHeight);
        var gap = SplitterThickness;
        var (left, top) = Splits(size, gap);
        var nearColumn = Math.Abs(point.X - (left + gap / 2)) <= gap / 2 + GripSlack;
        var nearRow = Math.Abs(point.Y - (top + gap / 2)) <= gap / 2 + GripSlack;
        var inRightColumn = point.X >= left + gap;
        var inBottomRow = point.Y >= top + gap;
        return Layout switch
        {
            TileLayout.TwoColumns or TileLayout.FourLeft => (nearColumn, false),
            TileLayout.TwoRows => (false, nearRow),
            TileLayout.ThreeLeft => (nearColumn, nearRow && inRightColumn),
            TileLayout.ThreeTop => (nearColumn && inBottomRow, nearRow),
            TileLayout.Four => (nearColumn, nearRow),
            _ => (false, false)
        };
    }

    private (double Left, double Top) Splits(Size size, double gap) =>
        (Math.Max(0, (size.Width - gap) * Math.Clamp(ColumnSplit, 0, 1)),
            Math.Max(0, (size.Height - gap) * Math.Clamp(RowSplit, 0, 1)));

    private void LayTiles(Size size, bool maximized)
    {
        var width = size.Width;
        var height = size.Height;
        if (maximized || Layout == TileLayout.One)
        {
            _tiles[0] = new Rect(0, 0, width, height);
            _tileCount = 1;
            return;
        }

        var gap = SplitterThickness;
        var (left, top) = Splits(size, gap);
        var right = left + gap;
        var bottom = top + gap;
        var rightWidth = Math.Max(0, width - right);
        var bottomHeight = Math.Max(0, height - bottom);
        switch (Layout)
        {
            case TileLayout.TwoColumns:
                Set(new Rect(0, 0, left, height), new Rect(right, 0, rightWidth, height));
                break;
            case TileLayout.TwoRows:
                Set(new Rect(0, 0, width, top), new Rect(0, bottom, width, bottomHeight));
                break;
            case TileLayout.ThreeLeft:
                Set(new Rect(0, 0, left, height), new Rect(right, 0, rightWidth, top),
                    new Rect(right, bottom, rightWidth, bottomHeight));
                break;
            case TileLayout.ThreeTop:
                Set(new Rect(0, 0, width, top), new Rect(0, bottom, left, bottomHeight),
                    new Rect(right, bottom, rightWidth, bottomHeight));
                break;
            case TileLayout.FourLeft:
                var third = Math.Max(0, (height - 2 * gap) / 3);
                Set(new Rect(0, 0, left, height), new Rect(right, 0, rightWidth, third),
                    new Rect(right, third + gap, rightWidth, third), new Rect(right, 2 * (third + gap), rightWidth, third));
                break;
            default:
                Set(new Rect(0, 0, left, top), new Rect(right, 0, rightWidth, top),
                    new Rect(0, bottom, left, bottomHeight), new Rect(right, bottom, rightWidth, bottomHeight));
                break;
        }
    }

    private void Set(params ReadOnlySpan<Rect> tiles)
    {
        tiles.CopyTo(_tiles);
        _tileCount = tiles.Length;
    }
}
