using Adamantium.Graphics.Core;
using Adamantium.ProceduralGeometry;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Graphics;
using Adamantium.UI.Core.Input;
using Adamantium.UI.Core.Media.Imaging;
using Adamantium.UI.Core.RoutedEvents;

namespace Adamantium.UI.Controls.Panels;

/// <summary>Displays frames an external producer renders into a shared surface, imported zero-copy via
/// <see cref="SetSource"/>. Interop resources are freed when the source changes.</summary>
public class RenderTargetPanel : Grid
{
   private readonly object _sourceLock = new();
   private SharedSurfaceImage _image;

   static RenderTargetPanel()
   {
      UseLayoutRoundingProperty.OverrideMetadata(typeof(RenderTargetPanel),
         new PropertyMetadata(true, PropertyMetadataOptions.AffectsMeasure));
      // Unlike a plain panel, this one IS an interactive surface: the hosted universe is activated and fed input ONLY while
      // the panel holds focus (RenderTargetUniverseOutput keys IsActive/Got|LostFocus off it). So opt back IN to focus,
      // since Panel's default is now false. A click on the panel then focuses it and the universe starts receiving input.
      FocusableProperty.OverrideMetadata(typeof(RenderTargetPanel), new PropertyMetadata(true));
   }

   public RenderTargetPanel()
   {
      PointerCaptureLost += OnPointerCaptureLost;
   }

   /// <summary>The descriptor of the currently bound source, or <c>null</c> when nothing is bound.</summary>
   public SharedSurfaceDescriptor Source { get; private set; }

   public static readonly AdamantiumProperty MouseLookModeProperty = AdamantiumProperty.Register(nameof(MouseLookMode),
      typeof(MouseLookMode), typeof(RenderTargetPanel), new PropertyMetadata(MouseLookMode.None));

   /// <summary>How the panel feeds the hosted universe a relative mouse delta for mouse-look (see <see cref="MouseLookMode"/>).
   /// Default <see cref="MouseLookMode.None"/> - the cursor stays a normal pointer.</summary>
   public MouseLookMode MouseLookMode
   {
      get => GetValue<MouseLookMode>(MouseLookModeProperty);
      set => SetValue(MouseLookModeProperty, value);
   }

   public static readonly AdamantiumProperty IsMouseLookEnabledProperty = AdamantiumProperty.Register(
      nameof(IsMouseLookEnabled), typeof(bool), typeof(RenderTargetPanel),
      new PropertyMetadata(true, OnIsMouseLookEnabledChanged));

   /// <summary>Gates mouse-look ON/OFF without changing the <see cref="MouseLookMode"/>. Bind it to app state (e.g. an
   /// open overlay menu) so a click on the panel does NOT hide the cursor while the menu is up; setting it false while
   /// looking immediately restores the cursor. Default <c>true</c>.</summary>
   public bool IsMouseLookEnabled
   {
      get => GetValue<bool>(IsMouseLookEnabledProperty);
      set => SetValue(IsMouseLookEnabledProperty, value);
   }

   private static void OnIsMouseLookEnabledChanged(AdamantiumComponent a, AdamantiumPropertyChangedEventArgs e)
   {
      var panel = (RenderTargetPanel)a;
      if (!panel.IsMouseLookEnabled) panel.Disengage();   // disabled mid-look (a menu opened) -> give the cursor back
   }

   public static readonly AdamantiumProperty AcceptsTabProperty = AdamantiumProperty.Register(nameof(AcceptsTab),
      typeof(bool), typeof(RenderTargetPanel), new PropertyMetadata(true));

   /// <summary>Whether Tab typed into the panel is the hosted universe's rather than a step to the next control. Default
   /// <c>true</c>, since a game binds Tab; Ctrl+Tab then leaves the panel.</summary>
   public bool AcceptsTab
   {
      get => GetValue<bool>(AcceptsTabProperty);
      set => SetValue(AcceptsTabProperty, value);
   }

   private bool _looking;    // relative mode currently engaged by THIS panel
   private bool _leftDown;   // Drag mode: left button held on the panel
   private bool _rightDown;  // Drag mode: right button held on the panel
   private bool _middleDown;
   private Vector2 _engagePoint;   // panel-relative cursor position at engage; restored (as screen) on release

   // Esc is the always-available escape hatch out of mouse-look (frees the hidden cursor), the safety net for
   // Continuous mode and any missed button-up.
   protected override void OnKeyDown(KeyEventArgs e)
   {
      base.OnKeyDown(e);
      if (e.Key == Key.Escape)
      {
         Disengage();
      }
      else if (e.Key == Key.Tab && !e.Handled && AcceptsTab && ReferenceEquals(e.OriginalSource, this)
               && (e.Modifiers & (InputModifiers.LeftControl | InputModifiers.RightControl)) != 0)
      {
         var backwards = (e.Modifiers & (InputModifiers.LeftShift | InputModifiers.RightShift)) != 0;
         e.Handled = KeyboardNavigation.MoveToArea(backwards)
                     || KeyboardNavigation.Move(backwards ? FocusNavigationDirection.Previous : FocusNavigationDirection.Next,
                         e.Modifiers);
      }
   }

   /// <summary>
   /// Binds an externally produced surface and triggers a redraw. A frame recorded with the previously bound surface
   /// draws this one instead. Pass <c>null</c> to unbind (equivalent to <see cref="ClearSource"/>).
   /// </summary>
   public void SetSource(SharedSurfaceDescriptor descriptor)
   {
      // The producer binds from its own thread, while the panel may be leaving or joining the tree on the loop.
      lock (_sourceLock)
      {
         if (ReferenceEquals(Source, descriptor)) return;

         var replaced = _image;
         Source = descriptor;
         _image = descriptor != null ? new SharedSurfaceImage(descriptor) : null;
         replaced?.ReplaceWith(_image);
      }
      InvalidateAfterSourceChange();
   }

   /// <summary>Unbinds the current source, if any (see <see cref="DropCurrentSource"/>).</summary>
   public void ClearSource()
   {
      lock (_sourceLock)
      {
         if (Source == null) return;
         DropCurrentSource();
      }
      InvalidateAfterSourceChange();
   }

   /// <summary>Unbinds <paramref name="descriptor"/> if it is still the bound source: a producer letting go of its own
   /// surface leaves alone one another producer has bound since.</summary>
   public void ClearSource(SharedSurfaceDescriptor descriptor)
   {
      lock (_sourceLock)
      {
         if (Source == null || !ReferenceEquals(Source, descriptor)) return;
         DropCurrentSource();
      }
      InvalidateAfterSourceChange();
   }

   // The source is bound after this frame's record, whose dirty clear would wipe an inline mark; LoopSignal.Post defers
   // the mark to before the next record.
   private void InvalidateAfterSourceChange()
   {
      if (UIAppContext.Current != null) LoopSignal.Post(() => InvalidateRender(false));
      else InvalidateRender(false);
   }

   // Lets go of the image without freeing its import: the render thread may still build a frame recorded with it, so the
   // import lives until its last holding component is gone (see SharedSurfaceImage.HoldImport).
   private void DropCurrentSource()
   {
      _image?.Dispose();
      _image = null;
      Source = null;
   }

   protected override void OnRender(IDrawingContext context)
   {
      var rect = new Rect(new Size(ActualWidth, ActualHeight));
      var session = context.ForControl(this);
      // Read once: the producer may rebind it from its own thread.
      var image = _image;
      if (image != null)
      {
         session.DrawImage(image, Background, rect, CornerRadius.Empty);
      }
      else
      {
         session.DrawRectangle(Background, rect, CornerRadius.Empty);
      }
   }

   protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
   {
      _leftDown = false;
      _rightDown = false;
      _middleDown = false;
      Disengage();   // a tab switch / tree teardown while looking must restore the cursor
      LetGoOfPointer();
      // The image, and with it the import, stays: imported again on the way back, the surface would be copied from a frame
      // the producer is free to overwrite, since the first import already read it.
      base.OnDetachedFromVisualTree(e);
   }

   // --- Mouse-look: engage relative mouse mode per MouseLookMode ---------------------------------------------------
   // Engage = focus + capture the mouse + ask the window (its platform worker) to hide/center the cursor and start
   // feeding synthesized RawMouseMove deltas. Disengage restores everything. Drag mode brackets it with the button
   // hold; Continuous mode brackets it with focus (click to engage, blur to release).

   private void Engage()
   {
      if (_looking || !IsMouseLookEnabled) return;
      _looking = true;
      // Remember the panel-relative cursor so it reappears there on release; the exact inverse of PointToScreen, so it
      // does not drift across engage cycles.
      _engagePoint = this.PointToClient(Mouse.Pointer.GetScreenPosition());
      Focus();
      Mouse.Pointer.Capture(this);
      (RootVisual as WindowBase)?.SetRelativeMouseMode(true, default);
   }

   private void Disengage()
   {
      if (!_looking) return;
      _looking = false;
      (RootVisual as WindowBase)?.SetRelativeMouseMode(false, this.PointToScreen(_engagePoint));
      LetGoOfPointer();
   }

   private void LetGoOfPointer()
   {
      if (!_looking && !_leftDown && !_rightDown && !_middleDown)
      {
         ReleasePointerCaptures();
      }
   }

   private void OnPointerCaptureLost(object sender, PointerEventArgs e)
   {
      if (IsPointerCaptured)
      {
         return;
      }

      _leftDown = false;
      _rightDown = false;
      _middleDown = false;
      Disengage();
   }

   protected override void OnPointerPressed(PointerPressedEventArgs e)
   {
      base.OnPointerPressed(e);

      if (!ReferenceEquals(e.OriginalSource, this))
      {
         return;
      }

      Focus();
      switch (e.ChangedButton)
      {
         case MouseButtons.Left:
            _leftDown = true;
            e.Pointer.Capture(this);
            if (MouseLookMode == MouseLookMode.Continuous)
            {
               Engage();
            }

            break;

         case MouseButtons.Middle:
            _middleDown = true;
            e.Pointer.Capture(this);
            break;

         case MouseButtons.Right:
            _rightDown = true;
            e.Pointer.Capture(this);
            if (MouseLookMode == MouseLookMode.Drag)
            {
               Engage();
            }
            else if (MouseLookMode == MouseLookMode.Continuous)
            {
               Engage();
            }

            break;
      }
   }

   protected override void OnPointerReleased(PointerReleasedEventArgs e)
   {
      base.OnPointerReleased(e);
      switch (e.ChangedButton)
      {
         case MouseButtons.Left:
            _leftDown = false;
            break;

         case MouseButtons.Middle:
            _middleDown = false;
            break;

         case MouseButtons.Right:
            _rightDown = false;
            if (MouseLookMode == MouseLookMode.Drag)
            {
               Disengage();
            }

            break;

         default:
            return;
      }

      LetGoOfPointer();
   }

   protected override void OnGotFocus(RoutedEventArgs e)
   {
      base.OnGotFocus(e);
      if (MouseLookMode == MouseLookMode.Continuous && ReferenceEquals(e.OriginalSource, this) && FocusManager.IsFocusVisible)
      {
         Engage();
      }
   }

   protected override void OnLostFocus(RoutedEventArgs e)
   {
      base.OnLostFocus(e);
      if (MouseLookMode == MouseLookMode.Continuous) Disengage();
   }
}
