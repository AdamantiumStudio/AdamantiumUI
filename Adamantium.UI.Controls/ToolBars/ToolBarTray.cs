using System.Collections.Generic;
using System.Linq;
using Adamantium.Mathematics;
using Adamantium.UI.Controls.Adorners;
using Adamantium.UI.Controls.Automation;
using Adamantium.UI.Controls.Panels;
using Adamantium.UI.Controls.Primitives;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Automation;
using Adamantium.UI.Core.Extensions;
using Adamantium.UI.Core.Input;
using Adamantium.UI.Core.Media;
using Adamantium.UI.Core.RoutedEvents;

namespace Adamantium.UI.Controls;

/// <summary>
/// Where an application's <see cref="ToolBar"/>s stand: in rows, each moved by its grip to another place of its row, to
/// another row, to a new row or to another tray of the window. A bar let go of anywhere else goes back where it was:
/// bars do not float. The tray's menu shows and hides its bars and locks them where they are. Bars are written in its
/// markup or made for the view models of its <see cref="ItemsControl.ItemsSource"/>, each by the template its
/// <see cref="ItemsControl.ItemTemplateSelector"/> picks - a template whose root is the bar.
/// </summary>
public class ToolBarTray : ItemsControl
{
    public static readonly AdamantiumProperty OrientationProperty = AdamantiumProperty.Register(nameof(Orientation),
        typeof(Orientation), typeof(ToolBarTray),
        new PropertyMetadata(Orientation.Horizontal, PropertyMetadataOptions.AffectsMeasure,
            static (a, _) => ((ToolBarTray)a).TurnBars()));

    /// <summary>Whether the bars stay where they are: their grips do not move them. Set on a tray, its bars inherit it.</summary>
    public static readonly AdamantiumProperty IsLockedProperty = AdamantiumProperty.RegisterAttached("IsLocked",
        typeof(bool), typeof(AdamantiumComponent),
        new PropertyMetadata(false, PropertyMetadataOptions.Inherits | PropertyMetadataOptions.BindsTwoWayByDefault));

    /// <summary>Locks the tray's bars, or frees them; checked while they are locked. Its menu runs it.</summary>
    public static RoutedCommand LockCommand { get; } = new(nameof(LockCommand), typeof(ToolBarTray));

    // How far beyond its first or last row a bar still lands in this tray, in a new row.
    private const double NewRowReach = 16;
    private const double CueThickness = 8;

    private readonly List<MenuItem> _menuBars = [];
    private Separator _menuSeparator;
    private Moving _moving;

    static ToolBarTray()
    {
        CommandManager.RegisterClassCommandBinding(typeof(ToolBarTray), new CommandBinding(LockCommand,
            static (sender, _) =>
            {
                var tray = (ToolBarTray)sender;
                tray.SetCurrentValue(IsLockedProperty, !tray.IsLocked);
            },
            static (sender, e) =>
            {
                e.CanExecute = true;
                e.IsChecked = ((ToolBarTray)sender).IsLocked;
                e.Handled = true;
            }));
    }

    public static bool GetIsLocked(IAdamantiumComponent element) => element.GetValue<bool>(IsLockedProperty);

    public static void SetIsLocked(IAdamantiumComponent element, bool value) => element.SetValue(IsLockedProperty, value);

    /// <summary>Along which way the bars run: across the top or bottom of a window, or down its side.</summary>
    public Orientation Orientation
    {
        get => GetValue<Orientation>(OrientationProperty);
        set => SetValue(OrientationProperty, value);
    }

    public bool IsLocked
    {
        get => GetValue<bool>(IsLockedProperty);
        set => SetValue(IsLockedProperty, value);
    }

    /// <summary>The bars shown in the tray, row by row.</summary>
    public IEnumerable<ToolBar> Bars => ItemContainerGenerator.RealizedIndices
        .OrderBy(index => index)
        .Select(index => ItemContainerGenerator.ContainerFromIndex(index))
        .OfType<ToolBar>()
        .OrderBy(bar => bar.Band)
        .ThenBy(bar => bar.BandIndex);

    private ToolBarTrayPanel Rows => ItemsHostPanel as ToolBarTrayPanel;

    protected internal override bool IsItemItsOwnContainer(object item) => item is ToolBar;

    // A view model's bar is the root of the template picked for it; what is not a bar - a template's other root, or an
    // element put in the tray - stands on a bar of its own.
    protected internal override IUIComponent GetContainerForItem(object item)
    {
        var template = ItemTemplateSelector?.SelectTemplate(item, this) ?? ItemTemplate;
        var built = template?.Build(this)?.RootComponent ?? item as IUIComponent;
        if (built is ToolBar bar)
        {
            return bar;
        }

        var holder = new ToolBar();
        if (built != null)
        {
            holder.Items.Add(built);
        }

        return holder;
    }

    protected internal override bool CanReuseContainer(IUIComponent container, object item) => false;

    protected internal override void PrepareContainer(IUIComponent container, object item)
    {
        if (container is not ToolBar bar)
        {
            return;
        }

        bar.Orientation = Orientation;
        if (!ReferenceEquals(bar, item))
        {
            if (item is not IUIComponent)
            {
                bar.DataContext = item;
            }

            if (ItemContainerStyle != null)
            {
                bar.Styles.Add(ItemContainerStyle);
            }
        }
    }

    // A bar made for an item lets go of it: its data, and an element it stood on, which may be shown again elsewhere.
    protected internal override void ClearContainer(IUIComponent container)
    {
        if (container is ToolBar bar)
        {
            bar.DataContext = null;
            bar.Items.Clear();
        }
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new ToolBarTrayAutomationPeer(this);

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
    }

    // A menu a style shares outlives the tray: left hooked, it would keep every tray that ever had it.
    protected override void OnDiscarded()
    {
        if (ContextMenu is { } menu)
        {
            menu.Opening -= OnMenuOpening;
        }

        base.OnDiscarded();
    }

    private void TurnBars()
    {
        foreach (var bar in Bars)
        {
            bar.Orientation = Orientation;
        }
    }

    private object ItemFor(ToolBar bar)
    {
        var index = ItemContainerGenerator.IndexFromContainer(bar);
        return index >= 0 && index < Items.Count ? Items[index] : null;
    }

    // The bars by name, each checked while shown, before what the theme's menu holds - locking them among it. A menu
    // shared with other trays names the bars of the tray it opens on.
    private void OnMenuOpening(object sender, ContextMenuOpeningEventArgs e)
    {
        var menu = (ContextMenu)sender;
        foreach (var made in _menuBars)
        {
            menu.Items.Remove(made);
        }

        _menuBars.Clear();
        if (_menuSeparator != null)
        {
            menu.Items.Remove(_menuSeparator);
            _menuSeparator = null;
        }

        if (e.Target == null || (!ReferenceEquals(e.Target, this) && !e.Target.GetVisualAncestors().Contains(this)))
        {
            return;
        }

        foreach (var bar in Bars.Where(bar => bar.Header != null))
        {
            var entry = new MenuItem
            {
                Header = bar.Header,
                IsCheckable = true,
                IsChecked = bar.Visibility == Visibility.Visible
            };
            entry.Click += (_, _) => bar.SetCurrentValue(VisibilityProperty,
                entry.IsChecked ? Visibility.Visible : Visibility.Collapsed);
            menu.Items.Insert(_menuBars.Count, entry);
            _menuBars.Add(entry);
        }

        if (_menuBars.Count > 0 && menu.Items.Count > _menuBars.Count)
        {
            _menuSeparator = new Separator();
            menu.Items.Insert(_menuBars.Count, _menuSeparator);
        }
    }

    // --- Moving a bar by its grip ---------------------------------------------------------------------------------

    internal void StartMoving(ToolBar bar, Vector2<double> pressedAt)
    {
        if (IsLocked || _moving != null)
        {
            return;
        }

        var root = RootOf(this);
        var trays = new List<ToolBarTray>();
        root.TraverseVisualTree(component =>
        {
            if (component is ToolBarTray tray && !tray.IsLocked && (ReferenceEquals(tray, this) || CanTakeFrom(tray)))
            {
                trays.Add(tray);
            }
        });

        // Over the rows while it is moved; what its author gave it comes back when it is let go of.
        _moving = new Moving(bar, root, pressedAt, trays);
        bar.IsMoving = true;
        bar.ZIndex = int.MaxValue;
        bar.RenderTransform = _moving.Shift;
    }

    internal void Move(ToolBar bar, Vector2<double> change)
    {
        if (_moving == null || !ReferenceEquals(_moving.Bar, bar))
        {
            return;
        }

        _moving.Shift.TranslateX = change.X;
        _moving.Shift.TranslateY = change.Y;
        var pointer = _moving.PressedAt + change;
        var slot = _moving.Trays.Select(tray => tray.SlotAt(_moving.Root.TranslatePoint(pointer, tray), bar))
            .FirstOrDefault(found => found != null);
        if (!ReferenceEquals(slot?.Tray, _moving.Slot?.Tray))
        {
            _moving.Slot?.Tray.HideCue();
        }

        _moving.Slot = slot;
        slot?.Tray.ShowCue(slot);
    }

    internal void FinishMoving(ToolBar bar, bool canceled)
    {
        if (_moving == null || !ReferenceEquals(_moving.Bar, bar))
        {
            return;
        }

        var moving = _moving;
        var slot = moving.Slot;
        slot?.Tray.HideCue();
        _moving = null;
        bar.RenderTransform = moving.Transform;
        if (moving.HadZIndex)
        {
            bar.ZIndex = moving.ZIndex;
        }
        else
        {
            bar.ClearValue(ZIndexProperty);
        }

        bar.IsMoving = false;
        if (canceled || slot == null)
        {
            return;
        }

        if (ReferenceEquals(slot.Tray, this))
        {
            PutInRow(bar, slot);
            return;
        }

        var item = ItemFor(bar);
        Items.Remove(item);
        Number(RowsOf(null));
        slot.Tray.Items.Add(item);
        if (slot.Tray.ItemContainerGenerator.ContainerFromIndex(slot.Tray.Items.Count - 1) is ToolBar moved)
        {
            slot.Tray.PutInRow(moved, slot);
        }
    }

    // A bar goes from one tray to another only as an item each holds of its own; bars made for a source stay in theirs.
    private bool CanTakeFrom(ToolBarTray tray) => ItemsSource == null && tray.ItemsSource == null;

    private static IUIComponent RootOf(IUIComponent component)
    {
        while (component.VisualParent is { } parent)
        {
            component = parent;
        }

        return component;
    }

    // Where a bar let go of at this point of the tray lands: before the first bar of a row whose middle is past it, or in
    // a new row just above the first or below the last. Null when the point is not over the tray.
    private ToolBarSlot SlotAt(Vector2<double> point, ToolBar moving)
    {
        if (Rows is not { } rows)
        {
            return null;
        }

        var horizontal = Orientation == Orientation.Horizontal;
        var inRows = this.TranslatePoint(point, rows);
        var along = horizontal ? inRows.X : inRows.Y;
        var across = horizontal ? inRows.Y : inRows.X;
        var length = horizontal ? rows.RenderSize.Width : rows.RenderSize.Height;
        var thickness = horizontal ? rows.RenderSize.Height : rows.RenderSize.Width;
        if (along < 0 || along > Math.Max(length, horizontal ? RenderSize.Width : RenderSize.Height) ||
            across < -NewRowReach || across > thickness + NewRowReach)
        {
            return null;
        }

        var bands = rows.Bands;
        if (bands.Count == 0 || across < 0)
        {
            return new ToolBarSlot(this, 0, 0, true);
        }

        var row = bands.Count;
        for (var i = 0; i < bands.Count; i++)
        {
            if (across < bands[i].Offset + bands[i].Thickness)
            {
                row = i;
                break;
            }
        }

        if (row == bands.Count)
        {
            return new ToolBarSlot(this, bands.Count, 0, true);
        }

        var band = bands[row];
        var place = 0;
        double start = 0;
        for (var i = 0; i < band.Bars.Count; i++)
        {
            var middle = start + band.Lengths[i] / 2;
            if (!ReferenceEquals(band.Bars[i], moving) && middle < along)
            {
                place++;
            }

            start += band.Lengths[i];
        }

        return new ToolBarSlot(this, row, place, false);
    }

    // The row the bar is let go of in, the bar at its place, every row numbered again from the first - the rows of hidden
    // bars among them, so a bar shown again comes back where it was. The slot counts the rows shown, the bar's own among
    // them: a bar alone in its row leaves the row when it is lifted, and let go of in it, it is a row of its own again.
    private void PutInRow(ToolBar bar, ToolBarSlot slot)
    {
        var bands = Rows?.Bands ?? [];
        var own = -1;
        for (var i = 0; i < bands.Count; i++)
        {
            if (bands[i].Bars.Count == 1 && ReferenceEquals(bands[i].Bars[0], bar))
            {
                own = i;
            }
        }

        var row = slot.Row;
        var isNewRow = slot.IsNewRow;
        if (own >= 0 && row == own && !isNewRow)
        {
            isNewRow = true;
        }
        else if (own >= 0 && row > own)
        {
            row--;
        }

        var rows = RowsOf(bar);
        var shown = rows.Where(candidate => candidate.Any(IsShown)).ToList();
        row = Math.Clamp(row, 0, shown.Count);
        if (isNewRow || row == shown.Count)
        {
            rows.Insert(row < shown.Count ? rows.IndexOf(shown[row]) : rows.Count, [bar]);
        }
        else
        {
            var target = shown[row];
            var before = target.Where(IsShown).Skip(slot.Place).FirstOrDefault();
            target.Insert(before != null ? target.IndexOf(before) : target.FindLastIndex(IsShown) + 1, bar);
        }

        Number(rows);
    }

    private static bool IsShown(ToolBar bar) => bar.Visibility != Visibility.Collapsed;

    // Every bar's row, hidden bars among them, in the order of the rows; the one being moved left out.
    private List<List<ToolBar>> RowsOf(ToolBar moving)
    {
        var rows = new List<List<ToolBar>>();
        var band = int.MinValue;
        foreach (var bar in Bars.Where(bar => !ReferenceEquals(bar, moving)))
        {
            if (rows.Count == 0 || bar.Band != band)
            {
                rows.Add([]);
                band = bar.Band;
            }

            rows[^1].Add(bar);
        }

        return rows;
    }

    private static void Number(List<List<ToolBar>> rows)
    {
        for (var row = 0; row < rows.Count; row++)
        {
            for (var place = 0; place < rows[row].Count; place++)
            {
                rows[row][place].SetCurrentValue(ToolBar.BandProperty, row);
                rows[row][place].SetCurrentValue(ToolBar.BandIndexProperty, place);
            }
        }
    }

    // --- The cue where the bar would land ---------------------------------------------------------------------------

    private DropInsertionIndicator _cue;
    private AdornerLayer _cueLayer;

    private void ShowCue(ToolBarSlot slot)
    {
        if (Rows is not { } rows || (RootVisual?.Window as IAdornerHost)?.AdornerLayer is not { } layer)
        {
            return;
        }

        var horizontal = Orientation == Orientation.Horizontal;
        var bands = rows.Bands;
        Rect inRows;
        Orientation line;
        if (slot.IsNewRow || bands.Count == 0)
        {
            var across = bands.Count == 0 ? 0 : slot.Row == 0 ? 0 : bands[^1].Offset + bands[^1].Thickness;
            var length = horizontal ? rows.RenderSize.Width : rows.RenderSize.Height;
            inRows = horizontal
                ? new Rect(0, across - CueThickness / 2, length, CueThickness)
                : new Rect(across - CueThickness / 2, 0, CueThickness, length);
            line = horizontal ? Orientation.Horizontal : Orientation.Vertical;
        }
        else
        {
            var band = bands[slot.Row];
            var along = SeamOf(band, slot.Place);
            inRows = horizontal
                ? new Rect(along - CueThickness / 2, band.Offset, CueThickness, band.Thickness)
                : new Rect(band.Offset, along - CueThickness / 2, band.Thickness, CueThickness);
            line = horizontal ? Orientation.Vertical : Orientation.Horizontal;
        }

        var corner = rows.TranslatePoint(new Vector2<double>(inRows.X, inRows.Y), this);
        var target = new Rect(corner.X, corner.Y, inRows.Width, inRows.Height);
        if (_cue != null && ReferenceEquals(_cueLayer, layer) && _cue.TargetRect == target && _cue.Orientation == line)
        {
            return;
        }

        HideCue();
        _cue = new DropInsertionIndicator { AdornedElement = this, Orientation = line, TargetRect = target };
        _cue.ApplyCurrentTheme();
        _cueLayer = layer;
        _cueLayer.Add(_cue);
    }

    // Where the bar would stand in the row: after the bars before its place, the one being moved not counted.
    private double SeamOf(ToolBarBand band, int place)
    {
        double along = 0;
        var counted = 0;
        for (var i = 0; i < band.Bars.Count && counted < place; i++)
        {
            along += band.Lengths[i];
            if (_moving == null || !ReferenceEquals(band.Bars[i], _moving.Bar))
            {
                counted++;
            }
        }

        return along;
    }

    private void HideCue()
    {
        if (_cue != null)
        {
            _cueLayer?.Remove(_cue);
        }

        _cue = null;
        _cueLayer = null;
    }

    /// <summary>A bar being moved: where the grip was pressed, in the window, the trays it may land in, and the layer and
    /// transform its author gave it.</summary>
    private sealed class Moving(ToolBar bar, IUIComponent root, Vector2<double> pressedAt, List<ToolBarTray> trays)
    {
        public ToolBar Bar { get; } = bar;

        public Transform Shift { get; } = new();

        public Transform Transform { get; } = bar.RenderTransform;

        public bool HadZIndex { get; } = bar.GetValueSource(ZIndexProperty) == ValuePriority.Local;

        public int ZIndex { get; } = bar.ZIndex;

        public IUIComponent Root { get; } = root;

        public Vector2<double> PressedAt { get; } = pressedAt;

        public List<ToolBarTray> Trays { get; } = trays;

        public ToolBarSlot Slot { get; set; }
    }
}

/// <summary>Where a moved bar lands: a tray, a row of it and a place in the row, or a new row before that one.</summary>
internal sealed class ToolBarSlot(ToolBarTray tray, int row, int place, bool isNewRow)
{
    public ToolBarTray Tray { get; } = tray;

    public int Row { get; } = row;

    public int Place { get; } = place;

    public bool IsNewRow { get; } = isNewRow;
}
