using Adamantium.UI.Controls.Base;
using Adamantium.UI.Controls.Panels;
using Adamantium.UI.Controls.Primitives;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Input;
using Adamantium.UI.Core.RoutedEvents;
using Adamantium.UI.Core.Templates;

namespace Adamantium.UI.Controls.Canvases;

// THE INSPECTOR'S PANEL, beside the plane: whether it is open, how wide, and what it looks like. The template gives it a
// column (PART_Inspector in a Grid beside PART_Viewport) and a splitter (PART_InspectorSplitter).
public partial class InfiniteCanvas
{
    /// <summary>The narrowest the panel is pulled to.</summary>
    public static readonly AdamantiumProperty MinInspectorWidthProperty = AdamantiumProperty.Register(
        nameof(MinInspectorWidth), typeof(Double), typeof(InfiniteCanvas), new PropertyMetadata(200.0, OnInspectorPlaced));

    public static readonly AdamantiumProperty InspectorWidthProperty = AdamantiumProperty.Register(
        nameof(InspectorWidth), typeof(Double), typeof(InfiniteCanvas),
        new PropertyMetadata(340.0, PropertyMetadataOptions.BindsTwoWayByDefault, OnInspectorPlaced));

    public static readonly AdamantiumProperty IsInspectorOpenProperty = AdamantiumProperty.Register(
        nameof(IsInspectorOpen), typeof(Boolean), typeof(InfiniteCanvas),
        new PropertyMetadata(true, PropertyMetadataOptions.BindsTwoWayByDefault, OnInspectorPlaced));

    public static readonly AdamantiumProperty HidesFoldedInspectorProperty = AdamantiumProperty.Register(
        nameof(HidesFoldedInspector), typeof(Boolean), typeof(InfiniteCanvas), new PropertyMetadata(false));

    public static readonly AdamantiumProperty ShowsDrawingActionsProperty = AdamantiumProperty.Register(
        nameof(ShowsDrawingActions), typeof(Boolean), typeof(InfiniteCanvas), new PropertyMetadata(true));

    public static readonly AdamantiumProperty InspectorPanelTemplateProperty = AdamantiumProperty.Register(
        nameof(InspectorPanelTemplate), typeof(DataTemplate), typeof(InfiniteCanvas),
        new PropertyMetadata(null, PropertyMetadataOptions.AffectsMeasure));

    private static readonly AdamantiumPropertyKey IsInspectorUnfoldedPropertyKey = AdamantiumProperty.RegisterReadOnly(
        nameof(IsInspectorUnfolded), typeof(Boolean), typeof(InfiniteCanvas), new PropertyMetadata(true));

    public static readonly AdamantiumProperty IsInspectorUnfoldedProperty = IsInspectorUnfoldedPropertyKey.Property;

    private static readonly AdamantiumPropertyKey InspectorPanelDataPropertyKey = AdamantiumProperty.RegisterReadOnly(
        nameof(InspectorPanelData), typeof(Object), typeof(InfiniteCanvas), new PropertyMetadata(null));

    public static readonly AdamantiumProperty InspectorPanelDataProperty = InspectorPanelDataPropertyKey.Property;

    // What the plane is always left, however wide the panel is asked to be.
    private const double LeastPlaneWidth = 120;

    private ColumnDefinition _inspectorColumn;
    private GridSplitter _inspectorSplitter;
    private CanvasCommand _foldInspector;
    private bool _placingInspector;
    private InputUIComponent _inspectorPart;

    public Double MinInspectorWidth
    {
        get => GetValue<Double>(MinInspectorWidthProperty);
        set => SetValue(MinInspectorWidthProperty, value);
    }

    /// <summary>How wide the open panel is; pulled by its splitter.</summary>
    public Double InspectorWidth
    {
        get => GetValue<Double>(InspectorWidthProperty);
        set => SetValue(InspectorWidthProperty, value);
    }

    /// <summary>Whether the panel is open or folded to a strip; whether there is one at all is
    /// <see cref="ShowsInspector"/>.</summary>
    public Boolean IsInspectorOpen
    {
        get => GetValue<Boolean>(IsInspectorOpenProperty);
        set => SetValue(IsInspectorOpenProperty, value);
    }

    /// <summary>Whether folding the panel hides it whole (<see cref="ShowsInspector"/> off) instead of leaving a strip -
    /// for a control with a button of its own that shows it. Off by default.</summary>
    public Boolean HidesFoldedInspector
    {
        get => GetValue<Boolean>(HidesFoldedInspectorProperty);
        set => SetValue(HidesFoldedInspectorProperty, value);
    }

    /// <summary>Whether the inspector offers a drawing's own actions (<see cref="CanvasInspector.ShowsDrawingActions"/>); on
    /// by default.</summary>
    public Boolean ShowsDrawingActions
    {
        get => GetValue<Boolean>(ShowsDrawingActionsProperty);
        set => SetValue(ShowsDrawingActionsProperty, value);
    }

    /// <summary>The whole panel beside the plane - open and folded, whichever <see cref="IsInspectorOpen"/> says. Its data
    /// is the canvas's own; the canvas is reached by <c>{Ancestor InfiniteCanvas, ...}</c>, and a
    /// <see cref="CanvasInspector"/> in it finds it by itself. The theme's by default.</summary>
    public DataTemplate InspectorPanelTemplate
    {
        get => GetValue<DataTemplate>(InspectorPanelTemplateProperty);
        set => SetValue(InspectorPanelTemplateProperty, value);
    }

    /// <summary>The data of the panel: the canvas's <c>DataContext</c> - but for a control, which a presenter would show in
    /// the panel's place.</summary>
    public Object InspectorPanelData => GetValue(InspectorPanelDataProperty);

    private void OnPanelDataChanged(object sender, AdamantiumPropertyChangedEventArgs e) =>
        SetValue(InspectorPanelDataPropertyKey, DataContext is IUIComponent ? null : DataContext);

    /// <summary>Whether the panel stands open beside the plane now: shown, and not folded.</summary>
    public Boolean IsInspectorUnfolded => GetValue<Boolean>(IsInspectorUnfoldedProperty);

    /// <summary>Folds the panel, or opens it again.</summary>
    public CanvasCommand FoldInspectorCommand => _foldInspector ??= new CanvasCommand(_ => FoldInspector());

    private void FoldInspector()
    {
        if (HidesFoldedInspector)
        {
            var open = !IsInspectorUnfolded;
            if (open)
            {
                SetCurrentValue(IsInspectorOpenProperty, true);
            }

            SetCurrentValue(ShowsInspectorProperty, open);
        }
        else
        {
            SetCurrentValue(IsInspectorOpenProperty, !IsInspectorOpen);
        }

        RememberLayout();
    }

    // A search opens the panel it is typed into, and is asked for once that has been laid out; one hidden whole stays
    // hidden. False when the panel is open already, or has nothing to search with.
    private bool OpenInspectorToSearch()
    {
        if (!ShowsInspector || IsInspectorOpen || !Holds<CanvasInspector>(_inspectorPart))
        {
            return false;
        }

        SetCurrentValue(IsInspectorOpenProperty, true);
        RememberLayout();
        _searchWhenLaidOut = true;
        InvalidateArrange();
        return true;
    }

    private static bool Holds<T>(IUIComponent at)
    {
        if (at == null || at is T)
        {
            return at != null;
        }

        foreach (var child in at.VisualChildren)
        {
            if (Holds<T>(child))
            {
                return true;
            }
        }

        return false;
    }

    private void SearchOnceLaidOut()
    {
        if (!_searchWhenLaidOut)
        {
            return;
        }

        _searchWhenLaidOut = false;
        LoopSignal.Post(() => SearchAsked?.Invoke(this, EventArgs.Empty));
    }

    private bool _searchWhenLaidOut;

    private static void OnInspectorPlaced(AdamantiumComponent component, AdamantiumPropertyChangedEventArgs e)
    {
        (component as InfiniteCanvas)?.PlaceInspector();
    }

    private void TakeInspector()
    {
        _inspectorPart = GetTemplateChild("PART_Inspector") as InputUIComponent;
        if (_inspectorPart is { VisualParent: Grid grid } && Grid.GetColumn(_inspectorPart) < grid.ColumnDefinitions.Count)
        {
            _inspectorColumn = grid.ColumnDefinitions[Grid.GetColumn(_inspectorPart)];
            _inspectorColumn.PropertyChanged += OnInspectorColumnChanged;
        }

        _inspectorSplitter = GetTemplateChild("PART_InspectorSplitter") as GridSplitter;
        if (_inspectorSplitter != null)
        {
            _inspectorSplitter.DragCompleted += OnInspectorPulled;
        }

        foreach (var part in new[] { _inspectorPart, _inspectorSplitter })
        {
            if (part != null)
            {
                part.AddHandler(PointerEvents.PointerPressedEvent, new PointerPressedEventHandler(OnInspectorPressed), true);
                part.AddHandler(PointerEvents.PointerReleasedEvent, new PointerReleasedEventHandler(OnInspectorReleased), true);
                part.AddHandler(PointerEvents.PointerEnteredEvent, new PointerEventHandler(OnInspectorEntered), true);
                Gestures.SetIsManipulationEnabled(part, true);
            }
        }

        PlaceInspector();
    }

    private void LeaveInspector()
    {
        if (_inspectorColumn != null)
        {
            _inspectorColumn.PropertyChanged -= OnInspectorColumnChanged;
        }

        if (_inspectorSplitter != null)
        {
            _inspectorSplitter.DragCompleted -= OnInspectorPulled;
        }

        foreach (var part in new[] { _inspectorPart, _inspectorSplitter })
        {
            if (part != null)
            {
                part.RemoveHandler(PointerEvents.PointerPressedEvent, new PointerPressedEventHandler(OnInspectorPressed));
                part.RemoveHandler(PointerEvents.PointerReleasedEvent, new PointerReleasedEventHandler(OnInspectorReleased));
                part.RemoveHandler(PointerEvents.PointerEnteredEvent, new PointerEventHandler(OnInspectorEntered));
            }
        }

        _inspectorColumn = null;
        _inspectorSplitter = null;
        _inspectorPart = null;
    }

    // What is done on the panel is not done to the plane under its own gestures.
    private static void OnInspectorPressed(object sender, PointerPressedEventArgs e) => e.Handled = true;

    private static void OnInspectorReleased(object sender, PointerReleasedEventArgs e) => e.Handled = true;

    // The tool's mark does not stay behind on the plane; the splitter's own moves are not heard by it.
    private void OnInspectorEntered(object sender, PointerEventArgs e)
    {
        var had = _snap != null || _pointerInside;
        _snap = null;
        _pointerInside = false;
        if (had)
        {
            RepaintGlass();
        }
    }

    private bool IsInspector(IUIComponent at) =>
        at != null && (ReferenceEquals(at, _inspectorPart) || ReferenceEquals(at, _inspectorSplitter));

    private void OnInspectorPulled(object sender, DragCompletedEventArgs e) => RememberLayout();

    private void OnInspectorColumnChanged(object sender, AdamantiumPropertyChangedEventArgs e)
    {
        if (e.Property == ColumnDefinition.WidthProperty && !_placingInspector && IsInspectorUnfolded &&
            _inspectorColumn.Width is { GridUnitType: GridUnitType.Pixel } width)
        {
            SetCurrentValue(InspectorWidthProperty, width.Value);
            if (_inspectorSplitter is not { IsDragging: true })
            {
                RememberLayout();
            }
        }
    }

    // The column is the panel's width while it is open, and what the strip takes while it is not.
    private void PlaceInspector()
    {
        var unfolded = ShowsInspector && IsInspectorOpen;
        SetValue(IsInspectorUnfoldedPropertyKey, unfolded);
        if (_inspectorColumn == null)
        {
            return;
        }

        _placingInspector = true;
        try
        {
            var least = LeastInspectorWidth;
            var wanted = Double.IsFinite(InspectorWidth) ? Math.Max(least, InspectorWidth) : least;
            _inspectorColumn.Width = unfolded ? new GridLength(wanted) : new GridLength(1, GridUnitType.Auto);
            LimitInspector(RenderSize.Width);
        }
        finally
        {
            _placingInspector = false;
        }
    }

    // A panel never leaves the plane without a strip to look at; narrower again, the canvas gives it its width back.
    private void LimitInspector(double room)
    {
        if (_inspectorColumn == null)
        {
            return;
        }

        // What else the template stands beside the plane - a panel on the other side - took when last laid out.
        var taken = 0.0;
        if (_inspectorPart?.VisualParent is Grid grid)
        {
            foreach (var column in grid.ColumnDefinitions)
            {
                if (!ReferenceEquals(column, _inspectorColumn) && !column.Width.IsStar)
                {
                    taken += column.ActualWidth;
                }
            }
        }

        var most = room > 0 ? Math.Max(0, room - taken - LeastPlaneWidth) : Double.PositiveInfinity;
        var least = IsInspectorUnfolded ? Math.Min(LeastInspectorWidth, most) : 0;
        if (_inspectorColumn.MaxWidth != most)
        {
            _inspectorColumn.MaxWidth = most;
        }

        if (_inspectorColumn.MinWidth != least)
        {
            _inspectorColumn.MinWidth = least;
        }
    }

    private double LeastInspectorWidth => Double.IsFinite(MinInspectorWidth) ? Math.Max(0, MinInspectorWidth) : 0;
}
