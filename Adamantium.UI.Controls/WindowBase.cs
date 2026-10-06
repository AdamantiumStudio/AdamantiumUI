using Adamantium.Graphics.Core;
using Adamantium.Graphics.Core.Presentation;
using Adamantium.UI.Controls.Adorners;
using Adamantium.UI.Controls.Automation;
using Adamantium.UI.Controls.Base;
using Adamantium.UI.Controls.Buttons;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Automation;
using Adamantium.UI.Core.Controls;
using Adamantium.UI.Core.Graphics;
using Adamantium.UI.Controls.Text;
using Adamantium.UI.Core.Input;
using Adamantium.UI.Core.Localization;
using Adamantium.UI.Core.Media;
using Adamantium.UI.Core.Resources;
using Adamantium.UI.Core.RoutedEvents;
using Adamantium.Win32;

namespace Adamantium.UI.Controls;

public abstract class WindowBase : ContentControl, IWindow, IWindowInternals, IAdornerHost, IPopupHost
{
    private IWindowRenderer _renderer;
    private protected bool _opensMaximized;
    protected IWindowWorkerService WindowWorkerService { get; private set; }

    public IWindowRenderer DefaultRenderer { get; set; }

    public IWindowRenderer Renderer
    {
        get => _renderer;
        set
        {
            if (value != _renderer)
            {
                OnRendererChanged(_renderer, value);
                _renderer = value;
            }
        }
    }

    private void OnRendererChanged(IWindowRenderer oldRenderer, IWindowRenderer newRenderer)
    {
        var args = new WindowRendererChangedEventArgs(oldRenderer, newRenderer);
        RendererChanged?.Invoke(this, args);
    }

    /// <summary>The window's adorner layer: tooling overlays (selection frames etc.) the renderer draws on top of the
    /// content. The WPF analog of the per-window AdornerLayer; add adorners here to have them rendered.</summary>
    public AdornerLayer AdornerLayer { get; } = new AdornerLayer();

    public IReadOnlyList<IUIComponent> Adorners => AdornerLayer.Adorners;

    /// <summary>The window's popup layer: open <see cref="Popup"/>s' children the renderer draws on top of the content,
    /// within the window. A popup registers itself here (via <see cref="IPopupHost"/>) while open. The layer is told which
    /// window owns it, so what it hosts can find the way back out - see PopupLayer.Owner.</summary>
    public PopupLayer PopupLayer { get; }

    protected WindowBase()
    {
        PopupLayer = new PopupLayer { Owner = this };
        // The window's content is a focus AREA, so Ctrl+Tab can leave a non-modal overlay for the page behind it and
        // come back to where the keyboard was - the overlays declare themselves areas too. See KeyboardNavigation.
        KeyboardNavigation.SetIsFocusArea(this, true);
    }

    public IReadOnlyList<IUIComponent> PopupRoots => PopupLayer.Roots;

    public void LayoutPopups() => PopupLayer.UpdateLayout(new Size(ClientWidth, ClientHeight));

    private bool _hoverHooked;

    /// <summary>Hover is a statement about what is under the cursor NOW, so it has to be re-decided when the CONTENT
    /// moves and the pointer does not: a list scrolled by the keyboard or the wheel slides its rows under a still
    /// cursor, and no Enter or Leave is ever sent. The window is where this belongs - it owns the tree the answer is
    /// hit-tested against - and a settled layout is exactly the moment the answer can have changed.</summary>
    private void HookHoverRefresh()
    {
        if (_hoverHooked) return;

        _hoverHooked = true;
        LayoutManager.GetOrCreate(this).LayoutUpdated += (_, _) => MouseDevice.CurrentDevice.RefreshMouseOver(this);
    }

    // Default/cancel routing: the window is the root, so unhandled Enter/Esc reach it last, after everything on the way
    // up has had its say. Enter activates the IsDefault button, Escape the IsCancel one - unless the focused element
    // already claimed the key. WPF Window default/cancel behavior.
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled) return;
        var target = e.Key switch
        {
            Key.Enter => FindButton(this, b => b.IsDefault),
            Key.Escape => FindButton(this, b => b.IsCancel),
            _ => null
        };
        if (target != null)
        {
            target.PerformClick();
            e.Handled = true;
            return;
        }

        // Escape that nobody handled on the way up clears keyboard focus, the only way to drop the focus ring.
        if (e.Key == Key.Escape && FocusManager.Focused != null)
        {
            FocusManager.Release(FocusManager.Focused);
            e.Handled = true;
            return;
        }

        // PageUp/PageDown/Home/End nobody handled scroll the view here, since a ScrollViewer is not focusable. Space is
        // left alone: it activates controls.
        var scrolled = e.Key switch
        {
            Key.PageDown => ScrollNearest(v => v.PageVertically(false)),
            Key.PageUp => ScrollNearest(v => v.PageVertically(true)),
            Key.Home => ScrollNearest(v => v.ScrollToVerticalEdge(true)),
            Key.End => ScrollNearest(v => v.ScrollToVerticalEdge(false)),
            _ => false
        };
        if (scrolled) e.Handled = true;
    }

    // The viewer a reading key means: the one the focus is inside (innermost first - a list inside a page scrolls
    // itself), else the first on screen with somewhere to go, which is what "the page" means when the keyboard is
    // nowhere in particular. The action returns false when that viewer cannot move, and the search goes on.
    private bool ScrollNearest(Func<ScrollViewer, bool> scroll)
    {
        for (var node = FocusManager.Focused as IUIComponent; node != null; node = node.VisualParent)
            if (node is ScrollViewer focused && scroll(focused)) return true;

        return ScrollFirstScrollable(this, scroll);
    }

    private static bool ScrollFirstScrollable(IUIComponent root, Func<ScrollViewer, bool> scroll)
    {
        foreach (var child in root.VisualChildren)
        {
            if (child is ScrollViewer viewer && scroll(viewer)) return true;
            if (ScrollFirstScrollable(child, scroll)) return true;
        }
        return false;
    }

    private static Button FindButton(IUIComponent root, Func<Button, bool> match)
    {
        foreach (var child in root.VisualChildren)
        {
            if (child is Button button && match(button)) return button;
            var found = FindButton(child, match);
            if (found != null) return found;
        }
        return null;
    }

    public static readonly RoutedEvent ClientSizeChangedEvent = EventManager.RegisterRoutedEvent("ClientSizeChanged",
        RoutingStrategy.Direct, typeof(SizeChangedEventHandler), typeof(WindowBase));

    public static readonly RoutedEvent MSAALevelChangedEvent = EventManager.RegisterRoutedEvent("MSAALevelChanged",
        RoutingStrategy.Direct, typeof(MSAALeveChangedHandler), typeof(WindowBase));
        
    public static readonly RoutedEvent StateChangedEvent = EventManager.RegisterRoutedEvent("StateChanged",
        RoutingStrategy.Direct, typeof(StateChangedHandler), typeof(WindowBase));


    // Left/Top MOVE the window - they are not just remembered. Without the callback, assigning them changed a managed
    // number and the window stayed where it was, which is not what a window API means anywhere.
    public static readonly AdamantiumProperty LeftProperty = AdamantiumProperty.Register(nameof(Left),
        typeof(Double), typeof(WindowBase), new PropertyMetadata(0d, PositionChangedCallback));

    public static readonly AdamantiumProperty TopProperty = AdamantiumProperty.Register(nameof(Top),
        typeof(Double), typeof(WindowBase), new PropertyMetadata(0d, PositionChangedCallback));

    // The window's place on the desktop as two plain ints, written by the platform the moment it hears about a move and
    // read by the render thread without a lock. Two independent reads can in principle catch one axis of an older
    // position, and that is deliberate: a half-updated position is one pixel wrong for one frame, where taking a lock on
    // a path the renderer walks every frame would cost far more than it saves.
    private volatile int _liveX;
    private volatile int _liveY;
    private volatile bool _liveKnown;

    /// <inheritdoc/>
    /// <remarks>Falls back to <see cref="Position"/> until the platform has reported a position at least once -
    /// otherwise a window placed by the application, and never moved by the user, would report the origin.</remarks>
    public PixelPoint LivePosition
    {
        get
        {
            // ASKED, not remembered. Move notifications arrive with the mouse - measured at about 220 a second - while
            // frames are built two to three times as often, so a remembered position is already stale for most frames.
            // The platform answers this straight from the OS; the remembered value is the fallback where it cannot.
            var asked = LivePositionProvider?.Invoke();
            if (asked.HasValue) return asked.Value;

            return _liveKnown ? new PixelPoint(_liveX, _liveY) : Position;
        }
    }

    /// <summary>Set by the platform to report where the OS has this window RIGHT NOW, callable from any thread. Null
    /// where a platform has no cheap way to ask, and the position reported by its last move message is used instead.</summary>
    public Func<PixelPoint?> LivePositionProvider { get; set; }

    /// <summary>Whether the user is dragging this window right now. Written by the message thread, read by the render
    /// thread. Anything anchored to the DESKTOP cannot be drawn correctly during a drag - see RenderCache.WindowOnDesktop.
    /// </summary>
    public bool IsBeingMoved
    {
        get => System.Threading.Volatile.Read(ref _isBeingMoved);
        set => System.Threading.Volatile.Write(ref _isBeingMoved, value);
    }

    private bool _isBeingMoved;

    /// <summary>Record where the OS just put this window, from whatever thread it said so on. Called by the platform
    /// ahead of the queued property update - see <see cref="LivePosition"/> for why the two are separate.</summary>
    public void UpdateLivePosition(double left, double top)
    {
        _liveX = (int)left;
        _liveY = (int)top;
        _liveKnown = true;
    }

    private static void PositionChangedCallback(AdamantiumComponent a, AdamantiumPropertyChangedEventArgs e)
    {
        // Before the OS window exists the value is simply remembered - it is read when the window is created.
        if (a is WindowBase window && !window._positionFromPlatform)
        {
            window.WindowWorkerService?.SetPosition(window.Left, window.Top);
        }
    }

    private bool _positionFromPlatform;

    /// <summary>Records a position the platform moved the window to (drag, snap), without moving it again, so Left/Top
    /// stay true.</summary>
    public void UpdatePositionFromPlatform(double left, double top)
    {
        // Recorded FIRST, and without going near the property system: this is the copy the render thread reads, and it
        // has to be current now rather than whenever the loop thread gets to the queued update below. See LivePosition.
        UpdateLivePosition(left, top);

        if (Left.Equals(left) && Top.Equals(top)) return;

        _positionFromPlatform = true;
        try
        {
            Left = left;
            Top = top;
        }
        finally
        {
            _positionFromPlatform = false;
        }
    }
        
    public static readonly AdamantiumProperty TitleProperty = AdamantiumProperty.Register(nameof(Title),
        typeof(String), typeof(WindowBase), new PropertyMetadata(String.Empty, TitleChangedCallback));

    public static readonly AdamantiumProperty ClientWidthProperty = AdamantiumProperty.Register(nameof(ClientWidth),
        typeof(Double), typeof(WindowBase),
        new PropertyMetadata(Double.NaN,
            PropertyMetadataOptions.BindsTwoWayByDefault | PropertyMetadataOptions.AffectsMeasure |
            PropertyMetadataOptions.AffectsRender, ClientWidthChangedCallBack));

    public static readonly AdamantiumProperty ClientHeightProperty = AdamantiumProperty.Register(nameof(ClientHeight),
        typeof(Double), typeof(WindowBase),
        new PropertyMetadata(Double.NaN,
            PropertyMetadataOptions.BindsTwoWayByDefault | PropertyMetadataOptions.AffectsMeasure |
            PropertyMetadataOptions.AffectsRender, ClientHeightChangedCallBack));
        
    public static readonly AdamantiumProperty MSAALevelProperty = AdamantiumProperty.Register(nameof(MSAALevel),
        typeof(MSAALevel), typeof(WindowBase),
        new PropertyMetadata(MSAALevel.None, PropertyMetadataOptions.AffectsRender, MSAALevelChangedCallback));

    // Live toggle for the GPU analytic AA (fill coverage fringe + feathered strokes), independent of MSAALevel so both
    // can be A/B-compared. AffectsRender re-renders on change; the render path reads it each frame (no rebuild needed).
    public static readonly AdamantiumProperty AnalyticAntialiasingProperty = AdamantiumProperty.Register(nameof(AnalyticAntialiasing),
        typeof(bool), typeof(WindowBase),
        new PropertyMetadata(true, PropertyMetadataOptions.AffectsRender));

    public static readonly AdamantiumProperty StateProperty = AdamantiumProperty.Register(nameof(State),
        typeof(WindowState), typeof(WindowBase),
        new PropertyMetadata(WindowState.Normal, PropertyMetadataOptions.AffectsRender, StateChangedCallback));

    // Modern borderless chrome ON by default: the OS frame is removed and the window draws its own title bar (see the
    // Window ControlTemplate). Read once by the platform worker at create time (the native frame styles are fixed then),
    // so flip it in markup/ctor before the window is shown.
    public static readonly AdamantiumProperty UseCustomChromeProperty = AdamantiumProperty.Register(nameof(UseCustomChrome),
        typeof(bool), typeof(WindowBase), new PropertyMetadata(true));

    // --- Overlay window traits ------------------------------------------------------------------------------------
    // Topmost, click-through, non-activating and transparent; read once at window creation, when native styles are fixed.

    // Each of these re-applies itself to the LIVE window, so they behave as properties rather than as arguments that
    // only matter before the window exists.
    private static void OverlayTraitChanged(AdamantiumComponent component, AdamantiumPropertyChangedEventArgs e)
    {
        (component as WindowBase)?.WindowWorkerService?.UpdateOverlayTraits();
    }

    /// <summary>Stays above other windows.</summary>
    public static readonly AdamantiumProperty TopmostProperty = AdamantiumProperty.Register(nameof(Topmost),
        typeof(bool), typeof(WindowBase), new PropertyMetadata(false, OverlayTraitChanged));

    /// <summary>Clicks pass straight through to whatever is behind. The window is seen and never touched.</summary>
    public static readonly AdamantiumProperty TransparentToInputProperty = AdamantiumProperty.Register(nameof(TransparentToInput),
        typeof(bool), typeof(WindowBase), new PropertyMetadata(false, OverlayTraitChanged));

    /// <summary>False to show without taking focus - an overlay that stole activation would end the very drag it is
    /// there to help with. Read when the window is shown.</summary>
    public static readonly AdamantiumProperty ActivateOnShowProperty = AdamantiumProperty.Register(nameof(ActivateOnShow),
        typeof(bool), typeof(WindowBase), new PropertyMetadata(true, OverlayTraitChanged));

    /// <summary>The OS frame around the window: the ambient drop shadow and the accent outline. On by default - it is
    /// what makes a window look like a window. An overlay turns it off.</summary>
    public static readonly AdamantiumProperty ShowWindowBorderProperty = AdamantiumProperty.Register(nameof(ShowWindowBorder),
        typeof(bool), typeof(WindowBase), new PropertyMetadata(true, OverlayTraitChanged));

    public bool ShowWindowBorder
    {
        get => GetValue<bool>(ShowWindowBorderProperty);
        set => SetValue(ShowWindowBorderProperty, value);
    }

    /// <summary>Per-pixel transparency: the desktop composes the window with its alpha. Changing it rebuilds the swapchain
    /// at the next frame.</summary>
    public static readonly AdamantiumProperty UseTransparentCompositionProperty = AdamantiumProperty.Register(nameof(UseTransparentComposition),
        typeof(bool), typeof(WindowBase), new PropertyMetadata(false, TransparentCompositionChanged));

    // Never rebuild from the setter: it is called on whatever thread set the property, while the render thread may be
    // mid-frame with the swapchain it is about to destroy. Marking it stale hands the rebuild to BeginDraw, which runs
    // before the frame draws and is serialized with submit/present.
    private static void TransparentCompositionChanged(AdamantiumComponent component, AdamantiumPropertyChangedEventArgs e)
    {
        // The metadata callback fires on EVERY write, not only on a change of value - and a rebuild costs a device-idle
        // wait plus every render target, so a write that said nothing must not buy one.
        if (Equals(e.OldValue, e.NewValue)) return;

        (component as WindowBase)?.Renderer?.InvalidatePresenter();
    }

    /// <summary>How frames reach the screen: paced and tear-free or as fast as possible; Inherit uses the application's.
    /// Changing it rebuilds the swapchain at the next frame.</summary>
    public static readonly AdamantiumProperty PresentPolicyProperty = AdamantiumProperty.Register(nameof(PresentPolicy),
        typeof(PresentPolicy), typeof(WindowBase), new PropertyMetadata(PresentPolicy.Inherit, PresentPolicyChanged));

    private static void PresentPolicyChanged(AdamantiumComponent component, AdamantiumPropertyChangedEventArgs e)
    {
        // As with transparent composition: the callback fires on every write, and a rebuild costs a device-idle wait
        // plus every render target, so a write that said nothing must not buy one. And never rebuild from the setter -
        // it runs on whatever thread wrote the property while the render thread may be mid-frame.
        if (Equals(e.OldValue, e.NewValue)) return;

        (component as WindowBase)?.Renderer?.InvalidatePresenter();
    }

    public PresentPolicy PresentPolicy
    {
        get => GetValue<PresentPolicy>(PresentPolicyProperty);
        set => SetValue(PresentPolicyProperty, value);
    }

    /// <summary>Uniform translucency of the whole window, 0..1. Composed by the desktop, so the content underneath
    /// shows through live - which is what a docking preview rectangle is.</summary>
    public static readonly AdamantiumProperty WindowOpacityProperty = AdamantiumProperty.Register(nameof(WindowOpacity),
        typeof(double), typeof(WindowBase), new PropertyMetadata(1.0, OverlayTraitChanged));

    public bool Topmost
    {
        get => GetValue<bool>(TopmostProperty);
        set => SetValue(TopmostProperty, value);
    }

    public bool TransparentToInput
    {
        get => GetValue<bool>(TransparentToInputProperty);
        set => SetValue(TransparentToInputProperty, value);
    }

    public bool ActivateOnShow
    {
        get => GetValue<bool>(ActivateOnShowProperty);
        set => SetValue(ActivateOnShowProperty, value);
    }

    public bool UseTransparentComposition
    {
        get => GetValue<bool>(UseTransparentCompositionProperty);
        set => SetValue(UseTransparentCompositionProperty, value);
    }

    public double WindowOpacity
    {
        get => GetValue<double>(WindowOpacityProperty);
        set => SetValue(WindowOpacityProperty, value);
    }

    public static readonly AdamantiumProperty ResizeModeProperty = AdamantiumProperty.Register(nameof(ResizeMode),
        typeof(WindowResizeMode), typeof(WindowBase), new PropertyMetadata(WindowResizeMode.CanResize, ResizeModeChangedCallback));

    // Whether this window is the active (focused) one - set by the platform on WM_ACTIVATE. An AdamantiumProperty so the
    // theme can trigger on it (accent title bar / border when active, dimmed when not), AffectsRender to repaint the swap.
    public static readonly AdamantiumProperty IsActiveProperty = AdamantiumProperty.Register(nameof(IsActive),
        typeof(bool), typeof(WindowBase), new PropertyMetadata(false, PropertyMetadataOptions.AffectsRender));

    // True while a theme swap's cascade is still draining (see IThemeManager.IsThemeChanging). Mirrored onto the window as
    // an AdamantiumProperty for one reason: it is what a THEME triggers on to raise its own busy overlay in the window
    // template. The engine owns the STATE; what is shown - and whether anything is shown at all - is the theme's call.
    public static readonly AdamantiumProperty IsThemeChangingProperty = AdamantiumProperty.Register(nameof(IsThemeChanging),
        typeof(bool), typeof(WindowBase), new PropertyMetadata(false, PropertyMetadataOptions.AffectsRender));

    public bool IsThemeChanging
    {
        get => GetValue<bool>(IsThemeChangingProperty);
        private set => SetValue(IsThemeChangingProperty, value);
    }

    // The caption a theme's busy overlay shows. A window property (not baked into the template) so the indicator is a
    // GENERIC busy overlay whose text an app sets for any wait, not only the theme swap. The theme provides the default.
    public static readonly AdamantiumProperty LoadingIndicatorTextProperty = AdamantiumProperty.Register(
        nameof(LoadingIndicatorText), typeof(string), typeof(WindowBase), new PropertyMetadata(string.Empty));

    public string LoadingIndicatorText
    {
        get => GetValue<string>(LoadingIndicatorTextProperty);
        set => SetValue(LoadingIndicatorTextProperty, value);
    }

    // A plain .NET event (not a routed one): the platform worker keeps a thread-safe ResizeMode snapshot for the hit-test
    // and refreshes it here when the mode changes at runtime (e.g. toggling grip-resize on).
    public event EventHandler ResizeModeChanged;

    private static void ResizeModeChangedCallback(AdamantiumComponent adamantiumComponent, AdamantiumPropertyChangedEventArgs e)
    {
        if (adamantiumComponent is WindowBase component)
            component.ResizeModeChanged?.Invoke(component, EventArgs.Empty);
    }

    // MahApps-style caption command bars, forwarded to the TitleBar by the default Window template. Bind a view-model's
    // collection of WindowCommand items to show quick actions in the title bar (left of it / right, before the buttons).
    public static readonly AdamantiumProperty LeftWindowCommandsProperty = AdamantiumProperty.Register(nameof(LeftWindowCommands),
        typeof(System.Collections.IEnumerable), typeof(WindowBase), new PropertyMetadata(null));

    public static readonly AdamantiumProperty RightWindowCommandsProperty = AdamantiumProperty.Register(nameof(RightWindowCommands),
        typeof(System.Collections.IEnumerable), typeof(WindowBase), new PropertyMetadata(null));

    // Forwarded to TitleBar.LeadingContent by the default template.
    public static readonly AdamantiumProperty TitleBarLeadingContentProperty = AdamantiumProperty.Register(
        nameof(TitleBarLeadingContent), typeof(object), typeof(WindowBase),
        new PropertyMetadata(null, PropertyMetadataOptions.AffectsMeasure));

    /// <summary>Content placed at the start of the custom caption, before the window commands.</summary>
    public object TitleBarLeadingContent
    {
        get => GetValue(TitleBarLeadingContentProperty);
        set => SetValue(TitleBarLeadingContentProperty, value);
    }

    // Window icon/logo shown at the left of the custom title bar (forwarded to the TitleBar by the default template).
    public static readonly AdamantiumProperty IconProperty = AdamantiumProperty.Register(nameof(Icon),
        typeof(object), typeof(WindowBase), new PropertyMetadata(null));

    public static readonly AdamantiumProperty TitleAlignmentProperty = AdamantiumProperty.Register(nameof(TitleAlignment),
        typeof(HorizontalAlignment), typeof(WindowBase), new PropertyMetadata(HorizontalAlignment.Left));

    public static readonly AdamantiumProperty StartupLocationProperty = AdamantiumProperty.Register(nameof(StartupLocation),
        typeof(WindowStartupLocation), typeof(WindowBase), new PropertyMetadata(WindowStartupLocation.CenterOwner));

    public static readonly AdamantiumProperty RemembersPlacementProperty = AdamantiumProperty.Register(nameof(RemembersPlacement),
        typeof(bool), typeof(WindowBase), new PropertyMetadata(true));

    public static readonly AdamantiumProperty PlacementKeyProperty = AdamantiumProperty.Register(nameof(PlacementKey),
        typeof(string), typeof(WindowBase), new PropertyMetadata(null));

    // Caption background for the ACTIVE (focused) and INACTIVE window - the default template paints the TitleBar with
    // InactiveTitleBarBackground and swaps to TitleBarBackground while IsActive. Theme sets the defaults (accent / neutral);
    // a user can override either on the window (e.g. a brand color when focused, a custom dim when not).
    /// <summary>Height of the custom-chrome caption. The WINDOW owns this number and the theme's title bar measures
    /// itself by it - not the other way round: code that needs the caption (positioning a window under the cursor that
    /// grabbed it, hit-testing the drag area) must not have to reach into a template part, and a restyle must not be
    /// able to drift away from what the window believes its caption to be.</summary>
    public static readonly AdamantiumProperty TitleBarHeightProperty = AdamantiumProperty.Register(nameof(TitleBarHeight),
        typeof(double), typeof(WindowBase), new PropertyMetadata(36.0, PropertyMetadataOptions.AffectsMeasure));

    public double TitleBarHeight
    {
        get => GetValue<double>(TitleBarHeightProperty);
        set => SetValue(TitleBarHeightProperty, value);
    }

    /// <summary>Which side the caption buttons sit on. Stated on the WINDOW for the same reason the caption's height is:
    /// it is a property of the window's chrome, a theme sets it with an ordinary setter, and the template hands it down
    /// to the title bar. A platform theme therefore says "left" once instead of every window saying it.</summary>
    public static readonly AdamantiumProperty CaptionButtonPlacementProperty = AdamantiumProperty.Register(
        nameof(CaptionButtonPlacement), typeof(CaptionButtonPlacement), typeof(WindowBase),
        new PropertyMetadata(CaptionButtonPlacement.Right, PropertyMetadataOptions.AffectsMeasure));

    public CaptionButtonPlacement CaptionButtonPlacement
    {
        get => GetValue<CaptionButtonPlacement>(CaptionButtonPlacementProperty);
        set => SetValue(CaptionButtonPlacementProperty, value);
    }

    public static readonly AdamantiumProperty TitleBarBackgroundProperty = AdamantiumProperty.Register(nameof(TitleBarBackground),
        typeof(Brush), typeof(WindowBase), new PropertyMetadata(null, PropertyMetadataOptions.AffectsRender));

    public static readonly AdamantiumProperty InactiveTitleBarBackgroundProperty = AdamantiumProperty.Register(nameof(InactiveTitleBarBackground),
        typeof(Brush), typeof(WindowBase), new PropertyMetadata(null, PropertyMetadataOptions.AffectsRender));

    public Brush TitleBarBackground
    {
        get => GetValue<Brush>(TitleBarBackgroundProperty);
        set => SetValue(TitleBarBackgroundProperty, value);
    }

    public Brush InactiveTitleBarBackground
    {
        get => GetValue<Brush>(InactiveTitleBarBackgroundProperty);
        set => SetValue(InactiveTitleBarBackgroundProperty, value);
    }

    // Caption FOREGROUND (title text + caption-button glyphs) for the ACTIVE and INACTIVE window - mirrors the background
    // pair. Default active = the theme's on-accent contrast color (white on a dark accent, black on a light one) so the
    // caption reads on an accent-painted bar; inactive = the neutral primary text color. Overridable per window.
    public static readonly AdamantiumProperty TitleBarForegroundProperty = AdamantiumProperty.Register(nameof(TitleBarForeground),
        typeof(Brush), typeof(WindowBase), new PropertyMetadata(null, PropertyMetadataOptions.AffectsRender));

    public static readonly AdamantiumProperty InactiveTitleBarForegroundProperty = AdamantiumProperty.Register(nameof(InactiveTitleBarForeground),
        typeof(Brush), typeof(WindowBase), new PropertyMetadata(null, PropertyMetadataOptions.AffectsRender));

    public Brush TitleBarForeground
    {
        get => GetValue<Brush>(TitleBarForegroundProperty);
        set => SetValue(TitleBarForegroundProperty, value);
    }

    public Brush InactiveTitleBarForeground
    {
        get => GetValue<Brush>(InactiveTitleBarForegroundProperty);
        set => SetValue(InactiveTitleBarForegroundProperty, value);
    }

    private static void TitleChangedCallback(AdamantiumComponent a, AdamantiumPropertyChangedEventArgs e)
    {
        if (!(a is WindowBase component)) return;

        if (component.WindowWorkerService != null)
        {
            var title = (string)e.NewValue;
            component.WindowWorkerService.SetTitle(title);
        }
    }

    private static void StateChangedCallback(AdamantiumComponent adamantiumComponent, AdamantiumPropertyChangedEventArgs e)
    {
        if (!(adamantiumComponent is WindowBase component)) return;

        var args = new StateChangedEventArgs((WindowState)e.NewValue);
        args.RoutedEvent = StateChangedEvent;
        component.RaiseEvent(args);
    }
        
    private static void MSAALevelChangedCallback(AdamantiumComponent adamantiumComponent, AdamantiumPropertyChangedEventArgs e)
    {
        if (!(adamantiumComponent is WindowBase component)) return;

        var args = new MSAALevelChangedEventArgs((MSAALevel)e.NewValue);
        args.RoutedEvent = MSAALevelChangedEvent;
        component.RaiseEvent(args);
    }

    private static void ClientWidthChangedCallBack(AdamantiumComponent adamantiumAdamantiumComponent, AdamantiumPropertyChangedEventArgs e)
    {
        if (!(adamantiumAdamantiumComponent is WindowBase component)) return;
        Size old = default;
        // Only concrete numbers are a size to push to the OS window or to report; the default is NaN (auto).
        if (e.OldValue is not double oldWidth || double.IsNaN(oldWidth) || e.NewValue is not double newWidth)
            return;

        // No forced full walk on resize: layout changes mark render dirty themselves, so the resize splices. Theme and DPI
        // swaps still force one.

        // Tell the OS window, exactly as a Left/Top change does. Without this the client size was a managed number the
        // window itself never followed: it kept whatever it was created with, so nothing could be resized from code
        // after it opened (found on the docking compass overlay, which is re-sized to the area it covers).
        component.WindowWorkerService?.SetSize(component.ClientWidth, component.ClientHeight);

        old.Width = oldWidth;
        old.Height = component.Height;

        var newSize = new Size(newWidth, component.Height);
        var args = new SizeChangedEventArgs(old, newSize, true, false);
        args.RoutedEvent = ClientSizeChangedEvent;
        component.RaiseEvent(args);
    }
        
    private static void ClientHeightChangedCallBack(AdamantiumComponent adamantiumAdamantiumComponent, AdamantiumPropertyChangedEventArgs e)
    {
        if (!(adamantiumAdamantiumComponent is WindowBase component)) return;
        // See ClientWidthChangedCallBack.
        if (e.OldValue is not double oldHeight || double.IsNaN(oldHeight) || e.NewValue is not double newHeight)
            return;

        // No forced full walks - see ClientWidthChangedCallBack: the resize settle marks honestly now, so it splices.

        component.WindowWorkerService?.SetSize(component.ClientWidth, component.ClientHeight);

        var old = new Size(component.Width, oldHeight);
        var newSize = new Size(component.Width, newHeight);
        var args = new SizeChangedEventArgs(old, newSize, false, true);
        args.RoutedEvent = ClientSizeChangedEvent;
        component?.RaiseEvent(args);
    }

    /// <summary>The window's left edge on the desktop in physical pixels, like <see cref="PointToScreen"/>: which monitor's
    /// scale applies is only known from the physical point.</summary>
    public Double Left
    {
        get => GetValue<Double>(LeftProperty);
        set => SetValue(LeftProperty, value);
    }
        
    public Double Top
    {
        get => GetValue<Double>(TopProperty);
        set => SetValue(TopProperty, value);
    }
        
    public string Title
    {
        get => GetValue<string>(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public MSAALevel MSAALevel
    {
        get => GetValue<MSAALevel>(MSAALevelProperty);
        set => SetValue(MSAALevelProperty, value);
    }

    public bool AnalyticAntialiasing
    {
        get => GetValue<bool>(AnalyticAntialiasingProperty);
        set => SetValue(AnalyticAntialiasingProperty, value);
    }

    public WindowState State
    {
        get => GetValue<WindowState>(StateProperty);
        set => SetValue(StateProperty, value);
    }

    public bool UseCustomChrome
    {
        get => GetValue<bool>(UseCustomChromeProperty);
        set => SetValue(UseCustomChromeProperty, value);
    }

    public WindowResizeMode ResizeMode
    {
        get => GetValue<WindowResizeMode>(ResizeModeProperty);
        set => SetValue(ResizeModeProperty, value);
    }

    public System.Collections.IEnumerable LeftWindowCommands
    {
        get => GetValue<System.Collections.IEnumerable>(LeftWindowCommandsProperty);
        set => SetValue(LeftWindowCommandsProperty, value);
    }

    public System.Collections.IEnumerable RightWindowCommands
    {
        get => GetValue<System.Collections.IEnumerable>(RightWindowCommandsProperty);
        set => SetValue(RightWindowCommandsProperty, value);
    }

    /// <summary>Icon/logo content shown at the left of the custom title bar.</summary>
    public object Icon
    {
        get => GetValue<object>(IconProperty);
        set => SetValue(IconProperty, value);
    }

    /// <summary>Where the title stands in the custom title bar; <c>Center</c> is the middle of the window, whatever
    /// sits on either side.</summary>
    public HorizontalAlignment TitleAlignment
    {
        get => GetValue<HorizontalAlignment>(TitleAlignmentProperty);
        set => SetValue(TitleAlignmentProperty, value);
    }

    /// <summary>Where the window appears when it has no remembered place (see <see cref="RemembersPlacement"/>).</summary>
    public WindowStartupLocation StartupLocation
    {
        get => GetValue<WindowStartupLocation>(StartupLocationProperty);
        set => SetValue(StartupLocationProperty, value);
    }

    /// <summary>Whether the window comes back where it was closed: on its screen, in its place, at its size, maximized if
    /// it was. On by default; when that screen is no longer connected, <see cref="StartupLocation"/> places it.</summary>
    public bool RemembersPlacement
    {
        get => GetValue<bool>(RemembersPlacementProperty);
        set => SetValue(RemembersPlacementProperty, value);
    }

    /// <summary>What the window's place is remembered under; the window's type when null. Windows of one type that each
    /// keep a place of their own take a key each.</summary>
    public string PlacementKey
    {
        get => GetValue<string>(PlacementKeyProperty);
        set => SetValue(PlacementKeyProperty, value);
    }

    /// <summary>Begins an OS-driven move of the window (custom-chrome caption drag). Wired from a title bar's press.</summary>
    public void DragMove() => WindowWorkerService?.BeginMoveDrag();

    /// <summary>Minimizes the window (title bar minimize button).</summary>
    public void Minimize() => State = WindowState.Minimized;

    /// <summary>Maximizes the window (title bar maximize button).</summary>
    public void Maximize() => State = WindowState.Maximized;

    /// <summary>Restores a maximized/minimized window to its normal size (title bar restore button).</summary>
    public void RestoreDown() => State = WindowState.Normal;

    /// <summary>Toggles between maximized and normal - the caption double-click / maximize button behavior.</summary>
    public void ToggleMaximizeRestore() =>
        State = State == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    // Caption metrics published by a hosted TitleBar on its arrange (loop thread). Read geometrically by the worker's
    // hit-test (OS message thread) - plain doubles, so a torn read is at worst a one-frame-off hit, never a crash. This
    // MUST stay geometric: the earlier visual-tree walk (GetVisualsAt) from WM_NCHITTEST raced the layout thread's
    // VisualChildren mutation during a state-change relayout and spun the UI thread (every caption button froze the app).
    public Rect CaptionDragRect { get; set; }

    // Published by a ResizeGripper on layout; read by the platform hit-test (OS message thread). Plain Rect for the same
    // thread-safety reason as CaptionDragRect. Empty = no grip / not in grip-resize mode.
    public Rect ResizeGripRect { get; set; }

    public Rect InputMethodCaret { get; set; }

    public Double ClientWidth
    {
        get => GetValue<Double>(ClientWidthProperty);
        set => SetValue(ClientWidthProperty, value);
    }

    public Double ClientHeight
    {
        get => GetValue<Double>(ClientHeightProperty);
        set => SetValue(ClientHeightProperty, value);
    }
        
    // Pointer to the surface for rendering on this window
    public abstract IntPtr SurfaceHandle { get; internal set; }
        
    public abstract IntPtr Handle { get; internal set; }
    public bool IsClosed { get; protected set; }

    public abstract Vector2 PointToClient(PixelPoint point);
    public abstract PixelPoint PointToScreen(Vector2 point);

    /// <summary>Where this window sits on the desktop, as ONE typed value. <see cref="Left"/>/<see cref="Top"/> hold the
    /// same thing as bindable numbers (a position is authored and serialized as two numbers); everything that COMPUTES a
    /// position uses this, so the units cannot be lost on the way - see <see cref="PixelPoint"/>.</summary>
    public PixelPoint Position
    {
        get => new(Left, Top);
        set
        {
            Left = value.X;
            Top = value.Y;
        }
    }
    public void AttachContextAndInitialize(IUIContext context)
    {
        UIContext = context;
        InitializeComponent();
        // The root window has no logical parent, so OnAttachedToLogicalTree never fires for it - resolve its
        // x:ViewModel here, once its tree is built and the context is set. Nested views self-resolve on attach.
        ApplyViewModel();
        WindowWorkerService = CreateWindowWorker(context);
        if (WindowWorkerService != null)
        {
            PlaceOnScreen(context);
            WindowWorkerService.SetWindow(this);
        }
        else
        {
            // The platform worker themes the window it creates; with no worker nothing else would, and the window would
            // never take its style or its template - no caption, no window background.
            context.ThemeEngine.ApplyCurrentTheme(this);
        }

        var themes = UIAppContext.Current?.ThemeManager;
        if (themes != null)
        {
            themes.ThemeChanging += OnThemeChanging;
            themes.ThemeChanged += OnThemeChanged;
            IsThemeChanging = themes.IsThemeChanging;   // a window opened mid-swap already shows the busy state
        }

        Languages.Changed += OnLanguageChanged;
    }

    private void OnLanguageChanged(object sender, EventArgs e)
    {
        foreach (var node in GetVisualDescendants())
        {
            if (node is TextBlock or TextBoxBase)
            {
                ((MeasurableUIComponent)node).InvalidateMeasure();
            }
        }
    }

    private void PlaceOnScreen(IUIContext context)
    {
        var screens = PlatformSettings.Screens;
        if (!ActivateOnShow || Design.IsDesignMode || screens.Count == 0)
        {
            return;
        }

        var remembered = RemembersPlacement ? context.Resolve<IWindowPlacementStore>().Load(PlacementName()) : null;
        var home = remembered == null ? null : screens.FirstOrDefault(s => s.Id == remembered.ScreenId);
        if (home != null)
        {
            ClientWidth = remembered.ClientWidth;
            ClientHeight = remembered.ClientHeight;
            Position = WindowPlacer.Restore(remembered, screens, SizeOn(home)) ?? Position;
            _opensMaximized = remembered.IsMaximized;
            return;
        }

        if (StartupLocation == WindowStartupLocation.CenterOwner
            && context.UIApplication?.ActiveWindow is WindowBase owner && !ReferenceEquals(owner, this))
        {
            var corner = owner.LivePosition;
            var ownerBounds = new Rect(corner.X, corner.Y, owner.Width, owner.Height);
            Position = WindowPlacer.Center(ownerBounds, SizeOn(WindowPlacer.ScreenOf(ownerBounds, screens)));
        }
        else if (StartupLocation != WindowStartupLocation.Manual)
        {
            var screen = WindowPlacer.ScreenAt(Mouse.ScreenCoordinates, screens);
            Position = WindowPlacer.Center(screen.WorkArea, SizeOn(screen));
        }
    }

    private Size SizeOn(ScreenInfo screen) =>
        new(ExtentOn(screen, ClientWidth, Width, 800), ExtentOn(screen, ClientHeight, Height, 600));

    private static double ExtentOn(ScreenInfo screen, double client, double outer, double fallback)
    {
        if (!double.IsNaN(client) && client > 0)
        {
            return client * screen.Scale;
        }

        // Logical as written in markup, like the client size: the platform scales it the same way when it creates the window.
        return (!double.IsNaN(outer) && outer > 0 ? outer : fallback) * screen.Scale;
    }

    private void RememberPlacement()
    {
        var bounds = WindowWorkerService?.RestoreBounds ?? default;
        var screens = PlatformSettings.Screens;
        if (!RemembersPlacement || !ActivateOnShow || Design.IsDesignMode || bounds.Width <= 0 || screens.Count == 0)
        {
            return;
        }

        var screen = WindowPlacer.ScreenOf(bounds, screens);
        var frame = FrameSize();
        UIContext.Resolve<IWindowPlacementStore>().Save(PlacementName(), new WindowPlacement
        {
            ScreenId = screen.Id,
            Left = bounds.X - screen.WorkArea.X,
            Top = bounds.Y - screen.WorkArea.Y,
            ClientWidth = (bounds.Width - frame.Width) / DpiScale.X,
            ClientHeight = (bounds.Height - frame.Height) / DpiScale.Y,
            IsMaximized = State == WindowState.Maximized
        });
    }

    private Size FrameSize() =>
        UseCustomChrome ? default : new Size(Math.Max(0, Width - ClientWidth * DpiScale.X), Math.Max(0, Height - ClientHeight * DpiScale.Y));

    private string PlacementName() => PlacementKey ?? GetType().FullName;

    /// <summary>The platform side of this window - an OS window and its message loop. A window drawn inside something
    /// else has none and returns null; everything that talks to the worker then does nothing.</summary>
    protected virtual IWindowWorkerService CreateWindowWorker(IUIContext context) =>
        UIAppContext.PlatformService.GetWindowWorker(context);

    private void OnThemeChanging(object sender, ThemeChangedEventArgs e) => IsThemeChanging = true;

    private void OnThemeChanged(object sender, ThemeChangedEventArgs e) => IsThemeChanging = false;

    protected virtual void InitializeComponent()
    {
        
    }

    public Vector2 ScreenToClient(PixelPoint p)
    {
        var point = new NativePoint((int)p.X, (int)p.Y);
        Win32Interop.ScreenToClient(Handle, ref point);
        // Win32 returns PHYSICAL client px; the framework works in logical DIP -> divide by THIS window's scale.
        return new PixelPoint(point.X, point.Y).ToLogical(DpiScale);
    }

    /// <summary>A point of this window's client area (LOGICAL) to a desktop point (PHYSICAL). The asymmetry is the
    /// desktop's: monitors can differ in scale, so a screen point has no one scale to be logical in. Convert with the
    /// scale of the window the point concerns - see <see cref="Left"/>.</summary>
    public PixelPoint ClientToScreen(Vector2 p)
    {
        // p is logical DIP -> back to physical client px before handing to Win32; the returned screen coords stay physical.
        var physical = PixelPoint.FromLogical(p, DpiScale);
        var point = new NativePoint((int)physical.X, (int)physical.Y);
        Win32Interop.ClientToScreen(Handle, ref point);
        return new PixelPoint(point.X, point.Y);
    }

    public bool ShouldDisplayWindow { get; protected set; }

    public void Initialize(IUIContext uiContext)
    {
        UIContext = uiContext;
        
    }
    public abstract void Show();
    public abstract void Close();
    public abstract void Hide();

    /// <summary>Bring this window to the foreground (restoring it if minimized). Platform-specific via the window worker.</summary>
    public void Activate() => WindowWorkerService?.Activate();

    /// <summary>Raise this window above the others WITHOUT taking focus - the mid-drag-safe counterpart of
    /// <see cref="Activate"/>, which would cost the drag its mouse capture.</summary>
    public void BringToFront() => WindowWorkerService?.RaiseWithoutActivation();

    /// <summary>Enter/leave RELATIVE mouse mode (hidden, centered cursor + synthesized raw delta) for a hosted universe's
    /// mouse-look. Driven by a <see cref="Panels.RenderTargetPanel"/> per its <c>MouseLookMode</c>; delegates to the
    /// platform worker.</summary>
    public void SetRelativeMouseMode(bool enabled, PixelPoint restoreScreen) =>
        WindowWorkerService?.SetRelativeMouseMode(enabled, restoreScreen);
        
    public bool IsActive
    {
        get => GetValue<bool>(IsActiveProperty);
        internal set => SetValue(IsActiveProperty, value);
    }

    public IDrawingContext GetDrawingContext()
    {
        if (Renderer != null)
        {
            return Renderer.DrawingContext;
        }

        if (DefaultRenderer != null)
            return DefaultRenderer.DrawingContext;

        throw new ArgumentException("Window does not contain renderer and could not return DrawingContext");
    }

    public IUIContext UIContext { get; private set; }

    public event SizeChangedEventHandler ClientSizeChanged
    {
        add => AddHandler(ClientSizeChangedEvent, value);
        remove => RemoveHandler(ClientSizeChangedEvent, value);
    }
        
    public event MSAALeveChangedHandler MSAALevelChanged
    {
        add => AddHandler(MSAALevelChangedEvent, value);
        remove => RemoveHandler(MSAALevelChangedEvent, value);
    }

    public event StateChangedHandler StateChanged
    {
        add => AddHandler(StateChangedEvent, value);
        remove => RemoveHandler(StateChangedEvent, value);
    }

    public void SetHandle(IntPtr handle)
    {
        Handle = handle;
    }

    public void SetSurface(IntPtr surfaceHandle)
    {
        SurfaceHandle = surfaceHandle;
    }

    void IWindowInternals.OnSourceInitialized()
    {
        SourceInitialized?.Invoke(this, EventArgs.Empty);
        // Seed the initial layout via the manager, NOT InvalidateMeasure(): a fresh root is IsMeasureValid=false, so that
        // method's early-return drops the enqueue - the first real layout would otherwise defer to the first user input.
        LayoutManager.GetOrCreate(this).InvalidateMeasure(this);
        HookHoverRefresh();
    }

    public void SetIsActive(bool isActive)
    {
        IsActive = isActive;
    }

    /// <summary>The OS is moving this window, once per step of its move loop. The only signal available during a caption
    /// drag: the platform's loop owns the mouse, so no managed move or button-up arrives until it ends. A docking host
    /// listens here to decide where the window would land.</summary>
    public event EventHandler WindowMoving;

    /// <summary>The move loop ended - the button is up and the window has settled. Where a drop is committed.</summary>
    public event EventHandler WindowMoveCompleted;

    public void RaiseWindowMoving() => WindowMoving?.Invoke(this, EventArgs.Empty);

    public void RaiseWindowMoveCompleted() => WindowMoveCompleted?.Invoke(this, EventArgs.Empty);

    protected void OnClosed()
    {
        var closingArgs = new WindowClosingEventArgs();
        Closing?.Invoke(this, closingArgs);
        if (!closingArgs.Cancel)
        {
            RememberPlacement();
            // The theme manager outlives every window, so a closed one that stayed subscribed would be kept alive by it.
            var themes = UIAppContext.Current?.ThemeManager;
            if (themes != null)
            {
                themes.ThemeChanging -= OnThemeChanging;
                themes.ThemeChanged -= OnThemeChanged;
            }
            Languages.Changed -= OnLanguageChanged;
            Closed?.Invoke(this, EventArgs.Empty);
        }
    }

    public event EventHandler<WindowClosingEventArgs> Closing;
    public event EventHandler<EventArgs> Closed;
    public event EventHandler<WindowRendererChangedEventArgs> RendererChanged;

    public event EventHandler<EventArgs> SourceInitialized;

    private Vector2 _dpiScale = new Vector2(1, 1);
    public Vector2 DpiScale
    {
        get => _dpiScale;
        set
        {
            if (_dpiScale == value) return;
            _dpiScale = value;
            // A DPI change re-scales the renderer (RenderScale/projection) and re-lays-out the tree over the next few
            // frames - and, like a theme swap, parts of that settle through paths that never mark the render dirty. A
            // Clean-frame op-replay then keeps showing the OLD-scale content (shrunken in the corner) until an unrelated
            // mark (a mouse move's hover) forces a walk. Force full render walks until the layout settles.
            VisualTreeNotifications.RaiseStateSwapStarted();
            DpiChanged?.Invoke(this, EventArgs.Empty);
        }
    }
    public event EventHandler<EventArgs> DpiChanged;

    protected override AutomationPeer OnCreateAutomationPeer() => new WindowAutomationPeer(this);
}
