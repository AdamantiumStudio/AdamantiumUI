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

        public override Vector2F PointToSurface(Vector2F absolute)
        {
            var point = UIExtensions.PointToClient(InputComponent, new PixelPoint((int)absolute.X, (int)absolute.Y));
            return new Vector2F((float)point.X, (float)point.Y) * PixelsPerPoint;
        }

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

        private void OnPointerMovedOrLeft(object sender, PointerEventArgs e) => Invalidate();

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
            Invalidate();
        }

        private void OnDetachedFromVisualTree(object sender, VisualTreeAttachmentEventArgs e)
        {
            lock (ancestorsLock)
            {
                UnwatchAncestors();
            }
        }

        private void OnInputPropertyChanged(object sender, AdamantiumPropertyChangedEventArgs e)
        {
            if (e.Property == UIComponent.VisibilityProperty)
            {
                Invalidate();
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
