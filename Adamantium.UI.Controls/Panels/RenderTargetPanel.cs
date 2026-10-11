using Adamantium.Graphics.Core;
using Adamantium.ProceduralGeometry;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Graphics;
using Adamantium.UI.Core.Input;
using Adamantium.UI.Core.Media.Imaging;
using Adamantium.UI.Core.RoutedEvents;

namespace Adamantium.UI.Controls.Panels;

/// <summary>Displays frames an external producer renders into a shared surface, imported zero-copy via
/// <see cref="SetSource"/>. Interop resources are freed on source change and when the panel leaves the tree.</summary>
public class RenderTargetPanel : Grid
{
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
      if (e.Key == Key.Escape) Disengage();
   }

   /// <summary>
   /// Binds an externally produced surface and triggers a redraw. Any previously bound surface is released first.
   /// Pass <c>null</c> to unbind (equivalent to <see cref="ClearSource"/>).
   /// </summary>
   public void SetSource(SharedSurfaceDescriptor descriptor)
   {
      if (ReferenceEquals(Source, descriptor)) return;

      DropCurrentSource();
      if (descriptor != null)
      {
         Source = descriptor;
         _image = new SharedSurfaceImage(descriptor);
      }
      InvalidateAfterSourceChange();
   }

   /// <summary>Unbinds the current source, if any (see <see cref="DropCurrentSource"/>).</summary>
   public void ClearSource()
   {
      if (Source == null) return;
      DropCurrentSource();
      InvalidateAfterSourceChange();
   }

   // The source is bound after this frame's record, whose dirty clear would wipe an inline mark; LoopSignal.Post defers
   // the mark to before the next record.
   private void InvalidateAfterSourceChange()
   {
      if (UIAppContext.Current != null) LoopSignal.Post(() => InvalidateRender(false));
      else InvalidateRender(false);
   }

   // Drops the source without freeing its import: the sampling render component frees it fence-gated, since the render
   // thread may still be replaying it.
   private void DropCurrentSource()
   {
      _image = null;
      Source = null;
   }

   /// <summary>Drops the source on detach. The imported surface is freed by its render component when the pipeline
   /// removes this panel's units (ReconcileDetachedControls, on the render thread) - never disposed here, off the
   /// render thread, where it could race a compositor submit still in flight.</summary>
   private void ReleaseSource()
   {
      _image = null;
      Source = null;
   }

   protected override void OnRender(IDrawingContext context)
   {
      var rect = new Rect(new Size(ActualWidth, ActualHeight));
      var session = context.ForControl(this);
      if (_image != null)
      {
         // TODO: wait(produce) before sampling and signal(consume) after, wired into the compositor's
         // queue submit (OnRender only records draw commands, so the semaphores can't be driven from here).
         session.DrawImage(_image, Background, rect, CornerRadius.Empty);
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
      ReleaseSource();
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
      _engagePoint = ScreenToPanel(Mouse.Pointer.GetScreenPosition());
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

   // Exact inverse of this.PointToScreen (screen -> panel-relative): the screen point in root-client coords minus the
   // ClipRectangle offsets PointToScreen accumulates, so the two compose to identity (no per-cycle drift).
   private Vector2 ScreenToPanel(PixelPoint screen)
   {
      var rootLocal = RootVisual.PointToClient(screen);
      var offset = default(Mathematics.Vector2);
      for (IUIComponent c = this; c != null && c is not IRootVisualComponent; c = c.VisualParent)
         offset += c.ClipRectangle.Location;
      return rootLocal - offset;
   }

   protected override void OnPointerPressed(PointerPressedEventArgs e)
   {
      base.OnPointerPressed(e);
      switch (e.ChangedButton)
      {
         case MouseButtons.Left:
            _leftDown = true;
            e.Pointer.Capture(this);
            if (MouseLookMode == MouseLookMode.Continuous)
            {
               Focus();
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
               Focus();
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
      if (MouseLookMode == MouseLookMode.Continuous) Engage();
   }

   protected override void OnLostFocus(RoutedEventArgs e)
   {
      base.OnLostFocus(e);
      if (MouseLookMode == MouseLookMode.Continuous) Disengage();
   }
}
