using System.Collections.Specialized;
using Adamantium.Core.Commands;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Dispatcher;
using Adamantium.UI.Core.Input;
using Adamantium.UI.Controls.Automation;
using Adamantium.UI.Core.Automation;
using Adamantium.UI.Core.Resources;
using Adamantium.UI.Core.RoutedEvents;
using Adamantium.UI.Core.Templates;

namespace Adamantium.UI.Controls.Primitives;

/// <summary>A menu row: a leaf runs <see cref="Command"/> and raises <see cref="Click"/>; an item with children opens them
/// as a submenu on hover. Dividers are <see cref="Separator"/>s, never MenuItems.</summary>
public class MenuItem : ItemsControl, IHeaderedItemsControl
{
    public static readonly AdamantiumProperty HeaderProperty = AdamantiumProperty.Register(nameof(Header),
        typeof(object), typeof(MenuItem), new PropertyMetadata(null));

    public static readonly AdamantiumProperty HeaderTemplateProperty = AdamantiumProperty.Register(nameof(HeaderTemplate),
        typeof(DataTemplate), typeof(MenuItem), new PropertyMetadata(null));

    public static readonly AdamantiumProperty IconProperty = AdamantiumProperty.Register(nameof(Icon),
        typeof(object), typeof(MenuItem), new PropertyMetadata(null));

    public static readonly AdamantiumProperty InputGestureTextProperty = AdamantiumProperty.Register(nameof(InputGestureText),
        typeof(string), typeof(MenuItem), new PropertyMetadata(null));

    public static readonly AdamantiumProperty CommandProperty = AdamantiumProperty.Register(nameof(Command),
        typeof(ICommand), typeof(MenuItem), new PropertyMetadata(null, OnCommandChanged));

    public static readonly AdamantiumProperty CommandParameterProperty = AdamantiumProperty.Register(nameof(CommandParameter),
        typeof(object), typeof(MenuItem), new PropertyMetadata(null, OnCommandParameterChanged));

    public static readonly AdamantiumProperty IsCheckableProperty = AdamantiumProperty.Register(nameof(IsCheckable),
        typeof(bool), typeof(MenuItem), new PropertyMetadata(false));

    public static readonly AdamantiumProperty IsCheckedProperty = AdamantiumProperty.Register(nameof(IsChecked),
        typeof(bool), typeof(MenuItem), new PropertyMetadata(false, PropertyMetadataOptions.BindsTwoWayByDefault));

    public static readonly AdamantiumProperty StaysOpenOnClickProperty = AdamantiumProperty.Register(nameof(StaysOpenOnClick),
        typeof(bool), typeof(MenuItem), new PropertyMetadata(false));

    // A row follows its command's availability, the way a button does: grayed out while the command says no, instead of
    // looking ordinary and doing nothing when clicked. The subscription is WEAK (the relay holds the row weakly), so a
    // command owned by a long-lived view-model does not keep a dismissed menu's rows alive - a menu is built and thrown
    // away on every open, so that leak would be per-open.
    private WeakCanExecuteChangedRelay<MenuItem> _commandRelay;

    private static void OnCommandChanged(AdamantiumComponent a, AdamantiumPropertyChangedEventArgs e)
    {
        if (a is MenuItem item) item.OnCommandChanged(e.NewValue as ICommand);
    }

    private static void OnCommandParameterChanged(AdamantiumComponent a, AdamantiumPropertyChangedEventArgs e)
    {
        if (a is MenuItem item) item.UpdateCanExecute();
    }

    private void OnCommandChanged(ICommand newCommand)
    {
        _commandRelay?.Detach();
        _commandRelay = newCommand != null
            ? new WeakCanExecuteChangedRelay<MenuItem>(newCommand, this, static i => i.UpdateCanExecute())
            : null;
        UpdateCanExecute();
    }

    private void UpdateCanExecute()
    {
        // A PARENT row opens a submenu and runs nothing, so a command on it says nothing about whether it can be used.
        var command = Command;
        IsEnabled = HasItems || command == null || command.CanExecute(CommandParameter);
    }

    // Read-only: true once the item has children (so it's a submenu parent, not a leaf). Drives the chevron + the flyout.
    public static readonly AdamantiumProperty HasItemsProperty = AdamantiumProperty.Register(nameof(HasItems),
        typeof(bool), typeof(MenuItem), new PropertyMetadata(false));

    // Whether this parent item's submenu flyout is shown. The template binds a Popup.IsOpen to it.
    public static readonly AdamantiumProperty IsSubmenuOpenProperty = AdamantiumProperty.Register(nameof(IsSubmenuOpen),
        typeof(bool), typeof(MenuItem), new PropertyMetadata(false, OnIsSubmenuOpenChanged));

    public static readonly RoutedEvent ClickEvent = EventManager.RegisterRoutedEvent(nameof(Click),
        RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(MenuItem));

    static MenuItem()
    {
        // A menu row is a keyboard-focus target (arrow-key navigation) - opt in, since the base default is false.
        FocusableProperty.OverrideMetadata(typeof(MenuItem), new PropertyMetadata(true));
    }

    public MenuItem()
    {
        Items.CollectionChanged += OnItemsCollectionChanged;
    }

    /// <summary>The row's label.</summary>
    public object Header { get => GetValue<object>(HeaderProperty); set => SetValue(HeaderProperty, value); }

    /// <summary>Template that renders <see cref="Header"/> (the menu's item template, for a row made from data).</summary>
    public DataTemplate HeaderTemplate { get => GetValue<DataTemplate>(HeaderTemplateProperty); set => SetValue(HeaderTemplateProperty, value); }

    /// <summary>Optional icon/glyph shown at the left of the row.</summary>
    public object Icon { get => GetValue<object>(IconProperty); set => SetValue(IconProperty, value); }

    /// <summary>Optional shortcut hint shown right-aligned (e.g. "Ctrl+S"). Display only - it wires nothing.</summary>
    public string InputGestureText { get => GetValue<string>(InputGestureTextProperty); set => SetValue(InputGestureTextProperty, value); }

    /// <summary>Command run when a LEAF item is clicked.</summary>
    public ICommand Command { get => GetValue<ICommand>(CommandProperty); set => SetValue(CommandProperty, value); }

    public object CommandParameter { get => GetValue<object>(CommandParameterProperty); set => SetValue(CommandParameterProperty, value); }

    /// <summary>Whether choosing the row flips <see cref="IsChecked"/>.</summary>
    public bool IsCheckable { get => GetValue<bool>(IsCheckableProperty); set => SetValue(IsCheckableProperty, value); }

    /// <summary>Whether the row is checked; the theme draws a check mark in the icon gutter. Binds two-way by default.</summary>
    public bool IsChecked { get => GetValue<bool>(IsCheckedProperty); set => SetValue(IsCheckedProperty, value); }

    /// <summary>Whether the menu stays open after the row is chosen - for a list of switches flipped one after another.</summary>
    public bool StaysOpenOnClick { get => GetValue<bool>(StaysOpenOnClickProperty); set => SetValue(StaysOpenOnClickProperty, value); }

    /// <summary>True when the item has children (a submenu parent). Read-only.</summary>
    public bool HasItems { get => GetValue<bool>(HasItemsProperty); private set => SetValue(HasItemsProperty, value); }

    /// <summary>Whether this parent's submenu flyout is open.</summary>
    public bool IsSubmenuOpen { get => GetValue<bool>(IsSubmenuOpenProperty); set => SetValue(IsSubmenuOpenProperty, value); }

    /// <summary>Raised when a LEAF item is activated (after its Command runs). A hosting menu listens for this to close
    /// the whole flyout.</summary>
    public event RoutedEventHandler Click
    {
        add => AddHandler(ClickEvent, value);
        remove => RemoveHandler(ClickEvent, value);
    }

    private void OnItemsCollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
    {
        HasItems = Items.Count > 0;
        UpdateCanExecute();   // becoming (or ceasing to be) a parent changes whether a command decides this row's state
    }

    // Submenu open/close side effects: on open, cap its scroll to the window; on close, recursively close every deeper
    // submenu (overlay popups aren't detached on close, so a nested one would otherwise linger).
    private static void OnIsSubmenuOpenChanged(AdamantiumComponent a, AdamantiumPropertyChangedEventArgs e)
    {
        if (a is not MenuItem mi) return;
        AutomationEvents.RaiseExpandCollapse(mi, (bool)e.NewValue);
        if ((bool)e.NewValue)
        {
            // Opening: cap the submenu's scroll to the window (the row is in the overlay, so find the window via the popup
            // host recorded on an ancestor overlay root) so a long submenu scrolls instead of clipping.
            if (mi._scroll != null) mi._scroll.MaxHeight = Popup.WindowHeightCap(Popup.FindPopupHost(mi));
            return;
        }
        // Closing must close everything BELOW too: the flyout's rows live in an overlay popup that isn't detached when the
        // popup closes, so a deeper open submenu would otherwise linger.
        mi.CloseChildSubmenus();
    }

    // Close every open child submenu (skipped during construction: the generator isn't built yet).
    private void CloseChildSubmenus()
    {
        if (ItemContainerGenerator is null) return;
        foreach (var index in ItemContainerGenerator.RealizedIndices.ToList())
            if (ItemContainerGenerator.ContainerFromIndex(index) is MenuItem child)
                child.IsSubmenuOpen = false;
    }

    // --- Container seam: a MenuItem hosts its submenu items in nested MenuItem containers, whatever the template - the
    // template draws the header. Mirrors ListBox -> ListBoxItem. A node flagged ISeparatorItem becomes a Separator. --
    protected internal override IUIComponent GetContainerForItem(object item)
    {
        if (item is ISeparatorItem { IsSeparator: true }) return new Separator();
        return CreateContainer(ItemContainerStyle);
    }

    protected internal override void PrepareContainer(IUIComponent container, object item)
    {
        if (container is MenuItem row)
        {
            row.OwnerMenu = this;
        }

        base.PrepareContainer(container, item);
    }

    /// <summary>The MenuItem whose submenu this row lives in (null for a ContextMenu's own top-level rows).</summary>
    internal MenuItem OwnerMenu { get; set; }

    /// <summary>Creates a MenuItem container carrying the owner's ItemContainerStyle (into Styles, applied AFTER the theme).</summary>
    internal static MenuItem CreateContainer(Style itemContainerStyle)
    {
        var container = new MenuItem();
        if (itemContainerStyle != null) container.Styles.Add(itemContainerStyle);
        return container;
    }

    private ScrollViewer _scroll;   // wraps this item's submenu items; capped to the window height so a long submenu scrolls
    private DispatcherTimer _closeTimer;   // closes this row's submenu a moment after the pointer leaves the branch
    private const int CloseDelayMs = 400;

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();   // can't find PART_ItemsPresenter yet - it lives in the submenu's lazily-built content
        DetachParts();   // a template swap re-runs this; drop the old wiring first

        // The submenu's card + scroll host + items presenter build lazily on first open (Popup.ChildTemplate), so wire those
        // parts up when they arrive instead of up front - keeps a never-opened leaf from ever building that subtree.
        _submenuPopup = GetTemplateChild("PART_SubmenuPopup") as Popup;
        if (_submenuPopup != null) _submenuPopup.ContentBuilt += OnSubmenuContentBuilt;
    }

    /// <summary>Let the template's parts go when the template does - see ScrollBar.OnRemoveTemplate. This one matters
    /// most of the family: the popup OUTLIVES nothing here, but the handler was never removed at all, and the popup
    /// reference was not even kept - so there was no way to remove it later.</summary>
    public override void OnRemoveTemplate()
    {
        base.OnRemoveTemplate();
        DetachParts();
    }

    private void DetachParts()
    {
        if (_submenuPopup != null) _submenuPopup.ContentBuilt -= OnSubmenuContentBuilt;
        _submenuPopup = null;
    }

    private Popup _submenuPopup;

    private void OnSubmenuContentBuilt(object sender, EventArgs e)
    {
        var popup = (Popup)sender;
        _scroll = popup.FindContentChild("PART_MenuScroll") as ScrollViewer;
        // Scrolling this item's submenu list closes any open grandchild submenu so it doesn't ride along with its row.
        if (_scroll != null)
        {
            _scroll.ScrollChanged += (_, _) => CloseChildSubmenus();
            // Cap here too: on the FIRST open the content isn't built yet when OnIsSubmenuOpenChanged runs, so it applies the
            // window cap now (later opens reuse this built content and cap via OnIsSubmenuOpenChanged).
            _scroll.MaxHeight = Popup.WindowHeightCap(Popup.FindPopupHost(this));
        }
        // Connect the items host now that it exists, and watch its Click chain: a leaf's Click bubbles only to the presenter
        // of ITS popup, so each parent closes its flyout on a descendant Click and re-raises Click on itself (which lives in
        // the OUTER popup), collapsing the whole chain out to the ContextMenu.
        if (popup.FindContentChild("PART_ItemsPresenter") is ItemsPresenter itemsPresenter)
        {
            ConnectPresenter(itemsPresenter);
            if (itemsPresenter is IInputComponent input)
                input.AddHandler(ClickEvent, new RoutedEventHandler(OnDescendantClicked), handledEventsToo: true);
        }
    }

    private void OnDescendantClicked(object sender, RoutedEventArgs e)
    {
        if (ReferenceEquals(e.Source, this)) return;   // our own re-raise, already bubbling outward
        if (KeepsMenuOpen(e)) return;
        IsSubmenuOpen = false;
        RaiseEvent(new RoutedEventArgs(ClickEvent, this) { RoutedEvent = ClickEvent });
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        // When the owning flyout closes this row detaches; drop the submenu state so it doesn't spontaneously re-open the
        // next time the menu is shown.
        IsSubmenuOpen = false;
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnMouseEnter(MouseEventArgs e)
    {
        base.OnMouseEnter(e);
        // The pointer is back on a menu row: cancel any pending close - of THIS row (re-entered) and of the parent whose
        // submenu we just moved INTO (so it doesn't close behind us).
        CancelCloseTimer();
        OwnerMenu?.CancelCloseTimer();
        // Hovering a row is what drives a menu: this row's submenu opens (if it has one) and any sibling's submenu that
        // was open closes, so only one branch is ever expanded at a time.
        CloseSiblingSubmenus();
        if (HasItems) IsSubmenuOpen = true;
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        // Left this row while its submenu is open: close it shortly, UNLESS the pointer moved into the submenu (a child's
        // OnMouseEnter cancels this timer) or onto a sibling (which closes it immediately). The delay bridges the gap the
        // pointer crosses from the row to its flyout.
        if (HasItems && IsSubmenuOpen) StartCloseTimer();
    }

    private void StartCloseTimer()
    {
        _closeTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(CloseDelayMs) };
        _closeTimer.Tick -= OnCloseTick;
        _closeTimer.Tick += OnCloseTick;
        _closeTimer.Start(TimeSpan.FromMilliseconds(CloseDelayMs));
    }

    private void CancelCloseTimer() => _closeTimer?.Stop();

    private void OnCloseTick(object sender, EventArgs e)
    {
        _closeTimer.Stop();
        IsSubmenuOpen = false;
    }

    protected override void OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(sender, e);
        if (IsOnAButtonInside(e.OriginalSource as IUIComponent))
        {
            return;
        }

        if (Invoke())
        {
            e.Handled = true;
        }
    }

    private bool IsOnAButtonInside(IUIComponent pressed)
    {
        for (var node = pressed; node != null && !ReferenceEquals(node, this); node = node.VisualParent)
        {
            if (node is ButtonBase)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Enter and Space do what a click does. A menu row is not a <see cref="Primitives.ButtonBase"/> - it is an
    /// items control, because it may hold a submenu - so it inherits none of the button's key handling, and a menu the
    /// keyboard had walked into could be looked at but never used.</summary>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled || e.IsRepeated || e.Key is not (Key.Enter or Key.Space)) return;

        if (Invoke()) e.Handled = true;
    }

    /// <summary>Opens the submenu of a parent row, or runs a leaf and announces the click so the flyout closes. Shared
    /// by the pointer and the keyboard - one row, one meaning of "chosen".</summary>
    internal bool Invoke()
    {
        if (HasItems)
        {
            // A parent row: choosing it just (re)opens its submenu, it does not dismiss the menu.
            IsSubmenuOpen = true;
            return true;
        }

        if (!IsEnabled) return false;

        if (IsCheckable)
        {
            SetCurrentValue(IsCheckedProperty, !IsChecked);
        }

        if (Command != null && Command.CanExecute(CommandParameter))
            Command.Execute(CommandParameter);
        RaiseEvent(new RoutedEventArgs(ClickEvent, this) { RoutedEvent = ClickEvent });
        return true;
    }

    internal static bool KeepsMenuOpen(RoutedEventArgs e)
    {
        return e.OriginalSource is MenuItem { StaysOpenOnClick: true };
    }

    // Close the submenus of THIS item's siblings (the other rows in the same menu), so hovering across rows swaps which
    // branch is open instead of leaving a trail of open flyouts.
    private void CloseSiblingSubmenus()
    {
        if (VisualParent is not { } parent) return;
        foreach (var sibling in parent.VisualChildren)
            if (!ReferenceEquals(sibling, this) && sibling is MenuItem { IsSubmenuOpen: true } item)
                item.IsSubmenuOpen = false;
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new MenuItemAutomationPeer(this);
}
