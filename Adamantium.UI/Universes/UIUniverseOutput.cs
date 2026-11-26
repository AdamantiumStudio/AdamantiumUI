using System;
using System.Collections.Generic;
using System.Linq;
using Adamantium.Core.Events;
using Adamantium.Multiverse;
using Adamantium.Multiverse.Input;
using Adamantium.Graphics.Core;
using Adamantium.Imaging;
using Adamantium.Mathematics;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Base;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Input;
using Adamantium.UI.Core.RoutedEvents;


namespace Adamantium.UI.Universes
{
    public abstract class UIUniverseOutput : UniverseOutput, IUniverseInputTarget
    {
        private OutputCursor cursor;
        private IWindow scaleSource;
        private IInputComponent listenedInput;
        private UniverseInputListener inputListener;
        private readonly List<IUIComponent> watchedAncestors = [];
        private readonly object ancestorsLock = new();
        private bool watchingAncestors;

        public override UniverseOutputDescription Description { get; protected set; }

        /// <summary>The component the universe's surface IS - a window, or the panel it is hosted in. Public because a
        /// cursor position only means something relative to it.</summary>
        public IInputComponent InputComponent { get; protected set; }

        // Relative to the surface, not the window, through the UI's own conversion: by hand it was right only at 100% DPI.
        public override Vector2F PointerScreenPosition
        {
            get
            {
                var point = Mouse.Pointer.GetScreenPosition();
                return new Vector2F((float)point.X, (float)point.Y);
            }
        }

        // Asked from the universe's thread: through where the surface stood when its own thread last placed it, never
        // by walking the tree there - a tab switched detaches it midway. Off the tree, where it stood last; the origin
        // before it ever stood anywhere.
        public override Vector2F PointToSurface(Vector2F absolute)
        {
            if (placement is not { } placed)
            {
                return Vector2F.Zero;
            }

            // As TranslatePoint goes: the window's client point into the world, then into the surface.
            var client = placed.Root.PointToClient(new PixelPoint((int)absolute.X, (int)absolute.Y));
            var world = Vector3F.TransformCoordinate(new Vector3F((float)client.X, (float)client.Y, 0), placed.RootWorld);
            var point = Vector3F.TransformCoordinate(world, placed.WorldToSurface);
            return new Vector2F(point.X, point.Y) * PixelsPerPoint;
        }

        // Where the surface stands in its window: the window, and its client points into the surface's own, read on
        // the surface's thread.
        private sealed record SurfacePlacement(IRootVisualComponent Root, Matrix4x4F RootWorld, Matrix4x4F SurfaceWorld,
            Matrix4x4F WorldToSurface);

        private volatile SurfacePlacement placement;
        private LayoutManager placedBy;

        // On the surface's own thread: after a layout pass or an overlay arranged, a pointer move, a render transform
        // given to it or above it, and as it comes into the tree.
        private void Place()
        {
            IUIComponent root = InputComponent;
            while (root is not IRootVisualComponent && root != null)
            {
                root = root.VisualParent;
            }

            if (root is not IRootVisualComponent window || !InputComponent.IsAttachedToVisualTree)
            {
                return;
            }

            // Made anew only when it moved: a pointer move asks every time.
            var rootWorld = root.WorldTransform;
            var surfaceWorld = InputComponent.WorldTransform;
            if (placement is { } placed && ReferenceEquals(placed.Root, window) && placed.RootWorld == rootWorld
                && placed.SurfaceWorld == surfaceWorld)
            {
                return;
            }

            placement = new SurfacePlacement(window, rootWorld, surfaceWorld, Matrix4x4F.Invert(surfaceWorld));
        }

        private void FollowLayout(bool attached)
        {
            var wanted = attached ? LayoutManager.For(InputComponent) : null;
            if (ReferenceEquals(wanted, placedBy))
            {
                return;
            }

            if (placedBy != null)
            {
                placedBy.PlacesSettled -= OnPlacesSettled;
            }

            placedBy = wanted;
            if (placedBy != null)
            {
                placedBy.PlacesSettled += OnPlacesSettled;
            }
        }

        private void OnPlacesSettled(object sender, EventArgs e) => Place();

        /// <summary>The host window's scale, pixels per point; 1 while the surface has no window.</summary>
        protected float HostScale => HostWindow is { } window ? (float)window.DpiScale.X : 1;

        /// <summary>The host window moved to a screen of another scale; <see cref="HostScale"/> has the new one.</summary>
        protected virtual void OnHostScaleChanged()
        {
        }

        // The same relative mode the surface's own mouse-look engages: it enters and leaves on the window's own thread
        // (the worker posts itself a message), which is what makes it safe to ask for from the universe loop.
        public override void HoldPointer(bool hold, Vector2F origin)
        {
            if (InputComponent?.RootVisual?.Window is not WindowBase root) return;

            if (hold) heldFrom = new PixelPoint((int)origin.X, (int)origin.Y);

            root.SetRelativeMouseMode(hold, hold ? default : heldFrom);
        }

        private PixelPoint heldFrom;

        protected UIUniverseOutput(IEventAggregator eventAggregator) : base(eventAggregator)
        {

        }

        protected override void Initialize(OutputContext context)
        {
            Initialize(context, SurfaceFormat.B8G8R8A8.UNorm);
        }

        protected override void Initialize(
            OutputContext context,
            SurfaceFormat pixelFormat,
            DepthFormat depthFormat = DepthFormat.Depth32Stencil8X24,
            MSAALevel msaaLevel = MSAALevel.X4)
        {
            OutputContext = context;
            InitializeInternal(context);
            Description.PixelFormat = pixelFormat;
            Description.DepthFormat = depthFormat;
            Description.MsaaLevel = msaaLevel;
        }

        protected virtual void InitializeInternal(OutputContext context)
        {
            WatchHostWindow();
            StopListening();
            listenedInput = InputComponent;
            inputListener = new UniverseInputListener(listenedInput, this);
            listenedInput.PointerMoved += OnPointerMovedOrLeft;
            listenedInput.PointerExited += OnPointerMovedOrLeft;
            listenedInput.AttachedToVisualTreeEvent += OnAttachedToVisualTree;
            listenedInput.DetachedFromVisualTreeEvent += OnDetachedFromVisualTree;
            listenedInput.PropertyChanged += OnInputPropertyChanged;
            lock (ancestorsLock)
            {
                watchingAncestors = true;
            }

            WatchAncestors();
            FollowLayout(listenedInput.IsAttachedToVisualTree);
            Place();
        }

        protected override void Dispose(bool disposeManagedResources)
        {
            StopListening();
            if (scaleSource != null)
            {
                scaleSource.DpiChanged -= HostDpiChanged;
                scaleSource.StateChanged -= HostStateChanged;
                scaleSource = null;
            }

            base.Dispose(disposeManagedResources);
        }

        private void StopListening()
        {
            if (listenedInput == null)
            {
                return;
            }

            inputListener.Dispose();
            inputListener = null;
            listenedInput.PointerMoved -= OnPointerMovedOrLeft;
            listenedInput.PointerExited -= OnPointerMovedOrLeft;
            listenedInput.AttachedToVisualTreeEvent -= OnAttachedToVisualTree;
            listenedInput.DetachedFromVisualTreeEvent -= OnDetachedFromVisualTree;
            listenedInput.PropertyChanged -= OnInputPropertyChanged;
            lock (ancestorsLock)
            {
                watchingAncestors = false;
                UnwatchAncestors();
            }

            FollowLayout(false);
            placement = null;
            listenedInput = null;
        }

        public override object NativeWindow => OutputContext.Context;

        /// <summary>
        /// Cursor type that will be displayed when mouse cursor will enter <see cref="UniverseOutput"/>
        /// </summary>
        public override OutputCursor Cursor
        {
            get => cursor;
            set
            {
                cursor = value;
                switch (value)
                {
                    case OutputCursor.Arrow:
                        InputComponent.Cursor = Cursors.Arrow;
                        break;
                    case OutputCursor.AppStarting:
                        InputComponent.Cursor = Cursors.AppStarting;
                        break;
                    case OutputCursor.CrossHair:
                        InputComponent.Cursor = Cursors.Crosshair;
                        break;
                    case OutputCursor.Hand:
                        InputComponent.Cursor = Cursors.Hand;
                        break;
                    case OutputCursor.Help:
                        InputComponent.Cursor = Cursors.Help;
                        break;
                    case OutputCursor.IBeam:
                        InputComponent.Cursor = Cursors.IBeam;
                        break;
                    case OutputCursor.No:
                        InputComponent.Cursor = Cursors.No;
                        break;
                    case OutputCursor.None:
                        InputComponent.Cursor = Cursors.None;
                        break;
                    case OutputCursor.SizeAll:
                        InputComponent.Cursor = Cursors.SizeAll;
                        break;
                    case OutputCursor.SizeNWSE:
                        InputComponent.Cursor = Cursors.SizeNWSE;
                        break;
                    case OutputCursor.SizeWE:
                        InputComponent.Cursor = Cursors.SizeWE;
                        break;
                    case OutputCursor.SizeNESW:
                        InputComponent.Cursor = Cursors.SizeNESW;
                        break;
                    case OutputCursor.SizeNS:
                        InputComponent.Cursor = Cursors.SizeNS;
                        break;
                    case OutputCursor.UpArrow:
                        InputComponent.Cursor = Cursors.UpArrow;
                        break;
                    case OutputCursor.Wait:
                        InputComponent.Cursor = Cursors.Wait;
                        break;
                }
            }
        }

        public override OutputState State
        {
            get
            {
                if (HostWindow is { State: WindowState.Minimized })
                {
                    return OutputState.Minimized;
                }

                if (!InputComponent.IsAttachedToVisualTree)
                {
                    return OutputState.OutOfView;
                }

                if (InputComponent.Visibility != Visibility.Visible || HasHiddenAncestor())
                {
                    return OutputState.Hidden;
                }

                return OutputState.Shown;
            }
        }

        public override bool IsKeyboardFocused => InputComponent.IsKeyboardFocused && HostWindow is { IsActive: true };

        public override bool IsPointerOver =>
            InputComponent.IsPointerDirectlyOver ||
            PointerDevice.Current.Pointers.Any(pointer => ReferenceEquals(pointer.Captured, InputComponent));

        protected IWindow HostWindow => InputComponent as IWindow ?? InputComponent.RootVisual?.Window;

        private void HostDpiChanged(object sender, EventArgs e)
        {
            OnHostScaleChanged();
        }

        private void OnPointerMovedOrLeft(object sender, PointerEventArgs e)
        {
            Place();
            Invalidate();
        }

        private void WatchHostWindow()
        {
            var host = HostWindow;
            if (ReferenceEquals(host, scaleSource))
            {
                return;
            }

            if (scaleSource != null)
            {
                scaleSource.DpiChanged -= HostDpiChanged;
                scaleSource.StateChanged -= HostStateChanged;
            }

            scaleSource = host;
            if (scaleSource != null)
            {
                scaleSource.DpiChanged += HostDpiChanged;
                scaleSource.StateChanged += HostStateChanged;
            }
        }

        private void HostStateChanged(object sender, StateChangedEventArgs e) => Invalidate();

        private void OnAttachedToVisualTree(object sender, VisualTreeAttachmentEventArgs e)
        {
            WatchHostWindow();
            WatchAncestors();
            FollowLayout(true);
            Place();
            Invalidate();
        }

        private void OnDetachedFromVisualTree(object sender, VisualTreeAttachmentEventArgs e)
        {
            lock (ancestorsLock)
            {
                UnwatchAncestors();
            }

            // Where it stood stays, to answer by while it is away.
            FollowLayout(false);
        }

        private void OnInputPropertyChanged(object sender, AdamantiumPropertyChangedEventArgs e)
        {
            if (e.Property == UIComponent.VisibilityProperty)
            {
                Invalidate();
            }
            else if (e.Property == UIComponent.RenderTransformProperty
                     || e.Property == UIComponent.RenderTransformOriginProperty)
            {
                Place();
            }
        }

        private void WatchAncestors()
        {
            lock (ancestorsLock)
            {
                UnwatchAncestors();
                if (!watchingAncestors)
                {
                    return;
                }

                for (var ancestor = InputComponent.VisualParent; ancestor != null; ancestor = ancestor.VisualParent)
                {
                    ancestor.PropertyChanged += OnInputPropertyChanged;
                    watchedAncestors.Add(ancestor);
                }
            }
        }

        private void UnwatchAncestors()
        {
            foreach (var ancestor in watchedAncestors)
            {
                ancestor.PropertyChanged -= OnInputPropertyChanged;
            }

            watchedAncestors.Clear();
        }

        private bool HasHiddenAncestor()
        {
            for (var ancestor = InputComponent.VisualParent; ancestor != null; ancestor = ancestor.VisualParent)
            {
                if (ancestor.Visibility != Visibility.Visible)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Whether input that reached the listened element is meant for the universe. Everything is, by default;
        /// a host that lays controls over the picture keeps theirs (see <see cref="RenderTargetUniverseOutput"/>). Only
        /// presses are asked: a release always passes, or a key or button pressed in the universe would stay held.</summary>
        protected virtual bool IsUniverseInput(RoutedEventArgs e) => true;

        /// <summary>Whether a key the universe took is marked handled, so focus navigation or a scroll viewer further up
        /// the route does not act on it too. Asked only while the universe accepts the keyboard. Not by default.</summary>
        protected virtual bool SpendsKey(KeyEventArgs e) => false;

        bool IUniverseInputTarget.TakesKeyboard => Input is { TakesKeyboard: true };

        bool IUniverseInputTarget.IsKeyDown(Keys key) => Input?.IsKeyDown(key) == true;

        bool IUniverseInputTarget.IsUniverseInput(RoutedEventArgs e) => IsUniverseInput(e);

        bool IUniverseInputTarget.SpendsKey(KeyEventArgs e) => SpendsKey(e);

        void IUniverseInputTarget.TakeKeyboardInput(KeyboardInput input) => OnKeyInput(input);

        void IUniverseInputTarget.TakeMouseInput(MouseInput input) => OnMouseInput(input);

        void IUniverseInputTarget.TakeGestureInput(GestureInput input) => OnGestureInput(input);

        void IUniverseInputTarget.TakeText(string text) => OnTextInput(text);
    }
}
