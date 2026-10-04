using Adamantium.UI.Core;
using Adamantium.UI.Controls.Base;
using Adamantium.UI.Core.Input;

namespace Adamantium.UI.Controls.Panels;

/// <summary>Cells of one size in rows and columns. Hosting items, an axis whose count is set shares the space between
/// its cells and the other axis is as big as the biggest item realized so far; a grid that grows downwards realizes only
/// the rows in view.</summary>
public class UniformGrid : VirtualizingPanel
{
   public static readonly AdamantiumProperty RowsProperty = AdamantiumProperty.Register(nameof(Rows), typeof (Int32),
      typeof (UniformGrid),
      new PropertyMetadata(0, PropertyMetadataOptions.AffectsMeasure | PropertyMetadataOptions.AffectsArrange));

   public static readonly AdamantiumProperty ColumnsProperty = AdamantiumProperty.Register(nameof(Columns), typeof(Int32), typeof(UniformGrid),
      new PropertyMetadata(0, PropertyMetadataOptions.AffectsMeasure | PropertyMetadataOptions.AffectsArrange));

   // The gap between cells - set ONCE on the grid instead of a Margin on every child (same names as Grid).
   public static readonly AdamantiumProperty RowSpacingProperty = AdamantiumProperty.Register(nameof(RowSpacing), typeof(Double), typeof(UniformGrid),
      new PropertyMetadata(0d, PropertyMetadataOptions.AffectsMeasure | PropertyMetadataOptions.AffectsArrange));

   public static readonly AdamantiumProperty ColumnSpacingProperty = AdamantiumProperty.Register(nameof(ColumnSpacing), typeof(Double), typeof(UniformGrid),
      new PropertyMetadata(0d, PropertyMetadataOptions.AffectsMeasure | PropertyMetadataOptions.AffectsArrange));

   private const int BufferRows = 2;
   private const int MaxCellPasses = 4;
   private const double DefaultViewportHeight = 1080.0;

   private int _columns;
   private int _rows;
   private bool _sharesWidth;
   private bool _sharesHeight;
   private double _cellWidth;
   private double _cellHeight;
   private double _naturalWidth;
   private double _naturalHeight;
   private int _lastItemCount = -1;
   private Size _lastConstraint;
   private int _lastFirstRow;
   private double _lastViewportHeight;
   private Vector2 _lastMeasuredOffset = new(double.NaN, double.NaN);
   private bool _cellGrew;
   private System.Action<IUIComponent> _onSlotBound;

   public Int32 Rows
   {
      get => GetValue<Int32>(RowsProperty);
      set => SetValue(RowsProperty, value);
   }

   public Int32 Columns
   {
      get => GetValue<Int32>(ColumnsProperty);
      set => SetValue(ColumnsProperty, value);
   }

   /// <summary>The vertical gap between cell rows.</summary>
   public Double RowSpacing
   {
      get => GetValue<Double>(RowSpacingProperty);
      set => SetValue(RowSpacingProperty, value);
   }

   /// <summary>The horizontal gap between cell columns.</summary>
   public Double ColumnSpacing
   {
      get => GetValue<Double>(ColumnSpacingProperty);
      set => SetValue(ColumnSpacingProperty, value);
   }

   /// <summary>The columns this grid is actually laid out in - the authored <see cref="Columns"/>, or the count worked
   /// out from the children when it is left at zero. The authored value alone is not the grid, so this is what a caller
   /// that has to reason about cell positions must ask. Zero until the first arrange.</summary>
   public Int32 EffectiveColumns { get; private set; }

   /// <summary>The rows this grid is actually laid out in - see <see cref="EffectiveColumns"/>.</summary>
   public Int32 EffectiveRows { get; private set; }

   /// <summary>One cell as of the last arrange, gaps excluded. This is the grid's whole point stated as a number: the
   /// counts are authored and the cell is DERIVED from the space, so anything that takes space away (a border, padding)
   /// costs every cell a fraction of a pixel instead of costing the line a whole column.</summary>
   public Size CellSize { get; private set; }

   /// <summary>Along an axis whose count is not set every cell is as big as the biggest item, so there an item's
   /// re-measure has to reach the grid. A virtualizing grid that shares out both axes is a boundary like any other.</summary>
   public override bool IsMeasureBoundary =>
      !Double.IsNaN(Width) && !Double.IsNaN(Height) || IsItemsHost && IsVirtualizing && _sharesWidth && _sharesHeight;

   public UniformGrid() { }

   private bool VirtualizesRows => IsVirtualizing && (Columns > 0 || Rows <= 0);

   private double RowPitch => _cellHeight + RowSpacing;

   protected override Size MeasurePlain(Size availableSize)
   {
      GetDimensions(Children.Count, out var rows, out var columns);
      if (rows == 0 || columns == 0) return new Size();

      // The spacing eats into the space available for cells, so each cell is (available - total gaps) / count.
      var cell = new Size(
         (availableSize.Width - (columns - 1) * ColumnSpacing) / columns,
         (availableSize.Height - (rows - 1) * RowSpacing) / rows);
      double maxWidth = 0, maxHeight = 0;
      foreach (var child in Children)
      {
         child.Measure(cell);
         if (child.DesiredSize.Width > maxWidth) maxWidth = child.DesiredSize.Width;
         if (child.DesiredSize.Height > maxHeight) maxHeight = child.DesiredSize.Height;
      }
      // Every cell is the largest child's size; the grid is that times the count, plus the gaps between them.
      return new Size(maxWidth * columns + (columns - 1) * ColumnSpacing,
                      maxHeight * rows + (rows - 1) * RowSpacing);
   }

   protected override Size ArrangePlain(Size finalSize)
   {
      GetDimensions(Children.Count, out var rows, out var columns);
      if (rows == 0 || columns == 0) return finalSize;

      var cellWidth = (finalSize.Width - (columns - 1) * ColumnSpacing) / columns;
      var cellHeight = (finalSize.Height - (rows - 1) * RowSpacing) / rows;
      EffectiveColumns = columns;
      EffectiveRows = rows;
      CellSize = new Size(cellWidth, cellHeight);
      var index = 0;
      foreach (var child in Children)
      {
         var column = index % columns;
         var row = index / columns;
         child.Arrange(new Rect(column * (cellWidth + ColumnSpacing), row * (cellHeight + RowSpacing), cellWidth, cellHeight));
         index++;
      }
      return finalSize;
   }

   protected override Size MeasureVirtualized(Size availableSize, Vector2 offset)
   {
      var generator = Owner.ItemContainerGenerator;
      var count = Owner.Items.Count;
      if (count == 0)
      {
         foreach (var parked in generator.SetWindow(0, -1)) ParkContainer(parked);
         _rows = _columns = 0;
         _lastItemCount = 0;
         return new Size();
      }

      GetDimensions(count, out _rows, out _columns);
      _sharesWidth = !double.IsInfinity(availableSize.Width) && (Columns > 0 || Rows <= 0);
      _sharesHeight = !double.IsInfinity(availableSize.Height) && Rows > 0;
      var sharedWidth = Math.Max(0, (availableSize.Width - (_columns - 1) * ColumnSpacing) / _columns);
      var sharedHeight = Math.Max(0, (availableSize.Height - (_rows - 1) * RowSpacing) / _rows);

      var constraint = new Size(_sharesWidth ? sharedWidth : double.PositiveInfinity,
                                _sharesHeight ? sharedHeight : double.PositiveInfinity);
      if (count != _lastItemCount || constraint != _lastConstraint)
      {
         _naturalWidth = _naturalHeight = 0;
         _lastItemCount = count;
         _lastConstraint = constraint;
      }

      _cellWidth = _sharesWidth ? sharedWidth : _naturalWidth;
      _cellHeight = _sharesHeight ? sharedHeight : _naturalHeight;

      if (!VirtualizesRows)
      {
         foreach (var parked in generator.SetWindow(0, count - 1)) ParkContainer(parked);
         for (var i = 0; i < count; i++)
         {
            var cell = (IMeasurableComponent)RealizeInWindow(i);
            cell.Measure(constraint);
            GrowCell(cell.DesiredSize);
         }

         return GridExtent();
      }

      double viewportHeight;
      if (double.IsInfinity(availableSize.Height))
      {
         OnNoViewport();
         viewportHeight = _lastViewportHeight > 0 ? _lastViewportHeight : DefaultViewportHeight;
      }
      else
      {
         viewportHeight = availableSize.Height;
         _lastViewportHeight = viewportHeight;
      }

      if (!_sharesHeight && _naturalHeight <= 0)
      {
         var probe = (IMeasurableComponent)RealizeInWindow(Math.Clamp(_lastFirstRow * _columns, 0, count - 1));
         probe.Measure(constraint);
         GrowCell(probe.DesiredSize);
      }

      var scrolling = offset != _lastMeasuredOffset;
      _lastMeasuredOffset = offset;
      var budget = BudgetOrUnlimited(scrolling ? ScrollBindBudget : FillBindBudget);
      _onSlotBound ??= OnSlotBound;

      for (var pass = 0; pass < MaxCellPasses; pass++)
      {
         var pitch = Math.Max(1, RowPitch);
         var firstRow = Math.Max(0, (int)Math.Floor(offset.Y / pitch) - BufferRows);
         var lastRow = Math.Min(_rows - 1, firstRow + (int)Math.Ceiling(viewportHeight / pitch) + 1 + 2 * BufferRows);
         _lastFirstRow = firstRow;

         _cellGrew = false;
         var last = Math.Min(count - 1, (lastRow + 1) * _columns - 1);
         foreach (var parked in generator.SetWindow(firstRow * _columns, last, budget, MinBindsPerPass, _onSlotBound))
         {
            ParkContainer(parked);
         }

         foreach (var index in generator.RealizedIndices)
         {
            if (generator.ContainerFromIndex(index) is not IMeasurableComponent cell) continue;

            cell.Measure(constraint);
            _cellGrew |= GrowCell(cell.DesiredSize);
         }

         if (!_cellGrew) break;
      }

      if (generator.PendingIndices.Count > 0) LayoutManager.For(this).InvalidateMeasureNextPass(this);

      return GridExtent();
   }

   protected override void ArrangeVirtualized(Size finalSize, Vector2 offset)
   {
      EffectiveColumns = _columns;
      EffectiveRows = _rows;
      CellSize = new Size(_cellWidth, _cellHeight);

      var generator = Owner.ItemContainerGenerator;
      foreach (var index in generator.RealizedIndices)
      {
         if (generator.ContainerFromIndex(index) is IMeasurableComponent cell) cell.Arrange(SlotRect(index));
      }

      ReconcileSkeletons(SlotRect);
   }

   /// <summary>Every cell comes from its index, so where an item sits is arithmetic - and stays answerable for an item
   /// virtualized away, which is when somebody needs to scroll to it.</summary>
   public override bool TryGetItemRect(int index, out Rect rect)
   {
      rect = default;
      if (!IsItemsHost || _columns <= 0 || index < 0 || index >= Owner.Items.Count) return false;

      rect = SlotRect(index);
      return true;
   }

   protected override bool RealizedWindowMovesFor(Vector2 from, Vector2 to)
   {
      if (!VirtualizesRows) return false;

      var pitch = Math.Max(1, RowPitch);
      return (int)Math.Floor(from.Y / pitch) != (int)Math.Floor(to.Y / pitch);
   }

   /// <summary>Every cell here comes from the child's INDEX, so navigation is the same arithmetic the arrange uses:
   /// sideways is index ±1 within the row, up and down are ±one row. A sideways step must not fall off the end of a
   /// line into the next one, which plain index arithmetic would happily do. Hosting items, a neighbor not realized is
   /// no answer.</summary>
   public override IUIComponent Navigate(IUIComponent from, FocusNavigationDirection direction)
   {
      if (!IsArrow(direction)) return base.Navigate(from, direction);

      int index, count, rows, columns;
      if (IsItemsHost)
      {
         index = from == null ? -1 : Owner.ItemContainerGenerator.IndexFromContainer(from);
         count = Owner.Items.Count;
         rows = _rows;
         columns = _columns;
      }
      else
      {
         index = from is MeasurableUIComponent child ? Children.IndexOf(child) : -1;
         count = Children.Count;
         GetDimensions(count, out rows, out columns);
      }

      if (index < 0 || rows == 0 || columns == 0) return null;

      var row = index / columns + (IsVertical(direction) ? (IsForward(direction) ? 1 : -1) : 0);
      var column = index % columns + (IsVertical(direction) ? 0 : IsForward(direction) ? 1 : -1);
      if (row < 0 || row >= rows || column < 0 || column >= columns) return null;

      var next = row * columns + column;
      if (next < 0 || next >= count) return null;

      return IsItemsHost ? Owner.ItemContainerGenerator.ContainerFromIndex(next) : Children[next];
   }

   private Size GridExtent() =>
      new(_columns * _cellWidth + (_columns - 1) * ColumnSpacing, _rows * _cellHeight + (_rows - 1) * RowSpacing);

   private Rect SlotRect(int index) =>
      new(index % _columns * (_cellWidth + ColumnSpacing), index / _columns * RowPitch, _cellWidth, _cellHeight);

   private void OnSlotBound(IUIComponent container)
   {
      if (container.VisualParent != this)
      {
         AddVisualChild(container);
         AddLogicalChild(container);
      }

      var cell = (IMeasurableComponent)container;
      if (!cell.IsMeasureValid) cell.Measure(_lastConstraint);
      _cellGrew |= GrowCell(cell.DesiredSize);
   }

   private bool GrowCell(Size desired)
   {
      var grew = false;
      if (!_sharesWidth && desired.Width > _naturalWidth)
      {
         _naturalWidth = desired.Width;
         _cellWidth = _naturalWidth;
         grew = true;
      }

      if (!_sharesHeight && desired.Height > _naturalHeight)
      {
         _naturalHeight = desired.Height;
         _cellHeight = _naturalHeight;
         grew = true;
      }

      return grew;
   }

   // Rows/Columns are filled in from the child count: set Columns and the rows follow (and vice versa); set neither and
   // the grid is as square as the count allows.
   private void GetDimensions(int count, out int rows, out int columns)
   {
      rows = Rows;
      columns = Columns;
      if (count == 0) { rows = 0; columns = 0; return; }
      if (columns > 0 && rows > 0) return;
      if (columns > 0) { rows = (count + columns - 1) / columns; return; }
      if (rows > 0) { columns = (count + rows - 1) / rows; return; }
      columns = (int)System.Math.Ceiling(System.Math.Sqrt(count));
      rows = (count + columns - 1) / columns;
   }
}
