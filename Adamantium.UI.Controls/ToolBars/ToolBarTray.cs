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

    /// <summary>Whether the tray's bars may go to another tray of the window, and another's come to it - when each holds
    /// its bars as items of its own. A control whose bars are its own parts says no: they move only within it.</summary>
    public static readonly AdamantiumProperty SharesBarsProperty = AdamantiumProperty.Register(nameof(SharesBars),
        typeof(bool), typeof(ToolBarTray), new PropertyMetadata(true));

    /// <summary>Over the bars the user shows and hides, in the tray's menu, when it lists contextual bars too.</summary>
    public static readonly AdamantiumProperty BarsHeaderProperty = AdamantiumProperty.Register(nameof(BarsHeader),
        typeof(string), typeof(ToolBarTray), new PropertyMetadata(null));

    /// <summary>Over the contextual bars, in the tray's menu.</summary>
    public static readonly AdamantiumProperty ContextualBarsHeaderProperty = AdamantiumProperty.Register(
        nameof(ContextualBarsHeader), typeof(string), typeof(ToolBarTray), new PropertyMetadata(null));

    /// <summary>Locks the tray's bars, or frees them; checked while they are locked. Its menu runs it.</summary>
    public static RoutedCommand LockCommand { get; } = new(nameof(LockCommand), typeof(ToolBarTray));

    // How far beyond its first or last row a bar still lands in this tray, in a new row.
    private const double NewRowReach = 16;
    private const double CueThickness = 8;

    // What the tray put in its menu when it last opened: its bars, the headers over them and the rule after them.
    private readonly List<IUIComponent> _menuMade = [];
    private ToolBar _placing;   // the bar whose place is marked: its own place in its row is not counted

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

    /// <summary>The room between two bars of a row - only between them, none after the last - a theme metric.</summary>
    public static readonly AdamantiumProperty BarSpacingProperty = AdamantiumProperty.Register(nameof(BarSpacing),
        typeof(double), typeof(ToolBarTray),
        new PropertyMetadata(0.0, PropertyMetadataOptions.AffectsMeasure, OnSpacingChanged));

    /// <summary>The room between two rows - across the tray, so between columns down the side - a theme metric.</summary>
    public static readonly AdamantiumProperty BandSpacingProperty = AdamantiumProperty.Register(nameof(BandSpacing),
        typeof(double), typeof(ToolBarTray),
        new PropertyMetadata(0.0, PropertyMetadataOptions.AffectsMeasure, OnSpacingChanged));

    // The rows' panel reads it from the tray: told nothing, it would keep its old layout.
    private static void OnSpacingChanged(AdamantiumComponent a, AdamantiumPropertyChangedEventArgs e) =>
        ((ToolBarTray)a).ItemsHostPanel?.InvalidateMeasure();

    public double BarSpacing
    {
        get => GetValue<double>(BarSpacingProperty);
        set => SetValue(BarSpacingProperty, value);
    }

    public double BandSpacing
    {
        get => GetValue<double>(BandSpacingProperty);
        set => SetValue(BandSpacingProperty, value);
    }

    public bool SharesBars
    {
        get => GetValue<bool>(SharesBarsProperty);
        set => SetValue(SharesBarsProperty, value);
    }

    /// <summary>The theme gives its own words; none, no header.</summary>
    public string BarsHeader
    {
        get => GetValue<string>(BarsHeaderProperty);
        set => SetValue(BarsHeaderProperty, value);
    }

    /// <summary>The theme gives its own words; none, no header.</summary>
    public string ContextualBarsHeader
    {
        get => GetValue<string>(ContextualBarsHeaderProperty);
        set => SetValue(ContextualBarsHeaderProperty, value);
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

    // The bars by name, each checked while shown, before what the theme's menu holds - locking them among it. Contextual
    // bars stand apart under a header of their own, checked while shown but not switched: what the user is on shows them.
    // A menu shared with other trays names the bars of the tray it opens on.
    private void OnMenuOpening(object sender, ContextMenuOpeningEventArgs e)
    {
        var menu = (ContextMenu)sender;
        foreach (var made in _menuMade)
        {
            menu.Items.Remove(made);
        }

        _menuMade.Clear();
        if (e.Target == null || (!ReferenceEquals(e.Target, this) && !e.Target.GetVisualAncestors().Contains(this)))
        {
            return;
        }

        var named = Bars.Where(bar => bar.Header != null).ToList();
        var contextual = named.Where(bar => bar.IsContextual).ToList();
        var headed = contextual.Count > 0 && contextual.Count < named.Count;
        if (headed && BarsHeader != null)
        {
            AddToMenu(menu, new MenuHeader { Text = BarsHeader });
        }

        foreach (var bar in named.Where(bar => !bar.IsContextual))
        {
            var entry = new MenuItem
            {
                Header = bar.Header,
                IsCheckable = true,
                IsChecked = bar.Visibility == Visibility.Visible
            };
            entry.Click += (_, _) => bar.SetCurrentValue(VisibilityProperty,
                entry.IsChecked ? Visibility.Visible : Visibility.Collapsed);
            AddToMenu(menu, entry);
        }

        if (headed && ContextualBarsHeader != null)
        {
            AddToMenu(menu, new MenuHeader { Text = ContextualBarsHeader });
        }

        foreach (var bar in contextual)
        {
            AddToMenu(menu, new MenuItem
            {
                Header = bar.Header,
                IsCheckable = true,
                IsChecked = bar.Visibility == Visibility.Visible,
                IsEnabled = false
            });
        }

        if (_menuMade.Count > 0 && menu.Items.Count > _menuMade.Count)
        {
            AddToMenu(menu, new Separator());
        }
    }

    // Put in after what the tray has already put in, before the theme's own rows.
    private void AddToMenu(ContextMenu menu, IUIComponent made)
    {
        menu.Items.Insert(_menuMade.Count, made);
        _menuMade.Add(made);
    }

    // --- Taking a bar dropped on the tray -------------------------------------------------------------------------
    // The tray moves no bar itself: whoever lets the user drag bars - with DragDrop, ToolBarDragBehavior - asks it
    // where a bar would land and puts it there.

    /// <summary>Whether <paramref name="bar"/>, let go of at <paramref name="point"/> of the tray, would land in it - a bar
    /// of its own, or one of another tray it may take - and if so marks where.</summary>
    public bool ShowDropPlace(ToolBar bar, Vector2<double> point)
    {
        var slot = CanTake(bar) ? SlotAt(point, bar) : null;
        if (slot == null)
        {
            HideCue();
            return false;
        }

        _placing = bar;
        ShowCue(slot);
        return true;
    }

    /// <summary>Takes the mark of where a bar would land away.</summary>
    public void HideDropPlace()
    {
        HideCue();
        _placing = null;
    }

    /// <summary>Puts <paramref name="bar"/>, let go of at <paramref name="point"/> of the tray, where it lands: in its row
    /// of this tray, or out of its own tray into this one. False when it does not land here.</summary>
    public bool DropBar(ToolBar bar, Vector2<double> point)
    {
        HideDropPlace();
        var slot = CanTake(bar) ? SlotAt(point, bar) : null;
        if (slot == null)
        {
            return false;
        }

        if (ReferenceEquals(bar.Tray, this))
        {
            PutInRow(bar, slot);
            return true;
        }

        var from = bar.Tray;
        var item = from.ItemFor(bar);
        from.Items.Remove(item);
        Number(from.RowsOf(null));
        Items.Add(item);
        if (ItemContainerGenerator.ContainerFromIndex(Items.Count - 1) is ToolBar moved)
        {
            PutInRow(moved, slot);
        }

        return true;
    }

    /// <summary>Whether a bar is being carried: it stands where it was, dimmed by its theme, while its ghost moves.</summary>
    public static void SetIsMoving(ToolBar bar, bool moving) => bar.IsMoving = moving;

    /// <summary>Whether a bar dropped here may land: neither tray locked, and the bar this tray's own or one of a tray both
    /// share their bars with. A tray that takes only some bars - those of one control - says so here.</summary>
    protected virtual bool CanTake(ToolBar bar) =>
        !IsLocked && bar.Tray is { IsLocked: false } from && (ReferenceEquals(from, this) || CanTakeFrom(from));

    // A bar goes from one tray to another only as an item each holds of its own, and only where both share their bars;
    // bars made for a source stay in theirs.
    private bool CanTakeFrom(ToolBarTray tray) =>
        SharesBars && tray.SharesBars && ItemsSource == null && tray.ItemsSource == null;

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

    // Where the bar would stand in the row: after the bars before its place, the one being placed not counted.
    private double SeamOf(ToolBarBand band, int place)
    {
        double along = 0;
        var counted = 0;
        for (var i = 0; i < band.Bars.Count && counted < place; i++)
        {
            along += band.Lengths[i] + (i > 0 ? BarSpacing : 0);
            if (!ReferenceEquals(band.Bars[i], _placing))
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
}

/// <summary>Where a moved bar lands: a tray, a row of it and a place in the row, or a new row before that one.</summary>
internal sealed class ToolBarSlot(ToolBarTray tray, int row, int place, bool isNewRow)
{
    public ToolBarTray Tray { get; } = tray;

    public int Row { get; } = row;

    public int Place { get; } = place;

    public bool IsNewRow { get; } = isNewRow;
}
