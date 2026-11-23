using System;
using System.Linq;
using Adamantium.Mathematics;
using Adamantium.UI.Controls.Base;
using Adamantium.UI.Controls.Primitives;
using Adamantium.UI.Controls.Automation;
using Adamantium.UI.Controls.Text;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Automation;
using Adamantium.UI.Core.Input;
using Adamantium.UI.Core.RoutedEvents;

namespace Adamantium.UI.Controls;

/// <summary>
/// A flyout list of <see cref="MenuItem"/> rows, shown in the window's popup overlay (no OS window) against a target or a
/// point. Light-dismisses on an outside click, and closes when any leaf row is clicked. Used for right-click menus and as
/// the overflow menu of a command bar. Items host in the popup via the control template's <c>PART_ItemsPresenter</c>.
/// </summary>
public class ContextMenu : ItemsControl
{
    static ContextMenu()
    {
        FocusManager.IsFocusScopeProperty.OverrideMetadata(typeof(ContextMenu), new PropertyMetadata(true));
    }

    public static readonly AdamantiumProperty IsOpenProperty = AdamantiumProperty.Register(nameof(IsOpen),
        typeof(bool), typeof(ContextMenu), new PropertyMetadata(false, OnIsOpenChanged));

    public static readonly AdamantiumProperty PlacementTargetProperty = AdamantiumProperty.Register(nameof(PlacementTarget),
        typeof(UIComponent), typeof(ContextMenu), new PropertyMetadata(null));

    public static readonly AdamantiumProperty PlacementProperty = AdamantiumProperty.Register(nameof(Placement),
        typeof(PlacementMode), typeof(ContextMenu), new PropertyMetadata(PlacementMode.Bottom));

    public static readonly AdamantiumProperty SelectsRangeProperty = AdamantiumProperty.Register(nameof(SelectsRange),
        typeof(bool), typeof(ContextMenu), new PropertyMetadata(false));

    private static readonly AdamantiumPropertyKey RangeCountPropertyKey = AdamantiumProperty.RegisterReadOnly(
        nameof(RangeCount), typeof(int), typeof(ContextMenu), new PropertyMetadata(0));

    public static readonly AdamantiumProperty RangeCountProperty = RangeCountPropertyKey.Property;

    public static readonly AdamantiumProperty RangeFormatProperty = AdamantiumProperty.Register(nameof(RangeFormat),
        typeof(string), typeof(ContextMenu), new PropertyMetadata(null));

    private Popup _popup;

    /// <summary>The popup the menu shows on, once its template is built.</summary>
    internal Popup Popup => _popup;
    private TextBlock _rangeLine;
    private IInputComponent _clickRoot;   // the items presenter; leaf-row clicks bubble to it (it IS an input element)
    private ScrollViewer _scroll;         // wraps the items; capped to the window height so a long menu scrolls

    /// <summary>Nothing of this control is shown where it stands - the rows live in the popup overlay - so a menu nobody
    /// right-clicked builds nothing at all, not even its own portal. Every element may carry one of these, and most are
    /// never opened.</summary>
    protected override bool DeferTemplate => !IsOpen;

    /// <summary>Whether the menu is shown. Set true (with a PlacementTarget) to open; cleared on pick / outside click.</summary>
    public bool IsOpen
    {
        get => GetValue<bool>(IsOpenProperty);
        set => SetValue(IsOpenProperty, value);
    }

    /// <summary>The element the menu positions against (e.g. the button that opened it).</summary>
    public UIComponent PlacementTarget
    {
        get => GetValue<UIComponent>(PlacementTargetProperty);
        set => SetValue(PlacementTargetProperty, value);
    }

    public PlacementMode Placement
    {
        get => GetValue<PlacementMode>(PlacementProperty);
        set => SetValue(PlacementProperty, value);
    }

    /// <summary>Whether the menu selects a RANGE, as an undo list does: the pointer or the keyboard on a row marks every
    /// row from the first to that one (<see cref="MenuItem.IsInRange"/>), <see cref="RangeCount"/> says how many, and the
    /// line under the rows says it in <see cref="RangeFormat"/>. Choosing the row is choosing them all - its command
    /// decides what that means.</summary>
    public bool SelectsRange
    {
        get => GetValue<bool>(SelectsRangeProperty);
        set => SetValue(SelectsRangeProperty, value);
    }

    /// <summary>How many rows the range reaches, from the first; 0 while the pointer and the keyboard are on none.</summary>
    public int RangeCount
    {
        get => GetValue<int>(RangeCountProperty);
        private set => SetValue(RangeCountPropertyKey, value);
    }

    /// <summary>What the line under the rows of a menu that selects a range says, <c>{0}</c> standing for
    /// <see cref="RangeCount"/>: "Undo {0} steps". No line while it is null or the range is empty.</summary>
    public string RangeFormat
    {
        get => GetValue<string>(RangeFormatProperty);
        set => SetValue(RangeFormatProperty, value);
    }

    /// <summary>Extra offset of the popup from its placement (used to open AT the cursor for a right-click menu:
    /// Placement=Relative + the click point).</summary>
    public double HorizontalOffset { get; set; }
    public double VerticalOffset { get; set; }

    /// <summary>Whether a press on the placement target leaves the menu open, so a button's second press closes it. Off by
    /// default: a right-click target may be a whole panel.</summary>
    public bool IgnoreTargetPress { get; set; }

    // A flyout occupies NO space where it is authored - its rows live in the popup overlay. Measuring to zero is not
    // enough: a stretching slot arranges it to the whole cell anyway, and being last in z-order it then swallows every
    // press meant for what it sits over (the quick-access buttons went dead under their own overflow menu).
    protected override Size<double> MeasureOverride(Size<double> availableSize)
    {
        base.MeasureOverride(availableSize);
        return Size<double>.Zero;
    }

    protected override Size<double> ArrangeOverride(Size<double> finalSize)
    {
        base.ArrangeOverride(Size<double>.Zero);
        return Size<double>.Zero;
    }

    /// <summary>Puts the keyboard on the menu's first row once its rows are built - called right after opening it. What
    /// a menu opened BY THE KEYBOARD owes its user: the rows are drawn in the overlay, so nothing in the opener's own
    /// tree can be stepped into.</summary>
    public void MoveKeyboardInsideWhenReady()
    {
        if (_popup == null) return;

        // Not now: the rows are generated when the flyout is measured, which happens on the popup layer's next pass.
        // That pass is also the only thing that can tell us they exist - the window's layout never touches this
        // subtree - so listen to it and step in on the first tick where there is something to step into.
        _popup.LayerPass -= OnLayerPass;
        _popup.LayerPass += OnLayerPass;
    }

    private void OnLayerPass(object sender, EventArgs e)
    {
        if (_popup?.Child is not { } content || !Core.Input.KeyboardNavigation.MoveInto(content)) return;

        _popup.LayerPass -= OnLayerPass;
    }

    /// <summary>Raised as the menu is about to open, before it is shown: the target, or something attached to it, can make
    /// its rows fit the moment - which apply, and what they offer for the point it opens at.</summary>
    public event EventHandler<ContextMenuOpeningEventArgs> Opening;

    /// <summary>Opens the menu positioned against <paramref name="target"/>. A menu that turns out to have no rows shuts
    /// again before it is drawn: an empty card offers nothing.</summary>
    public void Open(UIComponent target)
    {
        Opening?.Invoke(this, new ContextMenuOpeningEventArgs(target, null));
        PlacementTarget = target;
        IsOpen = true;
    }

    /// <summary>Opens the menu AT <paramref name="point"/> (in <paramref name="target"/>-local coordinates) - the right-click
    /// entry point: places relative to the target's top-left, then offsets to the click point.</summary>
    public void Open(UIComponent target, Vector2<double> point) => OpenAt(target, point, point);

    // The keyboard opens the menu at a place of its own choosing, not at a point anybody pointed at.
    internal void OpenFromKeyboard(UIComponent target, Vector2<double> point) => OpenAt(target, point, null);

    private void OpenAt(UIComponent target, Vector2<double> point, Vector2<double>? pointed)
    {
        Opening?.Invoke(this, new ContextMenuOpeningEventArgs(target, pointed));
        Placement = PlacementMode.Relative;
        HorizontalOffset = point.X;
        VerticalOffset = point.Y;
        PlacementTarget = target;
        IsOpen = true;
    }

    // Every data row is a MenuItem, whatever the template: the template draws its header. A node flagged ISeparatorItem
    // becomes a Separator.
    protected internal override IUIComponent GetContainerForItem(object item)
        => item is ISeparatorItem { IsSeparator: true } ? new Separator() : MenuItem.CreateContainer(ItemContainerStyle);

    protected internal override void PrepareContainer(IUIComponent container, object item)
    {
        base.PrepareContainer(container, item);
        if (container is MenuItem row)
        {
            _rows.Join(row);
            row.PointerEntered += OnRowReached;
            row.GotFocus += OnRowReached;
        }
    }

    protected internal override void ClearContainer(IUIComponent container)
    {
        if (container is MenuItem row)
        {
            _rows.Leave(row);
            row.PointerEntered -= OnRowReached;
            row.GotFocus -= OnRowReached;
            row.IsInRange = false;
        }

        base.ClearContainer(container);
    }

    private void OnRowReached(object sender, RoutedEventArgs e)
    {
        if (SelectsRange && sender is MenuItem row && ItemContainerGenerator.IndexFromContainer(row) is >= 0 and var index)
        {
            Reach(index + 1);
        }
    }

    private void Reach(int count)
    {
        RangeCount = count;
        for (var index = 0; index < Items.Count; index++)
        {
            if (ItemContainerGenerator.ContainerFromIndex(index) is MenuItem row)
            {
                row.IsInRange = index < count;
            }
        }

        ShowRangeLine();
    }

    private void ShowRangeLine()
    {
        if (_rangeLine == null)
        {
            return;
        }

        var shown = SelectsRange && RangeCount > 0 && RangeFormat != null;
        _rangeLine.Visibility = shown ? Visibility.Visible : Visibility.Collapsed;
        _rangeLine.Text = shown ? string.Format(System.Globalization.CultureInfo.CurrentCulture, RangeFormat, RangeCount) : null;
    }

    protected override void OnItemsChanged(System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        base.OnItemsChanged(e);
        _rows.LeaveRemoved(e, Items, ItemContainerGenerator);
    }

    private readonly MenuRows _rows = new();

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        DetachParts();   // a template swap re-runs this; drop the old wiring first

        // The card is built on first open, so neither the scroller nor the items presenter is in this template's
        // namescope - both arrive with the content.
        _popup = GetTemplateChild("PART_Popup") as Popup;
        if (_popup != null)
        {
            _popup.ContentBuilt += OnPopupContentBuilt;
            _popup.PlacementTarget = PlacementTarget ?? this;
            _popup.Placement = Placement;
            _popup.HorizontalOffset = HorizontalOffset;
            _popup.VerticalOffset = VerticalOffset;
            _popup.FlipToFit = true;
            _popup.KeepOpen = false;                     // click-outside-to-close (submenu overlays included), owned by Popup
            _popup.IgnoreTargetPress = IgnoreTargetPress;
            _popup.Closed -= OnPopupClosed;
            _popup.Closed += OnPopupClosed;
            _popup.IsOpen = IsOpen;
        }
    }

    // The card only exists once the popup has built its deferred content - take its parts then.
    private void OnPopupContentBuilt(object sender, EventArgs e)
    {
        var popup = (Popup)sender;

        _clickRoot = popup.FindContentChild("PART_ItemsPresenter") as IInputComponent;
        _scroll = popup.FindContentChild("PART_MenuScroll") as ScrollViewer;
        _rangeLine = popup.FindContentChild("PART_RangeLine") as TextBlock;
        ShowRangeLine();

        // base.OnApplyTemplate connected the items host while it was still in this control's namescope; now it isn't, so
        // the connection is made here instead - an unconnected host has no owner and never grows an items panel.
        if (_clickRoot is ItemsPresenter presenter)
        {
            ConnectPresenter(presenter);
        }

        // Scrolling the list = browsing it, not navigating a submenu: close any open submenu so it doesn't ride along with
        // the scrolled row it's anchored to. A NAMED handler, not a lambda: a lambda cannot be taken off again, and this
        // one has to come off when the template goes.
        if (_scroll != null)
        {
            _scroll.ScrollChanged += OnMenuScrolled;
        }

        // Any leaf row's Click bubbles up to the items presenter - close the menu after the command has run.
        // The handler INSTANCE is kept: RemoveHandler matches on the delegate, so a freshly-made one would not take off
        // the one that was added.
        _itemClicked ??= OnItemClicked;
        _clickRoot?.AddHandler(MenuItem.ClickEvent, _itemClicked, handledEventsToo: true);
    }

    /// <summary>Let the template's parts go when the template does - see ScrollBar.OnRemoveTemplate.</summary>
    public override void OnRemoveTemplate()
    {
        base.OnRemoveTemplate();
        DetachParts();
    }

    private void DetachParts()
    {
        if (_scroll != null) _scroll.ScrollChanged -= OnMenuScrolled;
        if (_clickRoot != null && _itemClicked != null) _clickRoot.RemoveHandler(MenuItem.ClickEvent, _itemClicked);
        if (_popup != null)
        {
            _popup.Closed -= OnPopupClosed;
            _popup.ContentBuilt -= OnPopupContentBuilt;
        }

        _scroll = null;
        _clickRoot = null;
        _rangeLine = null;
        _popup = null;
    }

    private RoutedEventHandler _itemClicked;

    private void OnMenuScrolled(object sender, EventArgs e) => CloseAllSubmenus();

    private void OnItemClicked(object sender, RoutedEventArgs e)
    {
        if (MenuItem.KeepsMenuOpen(e))
        {
            return;
        }

        IsOpen = false;
    }

    // The popup light-dismissed (a press outside the menu's overlay) - drive our IsOpen false so the whole menu tears down.
    private void OnPopupClosed(object sender, EventArgs e) => IsOpen = false;

    private static void OnIsOpenChanged(AdamantiumComponent a, AdamantiumPropertyChangedEventArgs e)
    {
        var menu = (ContextMenu)a;
        // First open: build the template now. Placement is stated on the menu BEFORE IsOpen (see Open), so the popup
        // OnApplyTemplate hands it is already positioned; what follows just re-states it for every later open.
        if ((bool)e.NewValue)
        {
            menu.EnsureTemplate();
            CommandManager.RequeryNow();
            menu.Reach(0);
        }

        if (menu._popup != null)
        {
            menu._popup.PlacementTarget = menu.PlacementTarget ?? menu;
            menu._popup.Placement = menu.Placement;
            menu._popup.HorizontalOffset = menu.HorizontalOffset;
            menu._popup.VerticalOffset = menu.VerticalOffset;
            // Re-stated here as well: an owner that wires the menu (a drop-down button) sets this while the menu may not
            // be templated yet, so the value read once in OnApplyTemplate would be the stale default.
            menu._popup.IgnoreTargetPress = menu.IgnoreTargetPress;
            menu._popup.IsOpen = (bool)e.NewValue;
            menu._popup.LayerPass -= menu.OnOpenedPass;
            if ((bool)e.NewValue) menu._popup.LayerPass += menu.OnOpenedPass;
            // Put away with no row stepped into, the keyboard is not taken on a later opening it was not asked for.
            else menu._popup.LayerPass -= menu.OnLayerPass;
        }
        if ((bool)e.NewValue)
        {
            if (menu._scroll != null) menu._scroll.MaxHeight = Popup.WindowHeightCap(Popup.FindPopupHost(menu));
        }
        else menu.CloseAllSubmenus();
    }

    // Whether there is anything to offer is asked in the first pass after opening, not when asked to open: the frame's
    // bindings are applied before the pass, and the choice the menu is about - a row picked by the right button's press
    // - is often made in the same frame as the release that opens it. Asked earlier, the menu would go by what the
    // previous choice offered. Shut here, the empty card is never drawn.
    private void OnOpenedPass(object sender, EventArgs e)
    {
        ((Popup)sender).LayerPass -= OnOpenedPass;
        if (Items.Count == 0) IsOpen = false;
    }

    // Closing the menu must close every open submenu flyout too (they are independent overlay popups). Close each top-level
    // row's submenu - which cascades down through MenuItem's own close handler.
    private void CloseAllSubmenus()
    {
        // OnIsOpenChanged fires for the default during construction, before the base ItemsControl builds the generator.
        if (ItemContainerGenerator is null) return;
        foreach (var index in ItemContainerGenerator.RealizedIndices.ToList())
            if (ItemContainerGenerator.ContainerFromIndex(index) is MenuItem child)
                child.IsSubmenuOpen = false;
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new ContextMenuAutomationPeer(this);
}
