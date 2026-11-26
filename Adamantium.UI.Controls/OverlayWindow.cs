using Adamantium.Navigation;
using Adamantium.ProceduralGeometry;
using Adamantium.UI.Controls.Automation;
using Adamantium.UI.Controls.Base;
using Adamantium.UI.Controls.Buttons;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Automation;
using Adamantium.UI.Core.Input;
using Adamantium.UI.Core.Media;
using Adamantium.UI.Core.Media.Animation;
using Adamantium.UI.Core.RoutedEvents;
using Adamantium.UI.Core.Templates;

namespace Adamantium.UI.Controls;

/// <summary>A titled card window inside its parent window, shown through an <see cref="OverlayWindowManager"/>.
/// <see cref="Close(object)"/> sets <see cref="Result"/>, which the awaited Show call returns.</summary>
public class OverlayWindow : ContentControl, IRootVisualComponent
{
    // The manager owns the show/close lifecycle, so IsOpen is read-only state a consumer can bind to.
    public static readonly AdamantiumProperty IsOpenProperty = AdamantiumProperty.RegisterReadOnly(nameof(IsOpen),
        typeof(bool), typeof(OverlayWindow), new PropertyMetadata(false));

    public static readonly AdamantiumProperty IsActiveProperty = AdamantiumProperty.RegisterReadOnly(
        nameof(IsActive), typeof(bool), typeof(OverlayWindow), new PropertyMetadata(false));

    public static readonly AdamantiumProperty TitleProperty = AdamantiumProperty.Register(nameof(Title),
        typeof(object), typeof(OverlayWindow), new PropertyMetadata(null));

    public static readonly AdamantiumProperty IconProperty = AdamantiumProperty.Register(nameof(Icon),
        typeof(object), typeof(OverlayWindow), new PropertyMetadata(null));

    public static readonly AdamantiumProperty ShowTitleBarProperty = AdamantiumProperty.Register(nameof(ShowTitleBar),
        typeof(bool), typeof(OverlayWindow), new PropertyMetadata(true));

    public static readonly AdamantiumProperty CanCloseProperty = AdamantiumProperty.Register(nameof(CanClose),
        typeof(bool), typeof(OverlayWindow), new PropertyMetadata(true));

    public static readonly AdamantiumProperty AllowMoveProperty = AdamantiumProperty.Register(nameof(AllowMove),
        typeof(bool), typeof(OverlayWindow), new PropertyMetadata(true));

    public static readonly AdamantiumProperty IsModalProperty = AdamantiumProperty.Register(nameof(IsModal),
        typeof(bool), typeof(OverlayWindow), new PropertyMetadata(false, OnIsModalChanged));

    // A modal overlay TRAPS Tab: while it is up the content behind is dimmed and unclickable, so a Tab that walked out
    // into it would put the keyboard somewhere the mouse cannot follow - focused, invisible, and with no way back
    // except more Tab. A non-modal overlay is an ordinary panel and keeps the plain order.
    private static void OnIsModalChanged(AdamantiumComponent a, AdamantiumPropertyChangedEventArgs e) =>
        KeyboardNavigation.SetTabNavigation(a, (bool)e.NewValue
            ? KeyboardNavigationMode.Cycle
            : KeyboardNavigationMode.Continue);

    public static readonly AdamantiumProperty CloseOnOverlayProperty = AdamantiumProperty.Register(nameof(CloseOnOverlay),
        typeof(bool), typeof(OverlayWindow), new PropertyMetadata(false));

    public static readonly AdamantiumProperty CloseByEscapeProperty = AdamantiumProperty.Register(nameof(CloseByEscape),
        typeof(bool), typeof(OverlayWindow), new PropertyMetadata(true));

    public static readonly AdamantiumProperty ShowActivatedProperty = AdamantiumProperty.Register(nameof(ShowActivated),
        typeof(bool), typeof(OverlayWindow), new PropertyMetadata(true));

    public static readonly AdamantiumProperty CanCollapseProperty = AdamantiumProperty.Register(nameof(CanCollapse),
        typeof(bool), typeof(OverlayWindow), new PropertyMetadata(false));

    public static readonly AdamantiumProperty IsCollapsedProperty = AdamantiumProperty.Register(nameof(IsCollapsed),
        typeof(bool), typeof(OverlayWindow), new PropertyMetadata(false, OnIsCollapsedChanged));

    public static readonly AdamantiumProperty MinimizeTemplateProperty = AdamantiumProperty.Register(nameof(MinimizeTemplate),
        typeof(DataTemplate), typeof(OverlayWindow), new PropertyMetadata(null));

    public static readonly AdamantiumProperty MaximizeTemplateProperty = AdamantiumProperty.Register(nameof(MaximizeTemplate),
        typeof(DataTemplate), typeof(OverlayWindow), new PropertyMetadata(null));

    public static readonly AdamantiumProperty CaptionButtonPlacementProperty = AdamantiumProperty.Register(
        nameof(CaptionButtonPlacement), typeof(CaptionButtonPlacement), typeof(OverlayWindow),
        new PropertyMetadata(TitleBar.SystemCaptionButtonPlacement, PropertyMetadataOptions.AffectsMeasure));

    static OverlayWindow()
    {
        // A card stands off its content; the chrome property itself is Control's, only the default differs.
        PaddingProperty.OverrideMetadata(typeof(OverlayWindow),
            new PropertyMetadata(new Thickness(16),
                PropertyMetadataOptions.AffectsMeasure | PropertyMetadataOptions.AffectsArrange));
        // The smallest a card is laid out and resized to; collapsed to its title bar, no height is kept.
        MinWidthProperty.OverrideMetadata(typeof(OverlayWindow), new PropertyMetadata(300.0));
        MinHeightProperty.OverrideMetadata(typeof(OverlayWindow), new PropertyMetadata(120.0, null,
            (a, value) => ((OverlayWindow)a).IsCollapsed ? 0.0 : value));
        // Collapsing coerces the size rather than writing it: a binding, a style or a size set meanwhile stays, and
        // expanding shows it. Collapsed, no height; the width it was shown at, if it had none of its own.
        HeightProperty.OverrideMetadata(typeof(OverlayWindow), new PropertyMetadata(double.NaN, null,
            (a, value) => ((OverlayWindow)a).IsCollapsed ? double.NaN : value));
        WidthProperty.OverrideMetadata(typeof(OverlayWindow), new PropertyMetadata(double.NaN, null,
            (a, value) => a is OverlayWindow { IsCollapsed: true, _collapsedWidth: > 0 } window && double.IsNaN((double)value)
                ? window._collapsedWidth
                : value));
    }

    public static readonly AdamantiumProperty OverlayBrushProperty = AdamantiumProperty.Register(nameof(OverlayBrush),
        typeof(Brush), typeof(OverlayWindow), new PropertyMetadata(null));

    public static readonly AdamantiumProperty TitleBarBackgroundProperty = AdamantiumProperty.Register(nameof(TitleBarBackground),
        typeof(Brush), typeof(OverlayWindow), new PropertyMetadata(Brushes.Transparent));

    public static readonly AdamantiumProperty TitleForegroundProperty = AdamantiumProperty.Register(nameof(TitleForeground),
        typeof(Brush), typeof(OverlayWindow), new PropertyMetadata(null));

    public static readonly AdamantiumProperty OpenDurationProperty = AdamantiumProperty.Register(nameof(OpenDuration),
        typeof(double), typeof(OverlayWindow), new PropertyMetadata(0.18));

    public static readonly AdamantiumProperty CanResizeProperty = AdamantiumProperty.Register(nameof(CanResize),
        typeof(bool), typeof(OverlayWindow), new PropertyMetadata(false));

    public static readonly AdamantiumProperty CanPinProperty = AdamantiumProperty.Register(nameof(CanPin),
        typeof(bool), typeof(OverlayWindow), new PropertyMetadata(false));

    public static readonly AdamantiumProperty IsPinnedProperty = AdamantiumProperty.Register(nameof(IsPinned),
        typeof(bool), typeof(OverlayWindow), new PropertyMetadata(false, OnIsPinnedChanged));

    public static readonly AdamantiumProperty LeftProperty = AdamantiumProperty.Register(nameof(Left),
        typeof(double), typeof(OverlayWindow), new PropertyMetadata(0.0, PropertyMetadataOptions.BindsTwoWayByDefault, OnPositionChanged));

    public static readonly AdamantiumProperty TopProperty = AdamantiumProperty.Register(nameof(Top),
        typeof(double), typeof(OverlayWindow), new PropertyMetadata(0.0, PropertyMetadataOptions.BindsTwoWayByDefault, OnPositionChanged));

    public static readonly AdamantiumProperty OwnerProperty = AdamantiumProperty.Register(nameof(Owner),
        typeof(UIComponent), typeof(OverlayWindow), new PropertyMetadata(null, (a, _) => ((OverlayWindow)a).FollowOwner()));

    public static readonly AdamantiumProperty StaysInsideOwnerProperty = AdamantiumProperty.Register(nameof(StaysInsideOwner),
        typeof(bool), typeof(OverlayWindow), new PropertyMetadata(true, (a, _) => ((OverlayWindow)a).FollowOwner()));

    /// <summary>True while the window is shown on the overlay. Read-only: the manager owns the lifecycle.</summary>
    public bool IsOpen
    {
        get => GetValue<bool>(IsOpenProperty);
        private set => SetReadOnlyValue(IsOpenProperty, value);
    }

    /// <summary>True when this is the front-most / last-interacted window (like <c>Window.IsActive</c>); the title bar dims
    /// when false. Read-only - the <see cref="OverlayWindowManager"/> maintains it.</summary>
    public bool IsActive
    {
        get => GetValue<bool>(IsActiveProperty);
        private set => SetReadOnlyValue(IsActiveProperty, value);
    }

    internal void SetActive(bool active) => IsActive = active;

    /// <summary>Title content shown on the left of the title bar.</summary>
    public object Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    /// <summary>Optional icon shown before the title.</summary>
    public object Icon
    {
        get => GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    public bool ShowTitleBar
    {
        get => GetValue<bool>(ShowTitleBarProperty);
        set => SetValue(ShowTitleBarProperty, value);
    }

    /// <summary>When false, the window cannot be closed by the user - the x button is hidden and Escape / an overlay
    /// click do nothing (programmatic <see cref="Close()"/> still works). Default true.</summary>
    public bool CanClose
    {
        get => GetValue<bool>(CanCloseProperty);
        set => SetValue(CanCloseProperty, value);
    }

    /// <summary>When true, a resize grip appears in the bottom-right corner. Default false.</summary>
    public bool CanResize
    {
        get => GetValue<bool>(CanResizeProperty);
        set => SetValue(CanResizeProperty, value);
    }

    /// <summary>When true, a pin button appears on the title bar; clicking it toggles <see cref="IsPinned"/>. Default false.</summary>
    public bool CanPin
    {
        get => GetValue<bool>(CanPinProperty);
        set => SetValue(CanPinProperty, value);
    }

    /// <summary>When true, the window stays above the non-pinned windows (always on top). Toggled by the pin button.</summary>
    public bool IsPinned
    {
        get => GetValue<bool>(IsPinnedProperty);
        set => SetValue(IsPinnedProperty, value);
    }

    /// <summary>When true (default) the window takes the keyboard and becomes the active one as it is shown. When false
    /// the keyboard stays where it was - a card shown under the pointer - until the window is pressed.</summary>
    public bool ShowActivated
    {
        get => GetValue<bool>(ShowActivatedProperty);
        set => SetValue(ShowActivatedProperty, value);
    }

    /// <summary>When true, a collapse button appears on the title bar; clicking it toggles <see cref="IsCollapsed"/>.
    /// Default false.</summary>
    public bool CanCollapse
    {
        get => GetValue<bool>(CanCollapseProperty);
        set => SetValue(CanCollapseProperty, value);
    }

    /// <summary>When true, only the title bar shows. Expanding gives the window back the height it had.</summary>
    public bool IsCollapsed
    {
        get => GetValue<bool>(IsCollapsedProperty);
        set => SetValue(IsCollapsedProperty, value);
    }

    /// <summary>What the collapse button shows while the content is shown: any icon; null, nothing. The theme gives a
    /// chevron.</summary>
    public DataTemplate MinimizeTemplate
    {
        get => GetValue<DataTemplate>(MinimizeTemplateProperty);
        set => SetValue(MinimizeTemplateProperty, value);
    }

    /// <summary>What the collapse button shows while collapsed - the way back to the content: any icon; null, nothing.
    /// The theme gives a chevron.</summary>
    public DataTemplate MaximizeTemplate
    {
        get => GetValue<DataTemplate>(MaximizeTemplateProperty);
        set => SetValue(MaximizeTemplateProperty, value);
    }

    /// <summary>Which side of the title bar the buttons sit on: by default where the operating system keeps a window's.
    /// </summary>
    public CaptionButtonPlacement CaptionButtonPlacement
    {
        get => GetValue<CaptionButtonPlacement>(CaptionButtonPlacementProperty);
        set => SetValue(CaptionButtonPlacementProperty, value);
    }

    /// <summary>When true (default) the title bar can be dragged to move the window within the parent.</summary>
    public bool AllowMove
    {
        get => GetValue<bool>(AllowMoveProperty);
        set => SetValue(AllowMoveProperty, value);
    }

    /// <summary>When true the dim overlay blocks input to everything behind this window (a modal dialog). Default false -
    /// a normal window that lets the ones behind it stay usable.</summary>
    public bool IsModal
    {
        get => GetValue<bool>(IsModalProperty);
        set => SetValue(IsModalProperty, value);
    }

    /// <summary>When modal, a click on the dim overlay (outside the window) closes it. Default false.</summary>
    public bool CloseOnOverlay
    {
        get => GetValue<bool>(CloseOnOverlayProperty);
        set => SetValue(CloseOnOverlayProperty, value);
    }

    /// <summary>When true (default) the Escape key closes the window while it is the front-most one.</summary>
    public bool CloseByEscape
    {
        get => GetValue<bool>(CloseByEscapeProperty);
        set => SetValue(CloseByEscapeProperty, value);
    }

    /// <summary>The dimming overlay used when <see cref="IsModal"/> is true. Defaults to the theme smoke brush.</summary>
    public Brush OverlayBrush
    {
        get => GetValue<Brush>(OverlayBrushProperty);
        set => SetValue(OverlayBrushProperty, value);
    }

    /// <summary>The title bar fill. Defaults to the theme accent brush.</summary>
    public Brush TitleBarBackground
    {
        get => GetValue<Brush>(TitleBarBackgroundProperty);
        set => SetValue(TitleBarBackgroundProperty, value);
    }

    /// <summary>The title text/icon color (readable on <see cref="TitleBarBackground"/>).</summary>
    public Brush TitleForeground
    {
        get => GetValue<Brush>(TitleForegroundProperty);
        set => SetValue(TitleForegroundProperty, value);
    }

    /// <summary>Open/close animation duration in seconds.</summary>
    public double OpenDuration
    {
        get => GetValue<double>(OpenDurationProperty);
        set => SetValue(OpenDurationProperty, value);
    }

    /// <summary>Where the window first appears - centered+cascaded, a manual top-left, or a corner of the parent window
    /// (see <see cref="OverlayStartupLocation"/>). Set before it is shown.</summary>
    public OverlayStartupLocation StartupLocation { get; set; } = OverlayStartupLocation.CenterOwner;

    /// <summary>The window's left in the parent window's coordinates (like <c>Window.Left</c>) - opened at a right corner,
    /// the room from the parent's right edge until it is dragged, resized or collapsed. Two-way bindable: dragging updates
    /// it, and assigning it (or the bound view-model property) moves the window. Used as the opening X unless
    /// <see cref="StartupLocation"/> centers it.</summary>
    public double Left
    {
        get => GetValue<double>(LeftProperty);
        set => SetValue(LeftProperty, value);
    }

    /// <summary>The window's top in the parent window's coordinates (like <c>Window.Top</c>) - opened at a bottom corner,
    /// the room from the parent's bottom edge until it is dragged, resized or collapsed. See <see cref="Left"/>.</summary>
    public double Top
    {
        get => GetValue<double>(TopProperty);
        set => SetValue(TopProperty, value);
    }

    /// <summary>The element the window belongs to - a control it shows something of, not the parent window as a whole:
    /// hidden while the element is not shown (its tab switched away, it taken out of the tree), and back where it was as
    /// the element is shown again; kept inside it while <see cref="StaysInsideOwner"/>. Null for a window of the parent
    /// window as a whole.</summary>
    public UIComponent Owner
    {
        get => GetValue<UIComponent>(OwnerProperty);
        set => SetValue(OwnerProperty, value);
    }

    /// <summary>Whether the window stays inside its <see cref="Owner"/>: dragged and resized only as far as its edges,
    /// moved in as it shrinks. Otherwise it goes anywhere in the parent window, as a window with no owner. Default true.
    /// </summary>
    public bool StaysInsideOwner
    {
        get => GetValue<bool>(StaysInsideOwnerProperty);
        set => SetValue(StaysInsideOwnerProperty, value);
    }

    /// <summary>The result set by the last <see cref="Close(object)"/> - what the awaited Show call returns.</summary>
    public object Result { get; private set; }

    /// <summary>Raised before the window closes; set <see cref="OverlayWindowClosingEventArgs.Cancel"/> to keep it open.</summary>
    public event EventHandler<OverlayWindowClosingEventArgs> Closing;

    /// <summary>Raised after the window has closed (its close animation finished). <see cref="Result"/> holds the result.</summary>
    public event EventHandler Closed;

    /// <summary>Raised when the window has been shown on the overlay.</summary>
    public event EventHandler Opened;

    /// <summary>Raised as the window is dragged by its title bar or resized by its corner - by hand, not as it is placed
    /// by its <see cref="Left"/> and <see cref="Top"/> or kept with its <see cref="Owner"/>.</summary>
    public event EventHandler PlacedByHand;

    private OverlayWindowManager _manager;
    private Button _closeButton;
    private Button _pinButton;
    private Button _collapseButton;
    private double _collapsedWidth;   // the width it was shown at as it collapsed
    private InputUIComponent _dragArea;   // the transparent title-bar strip that starts the drag
    private InputUIComponent _resizeGrip;
    private bool _dragging;
    private Vector2<double> _dragStart;           // pointer at press, in the PARENT WINDOW's space (which does not move)
    private double _dragBaseLeft, _dragBaseTop;   // the card's top-left at press (the anchor the delta is added to)
    private Rect _dragRoom;                       // where it may stand, at press (to clamp the card inside)
    private double _dragCardW, _dragCardH;        // card size at press
    private bool _resizing;
    private Vector2<double> _resizeStart;
    private double _resizeBaseW, _resizeBaseH;
    private Rect _resizeRoom;                        // where it may stand, at resize start
    private double _resizeLeft, _resizeTop;          // the card's top-left in window space at resize start (kept fixed)

    private static void OnIsPinnedChanged(AdamantiumComponent a, AdamantiumPropertyChangedEventArgs e)
    {
        if (a is OverlayWindow w) w._manager?.OnPinnedChanged(w);
    }

    // Collapsed, a height of its own would keep the room the content took; the width stays as it is shown - sized to
    // its content, the title bar alone would narrow it. Both by coercion, see the static constructor.
    private static void OnIsCollapsedChanged(AdamantiumComponent a, AdamantiumPropertyChangedEventArgs e)
    {
        var window = (OverlayWindow)a;
        // Only where the new height would move the title bar - centered, or kept to a bottom corner.
        if (window.HostPopup is { Placement: PlacementMode.Center }
            or { RelativeCorner: PlacementCorner.BottomLeft or PlacementCorner.BottomRight })
        {
            window.KeepTopLeft();
        }

        window._collapsedWidth = (bool)e.NewValue ? window.ActualWidth : 0;
        window.CoerceValue(HeightProperty);
        window.CoerceValue(WidthProperty);
        window.CoerceValue(MinHeightProperty);
    }

    // Left/Top are the card's place: the room from the corner it opened at, until it is dragged, resized or collapsed, and
    // its top-left from then on (KeepTopLeft). A change (from a drag write, an assignment, or a two-way binding to the
    // view model) moves the popup there. Skipped before the popup exists - the manager applies the initial placement.
    private static void OnPositionChanged(AdamantiumComponent a, AdamantiumPropertyChangedEventArgs e)
    {
        if (a is not OverlayWindow w) return;

        // Placed before it is shown: there, not kept as far from its owner as it was when last shown.
        if (w.HostPopup == null) w._ownerRoom = default;
        w.ApplyPositionToPopup();
    }

    // A centered card would re-center on its new size, its title bar jumping from under the pointer, and one kept to a
    // right or bottom corner would grow or shrink from that corner: placed by its top-left where it is instead, once it
    // is dragged, resized or collapsed. Not yet arranged, it is drawn nowhere and has no place to keep.
    private void KeepTopLeft()
    {
        if (HostPopup is not ({ Placement: PlacementMode.Center } or { RelativeCorner: not PlacementCorner.TopLeft })
            || RenderSize.Width <= 0)
        {
            return;
        }

        var at = WorldTransform.TranslationVector;
        HostPopup.RelativeCorner = PlacementCorner.TopLeft;
        SetCurrentValue(LeftProperty, (double)at.X);
        SetCurrentValue(TopProperty, (double)at.Y);
        ApplyPositionToPopup();   // also when Left/Top already held these and raised no change
    }

    private void ApplyPositionToPopup()
    {
        if (HostPopup == null) return;
        HostPopup.Placement = PlacementMode.Relative;   // Relative anchors the card's corner at (offset), not centered
        HostPopup.HorizontalOffset = Left;
        HostPopup.VerticalOffset = Top;
    }

    // --- the root of its own tree, shown in the window that holds it: the layer lays it out there (Bounds), and what
    //     needs the real window - its scale, its input method - is sent on to it ---

    private IWindow _window;
    private IUIContext _context;

    IWindow IRootVisualComponent.Window => _window;

    string IRootVisualComponent.Title
    {
        get => Title?.ToString();
        set => Title = value;
    }

    double IRootVisualComponent.ClientWidth
    {
        get => Bounds.Width;
        set => Width = value;
    }

    double IRootVisualComponent.ClientHeight
    {
        get => Bounds.Height;
        set => Height = value;
    }

    IUIContext IRootVisualComponent.UIContext => _context ?? _window?.UIContext;

    void IRootVisualComponent.AttachContextAndInitialize(IUIContext context) => _context = context;

    PixelPoint IRootVisualComponent.Position
    {
        get
        {
            var scale = WindowScale();
            return new PixelPoint(Bounds.X * scale.X, Bounds.Y * scale.Y);
        }
        set
        {
            var scale = WindowScale();
            Left = value.X / scale.X;
            Top = value.Y / scale.Y;
        }
    }

    // Its content is laid out in the window's own coordinates, where the layer places the card - so its points are the
    // window's.
    Vector2<double> IRootVisualComponent.PointToClient(PixelPoint point) => _window?.PointToClient(point) ?? default;

    PixelPoint IRootVisualComponent.PointToScreen(Vector2<double> point) => _window?.PointToScreen(point) ?? default;

    private Vector2<double> WindowScale() => _window?.DpiScale ?? new Vector2<double>(1, 1);

    // The overlay layer hosts each window as its OWN card-sized popup (like a menu/tooltip), so a click outside the card
    // falls through to the content behind - a full-window host popup would absorb every click (see OverlayWindowManager).
    // The card is positioned by this popup's offsets (cascade + drag); the manager owns the popup lifecycle.
    internal Popup HostPopup
    {
        get => _hostPopup;
        set
        {
            // The owner's room is kept: shown again over another window - its owner taken there - it is as far from the
            // owner as it was.
            if (_hostPopup != null) _hostPopup.LayerPass -= OnLayerPass;
            _hostPopup = value;
            if (_hostPopup != null) _hostPopup.LayerPass += OnLayerPass;
        }
    }

    internal Popup ScrimPopup { get; set; }   // the dim backdrop popup, only when this window is modal

    private Popup _hostPopup;
    // Hidden as its owner was not shown: shown again as the owner is - not if hidden by anyone else.
    private bool _hiddenWithOwner;
    // The owner's bounds as the window was last kept with it, in the parent window's coordinates, and the window's own
    // size then.
    private Rect _ownerRoom;
    private Size<double> _keptSize;

    private void FollowOwner()
    {
        _ownerRoom = default;
        if (HostPopup != null) OnLayerPass(null, EventArgs.Empty);
    }

    // Each pass of the layer, before it lays the window out - a hidden one too: hidden while its owner is not shown here,
    // no longer the active window, the keyboard taken out of it; kept with the owner as it moves or shrinks.
    private void OnLayerPass(object sender, EventArgs e)
    {
        var owner = Owner;
        var shown = owner == null || IsShownHere(owner);
        if (!shown && !_hiddenWithOwner && Visibility == Visibility.Visible)
        {
            _hiddenWithOwner = true;
            // Nowhere seen to type into: not the owner, which is not shown either.
            if (HasKeyboard()) FocusManager.Focus(null);
            _manager?.LetGoOf(this);
            SetCurrentValue(VisibilityProperty, Visibility.Collapsed);
            // A modal one's dim goes with it: nothing behind is held by a dialog not seen.
            (ScrimPopup?.ChildValue as UIComponent)?.SetCurrentValue(VisibilityProperty, Visibility.Collapsed);
        }
        else if (shown && _hiddenWithOwner)
        {
            _hiddenWithOwner = false;
            SetCurrentValue(VisibilityProperty, Visibility.Visible);
            (ScrimPopup?.ChildValue as UIComponent)?.SetCurrentValue(VisibilityProperty, Visibility.Visible);
        }

        if (shown)
        {
            KeepWithOwner();
        }
    }

    // In the tree of the window this one is shown over, and seen there: none of the elements it is in collapsed or
    // hidden - its tab not switched away, not taken to another window.
    private bool IsShownHere(UIComponent element)
    {
        if (!element.IsAttachedToVisualTree) return false;
        IUIComponent last = null;
        for (IUIComponent node = element; node != null; node = node.VisualParent)
        {
            if (node.Visibility != Visibility.Visible) return false;
            last = node;
        }

        return _window == null || ReferenceEquals(last, _window) || ReferenceEquals(Popup.HostOf(element), _window);
    }

    private bool HasKeyboard()
    {
        for (var node = FocusManager.Focused as IUIComponent; node != null; node = node.VisualParent)
        {
            if (ReferenceEquals(node, this)) return true;
        }

        return false;
    }

    // Where it may stand, in the parent window's coordinates: its owner's bounds while it stays inside the owner, else the
    // parent window's.
    private Rect Room(IInputComponent frame)
    {
        if (Owner is { RenderSize: { Width: > 0, Height: > 0 } size } owner && StaysInsideOwner)
        {
            var at = owner.WorldTransform.TranslationVector;
            return new Rect(at.X, at.Y, size.Width, size.Height);
        }

        return new Rect(0, 0, frame.RenderSize.Width, frame.RenderSize.Height);
    }

    // The owner moved: the window goes with it, as far from it as it was. Staying inside it, the window is moved in - and
    // made smaller where it is larger - as the owner shrinks, or as the window grows, unfolded.
    private void KeepWithOwner()
    {
        if (Owner is not { RenderSize: { Width: > 0, Height: > 0 } ownerSize } owner || RenderSize.Width <= 0)
        {
            return;
        }

        var ownerAt = owner.WorldTransform.TranslationVector;
        var room = new Rect(ownerAt.X, ownerAt.Y, ownerSize.Width, ownerSize.Height);
        if (room == _ownerRoom && RenderSize == _keptSize) return;

        var at = WorldTransform.TranslationVector;
        var moved = _ownerRoom.Width > 0
            ? new Vector2<double>(room.X - _ownerRoom.X, room.Y - _ownerRoom.Y)
            : default;
        _ownerRoom = room;
        _keptSize = RenderSize;
        var left = at.X + moved.X;
        var top = at.Y + moved.Y;
        var width = RenderSize.Width;
        var height = RenderSize.Height;
        if (StaysInsideOwner)
        {
            width = Math.Min(width, Math.Max(MinWidth, room.Width));
            height = Math.Min(height, Math.Max(MinHeight, room.Height));
            if (width < RenderSize.Width) SetCurrentValue(WidthProperty, width);
            if (height < RenderSize.Height && !IsCollapsed) Height = height;
            left = Within(left, room.X, room.X + room.Width - width);
            top = Within(top, room.Y, room.Y + room.Height - height);
        }

        if (left == at.X && top == at.Y) return;
        KeepTopLeft();
        SetCurrentValue(LeftProperty, left);
        SetCurrentValue(TopProperty, top);
    }

    // Between low and high - at low where the room is smaller than what is put in it.
    private static double Within(double value, double low, double high) => Math.Max(low, Math.Min(value, high));

    public OverlayWindow()
    {
        // Any press within the window raises it to the front (like clicking a desktop window), the ones a button or a
        // field inside it took included.
        AddHandler(PointerEvents.PointerPressedEvent, new PointerPressedEventHandler((_, e) =>
        {
            if (e.ChangedButton == MouseButtons.Left) _manager?.BringToFront(this);
        }), handledEventsToo: true);
        // A window is a place the keyboard can be sent to as a whole: Ctrl+Tab steps between it, the other overlays and
        // the page behind, and comes back to where the keyboard was in each. A MODAL one is exempt by its own Tab trap.
        KeyboardNavigation.SetIsFocusArea(this, true);
    }

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();

        if (_closeButton != null) _closeButton.Click -= OnCloseClick;
        if (_pinButton != null) _pinButton.Click -= OnPinClick;
        if (_collapseButton != null) _collapseButton.Click -= OnCollapseClick;
        if (_dragArea != null)
        {
            _dragArea.PointerPressed -= OnDragPress;
            _dragArea.PointerMoved -= OnDragMove;
            _dragArea.PointerReleased -= OnDragRelease;
        }
        if (_resizeGrip != null)
        {
            _resizeGrip.PointerPressed -= OnResizePress;
            _resizeGrip.PointerMoved -= OnResizeMove;
            _resizeGrip.PointerReleased -= OnResizeRelease;
        }

        _dragArea = GetTemplateChild("PART_TitleThumb") as InputUIComponent;
        _closeButton = GetTemplateChild("PART_CloseButton") as Button;
        _pinButton = GetTemplateChild("PART_PinButton") as Button;
        _collapseButton = GetTemplateChild("PART_CollapseButton") as Button;
        _resizeGrip = GetTemplateChild("PART_ResizeGrip") as InputUIComponent;

        if (_closeButton != null) _closeButton.Click += OnCloseClick;
        if (_pinButton != null) _pinButton.Click += OnPinClick;
        if (_collapseButton != null) _collapseButton.Click += OnCollapseClick;
        if (_dragArea != null)
        {
            _dragArea.PointerPressed += OnDragPress;
            _dragArea.PointerMoved += OnDragMove;
            _dragArea.PointerReleased += OnDragRelease;
        }
        if (_resizeGrip != null)
        {
            _resizeGrip.Cursor = Cursors.SizeNWSE;   // diagonal resize cursor on hover, so the grip reads as resizable
            _resizeGrip.PointerPressed += OnResizePress;
            _resizeGrip.PointerMoved += OnResizeMove;
            _resizeGrip.PointerReleased += OnResizeRelease;
        }
    }

    /// <summary>Let the template's parts go when the template does - see ScrollBar.OnRemoveTemplate. The guarded
    /// unhooks at the top of OnApplyTemplate cover a template that is REPLACED; this covers one that is dropped.</summary>
    public override void OnRemoveTemplate()
    {
        base.OnRemoveTemplate();
        if (_closeButton != null) _closeButton.Click -= OnCloseClick;
        if (_pinButton != null) _pinButton.Click -= OnPinClick;
        if (_collapseButton != null) _collapseButton.Click -= OnCollapseClick;
        if (_dragArea != null)
        {
            _dragArea.PointerPressed -= OnDragPress;
            _dragArea.PointerMoved -= OnDragMove;
            _dragArea.PointerReleased -= OnDragRelease;
        }
        if (_resizeGrip != null)
        {
            _resizeGrip.PointerPressed -= OnResizePress;
            _resizeGrip.PointerMoved -= OnResizeMove;
            _resizeGrip.PointerReleased -= OnResizeRelease;
        }
        _closeButton = null;
        _pinButton = null;
        _collapseButton = null;
        _dragArea = null;
        _resizeGrip = null;
    }

    /// <summary>Closes the window with no result (same as the x button).</summary>
    public void Close() => Close(null);

    /// <summary>Closes the window, setting <see cref="Result"/>, unless a <see cref="Closing"/> handler cancels it.</summary>
    public void Close(object result)
    {
        if (!IsOpen) return;
        var args = new OverlayWindowClosingEventArgs(result);
        Closing?.Invoke(this, args);
        if (args.Cancel) return;

        Result = result;
        IsOpen = false;
        Closed?.Invoke(this, EventArgs.Empty);   // the manager listens on Closed to pull it off the overlay
    }

    // --- called by the manager ---

    internal void NotifyShown(OverlayWindowManager manager, IPopupHost host)
    {
        _manager = manager;
        _window = host as IWindow;
        IsOpen = true;

        // A subtle scale-in "pop". The transform promotes the card to a compositor motion node while it plays; the compositor
        // now un-promotes it on completion (Compositor.Release), so the card returns to a static, world-baked node afterwards
        // instead of full-re-recording every frame. FillBehavior.Stop clears the scale back to 1 at the end.
        if (OpenDuration > 0)
        {
            var pop = RenderTransform ?? new Transform();
            RenderTransform = pop;
            RenderTransformOrigin = new Vector2<double>(0.5f, 0.5f);   // scale about the center
            var duration = TimeSpan.FromSeconds(OpenDuration);
            pop.BeginAnimation(Transform.ScaleXProperty, new DoubleAnimation { From = 0.85, To = 1, Duration = duration, FillBehavior = FillBehavior.Stop });
            pop.BeginAnimation(Transform.ScaleYProperty, new DoubleAnimation { From = 0.85, To = 1, Duration = duration, FillBehavior = FillBehavior.Stop });
        }

        // An opening window takes focus, so Tab and Escape reach it. Content may not be built yet, so retry over the next
        // few layout passes. One shown not activated leaves the keyboard where it is.
        _focusTries = ShowActivated ? FocusAttempts : 0;

        Opened?.Invoke(this, EventArgs.Empty);
    }

    private const int FocusAttempts = 8;
    private int _focusTries;

    protected override Size<double> ArrangeOverride(Size<double> finalSize)
    {
        var size = base.ArrangeOverride(finalSize);
        TakeKeyboard();
        return size;
    }

    /// <summary>Take the keyboard back - what the window BEHIND a closing one does. Immediately, since this window is
    /// already built and laid out, with the same retry armed behind it in case it is not.</summary>
    internal void TakeKeyboardBack()
    {
        _focusTries = FocusAttempts;
        TakeKeyboard();
    }

    private void TakeKeyboard()
    {
        if (_focusTries <= 0) return;
        _focusTries--;

        // Already in here (the content focused itself, or the person clicked something): leave it alone.
        for (IUIComponent node = FocusManager.Focused as IUIComponent; node != null; node = node.VisualParent)
        {
            if (!ReferenceEquals(node, this)) continue;
            _focusTries = 0;
            return;
        }

        if (KeyboardNavigation.MoveInto(this)) _focusTries = 0;
    }

    // --- input ---

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    // Drag moves the window by writing Left/Top (its absolute top-left). The delta is measured in the PARENT WINDOW's space
    // (a fixed frame) - NOT the card's, which moves as we drag it. The base is the card's real top-left, so it works whether
    // the window was centered or explicitly placed; the first drag of a centered window switches it to explicit placement at
    // its current spot, so it doesn't jump.
    private void OnDragPress(object sender, PointerPressedEventArgs e)
    {
        if (e.ChangedButton != MouseButtons.Left || !AllowMove || HostPopup?.PlacementTarget is not IInputComponent frame)
        {
            return;
        }

        _dragging = true;
        e.Handled = true;
        _dragStart = e.GetPosition(frame);
        _dragBaseLeft = WorldTransform.TranslationVector.X;
        _dragBaseTop = WorldTransform.TranslationVector.Y;
        _dragRoom = Room(frame);
        _dragCardW = RenderSize.Width;
        _dragCardH = RenderSize.Height;
        e.Pointer.Capture(_dragArea);
        _manager?.BringToFront(this);
    }

    private void OnDragMove(object sender, PointerEventArgs e)
    {
        if (!_dragging || HostPopup?.PlacementTarget is not IInputComponent frame) return;
        var delta = e.GetPosition(frame) - _dragStart;
        if (delta.X == 0 && delta.Y == 0) return;   // a press alone keeps a card at its corner

        KeepTopLeft();
        // SetCurrentValue (not the CLR setter) so a two-way binding forwards Left/Top to the view model instead of being
        // masked by a Local write; OnPositionChanged then moves the popup. Clamp the top-left inside where it may stand.
        var x = Within(_dragBaseLeft + delta.X, _dragRoom.X, _dragRoom.X + _dragRoom.Width - _dragCardW);
        var y = Within(_dragBaseTop + delta.Y, _dragRoom.Y, _dragRoom.Y + _dragRoom.Height - _dragCardH);
        SetCurrentValue(LeftProperty, x);
        SetCurrentValue(TopProperty, y);
        PlacedByHand?.Invoke(this, EventArgs.Empty);
    }

    private void OnDragRelease(object sender, PointerReleasedEventArgs e)
    {
        if (!_dragging || e.ChangedButton != MouseButtons.Left)
        {
            return;
        }

        _dragging = false;
        e.Handled = true;
        if (ReferenceEquals(e.Pointer.Captured, _dragArea))
        {
            e.Pointer.Capture(null);
        }
    }

    private void OnPinClick(object sender, RoutedEventArgs e) => IsPinned = !IsPinned;

    private void OnCollapseClick(object sender, RoutedEventArgs e) =>
        SetCurrentValue(IsCollapsedProperty, !IsCollapsed);   // a view model bound to it stays bound

    // Resize drags the bottom-right grip, the top-left staying put: a card centered or kept to another corner is placed by
    // its top-left on the first move, or growing it would move that corner too.
    private void OnResizePress(object sender, PointerPressedEventArgs e)
    {
        if (e.ChangedButton != MouseButtons.Left || !CanResize || HostPopup?.PlacementTarget is not IInputComponent frame)
        {
            return;
        }

        _resizing = true;
        e.Handled = true;
        _resizeStart = e.GetPosition(frame);
        _resizeBaseW = double.IsNaN(Width) ? RenderSize.Width : Width;
        _resizeBaseH = double.IsNaN(Height) ? RenderSize.Height : Height;
        _resizeRoom = Room(frame);
        _resizeLeft = WorldTransform.TranslationVector.X;   // the card's current top-left in window space (stays fixed)
        _resizeTop = WorldTransform.TranslationVector.Y;
        e.Pointer.Capture(_resizeGrip);
        _manager?.BringToFront(this);
    }

    private void OnResizeMove(object sender, PointerEventArgs e)
    {
        if (!_resizing || HostPopup?.PlacementTarget is not IInputComponent frame) return;
        var delta = e.GetPosition(frame) - _resizeStart;
        if (delta.X == 0 && delta.Y == 0) return;   // a press alone keeps a card at its corner

        KeepTopLeft();
        SizeCard(_resizeBaseW + delta.X, _resizeBaseH + delta.Y, _resizeLeft, _resizeTop, _resizeRoom);
        PlacedByHand?.Invoke(this, EventArgs.Empty);
    }

    private void SizeCard(double width, double height, double left, double top, Rect room)
    {
        // Grow from the fixed top-left; clamp the bottom-right so it can't leave where it may stand (the grip stops at the
        // edge instead of the opposite side stretching), and not below the minimum.
        var maxW = Math.Max(MinWidth, room.X + room.Width - left);
        var maxH = Math.Max(MinHeight, room.Y + room.Height - top);
        var newW = Math.Clamp(width, MinWidth, maxW);
        var newH = Math.Clamp(height, MinHeight, maxH);
        SetCurrentValue(WidthProperty, newW);
        Height = newH;

        // Placed by its top-left before the resize (KeepTopLeft), the card's offset IS its top-left.
        HostPopup.HorizontalOffset = left;
        HostPopup.VerticalOffset = top;
    }

    /// <summary>Moves the card by an offset of the window it is shown over, kept inside where it may stand - what a drag
    /// by its title bar does.</summary>
    internal void MoveCard(Vector2<double> offset)
    {
        if (HostPopup?.PlacementTarget is not IInputComponent frame) return;

        // From where the card IS, as a drag starts: a centered card's Left is not its place until it has been moved once.
        KeepTopLeft();
        var at = WorldTransform.TranslationVector;
        var room = Room(frame);
        SetCurrentValue(LeftProperty, Within(at.X + offset.X, room.X, room.X + room.Width - RenderSize.Width));
        SetCurrentValue(TopProperty, Within(at.Y + offset.Y, room.Y, room.Y + room.Height - RenderSize.Height));
        PlacedByHand?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Resizes the card from its top-left, kept inside where it may stand - what its grip does.</summary>
    internal void ResizeCard(double width, double height)
    {
        if (HostPopup?.PlacementTarget is not IInputComponent frame) return;

        KeepTopLeft();
        var at = WorldTransform.TranslationVector;
        SizeCard(width, height, at.X, at.Y, Room(frame));
        PlacedByHand?.Invoke(this, EventArgs.Empty);
    }

    private void OnResizeRelease(object sender, PointerReleasedEventArgs e)
    {
        if (!_resizing || e.ChangedButton != MouseButtons.Left)
        {
            return;
        }

        _resizing = false;
        e.Handled = true;
        if (ReferenceEquals(e.Pointer.Captured, _resizeGrip))
        {
            e.Pointer.Capture(null);
        }
    }

    // The root of its tree, so the keys left over in it end here, as a window's do: Enter presses its default button,
    // Escape its cancel one - or, with none, closes it. Escape does neither while it may not close by it.
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled || (e.Key == Key.Escape && !(CloseByEscape && CanClose)) || Button.PressDefaultOrCancel(this, e))
        {
            return;
        }

        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            Close();
        }
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new OverlayWindowAutomationPeer(this);
}
