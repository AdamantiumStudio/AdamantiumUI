using Adamantium.Mathematics;
using Adamantium.UI.Controls.Automation;
using Adamantium.UI.Controls.Base;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Automation;
using Adamantium.UI.Core.Input;
using Adamantium.UI.Core.RoutedEvents;

namespace Adamantium.UI.Controls.Primitives;

public class Thumb : Control
{
   public static readonly RoutedEvent DragStartedEvent = EventManager.RegisterRoutedEvent("DragStarted",
      RoutingStrategy.Bubble, typeof (DragStartedEventHandler), typeof (Thumb));

   public static readonly RoutedEvent DragDeltaEvent = EventManager.RegisterRoutedEvent("DragDelta",
      RoutingStrategy.Bubble, typeof(DragEventHandler), typeof(Thumb));

   public static readonly RoutedEvent DragCompletedEvent = EventManager.RegisterRoutedEvent("DragCompleted",
      RoutingStrategy.Bubble, typeof(DragCompletedEventHandler), typeof(Thumb));

   public static readonly AdamantiumProperty IsDraggingProperty = AdamantiumProperty.RegisterReadOnly(nameof(IsDragging),
      typeof (bool), typeof (Thumb), new PropertyMetadata(false));

   public event DragStartedEventHandler DragStarted
   {
      add => AddHandler(DragStartedEvent, value);
      remove => RemoveHandler(DragStartedEvent, value);
   }

   public event DragEventHandler DragDelta
   {
      add => AddHandler(DragDeltaEvent, value);
      remove => RemoveHandler(DragDeltaEvent, value);
   }

   public event DragCompletedEventHandler DragCompleted
   {
      add => AddHandler(DragCompletedEvent, value);
      remove => RemoveHandler(DragCompletedEvent, value);
   }

   public bool IsDragging
   {
      get => GetValue<bool>(IsDraggingProperty);
      private set => SetValue(IsDraggingProperty, value);
   }

   static Thumb()
   {
      DragStartedEvent.RegisterClassHandler<Thumb>(new DragStartedEventHandler(DragStartedHandler));
      DragDeltaEvent.RegisterClassHandler<Thumb>(new DragEventHandler(DragDeltaHandler));
      DragCompletedEvent.RegisterClassHandler<Thumb>(new DragCompletedEventHandler(DragCompletedHandler));
   }

   private static void DragStartedHandler(object sender, DragStartedEventArgs e)
   {
      var thumb = sender as Thumb;
      thumb?.OnDragStarted(e);
   }

   private static void DragDeltaHandler(object sender, DragEventArgs e)
   {
      var thumb = sender as Thumb;
      thumb?.OnDragDelta(e);
   }

   private static void DragCompletedHandler(object sender, DragCompletedEventArgs e)
   {
      var thumb = sender as Thumb;
      thumb?.OnDragCompleted(e);
   }

   protected virtual void OnDragStarted(DragStartedEventArgs e)
   {
   }

   protected virtual void OnDragDelta(DragEventArgs e)
   {
   }

   protected virtual void OnDragCompleted(DragCompletedEventArgs e)
   {
   }

   private List<Size<double>> sizes = new List<Size<double>>();
   public Thumb()
   {
   }

   private Vector2<double> dragStartPoint;

   public void CancelDrag()
   {
      IsDragging = false;
   }

   protected override void OnPointerPressed(PointerPressedEventArgs e)
   {
      base.OnPointerPressed(e);
      if (e.ChangedButton == MouseButtons.Left)
      {
         BeginDrag(e);
      }
   }

   /// <summary>Starts a drag from <paramref name="e"/> as if the press hit this thumb, for owners that route near-misses
   /// to it. Idempotent while a drag is live.</summary>
   public void BeginDrag(PointerPressedEventArgs e)
   {
      if (IsDragging && IsPointerCaptured)
      {
         return;
      }

      IsDragging = true;
      e.Handled = true;
      dragStartPoint = DragPosition(e);
      e.Pointer.Capture(this);
      RaiseEvent(new DragStartedEventArgs(dragStartPoint) { RoutedEvent = DragStartedEvent });
   }

   protected override void OnPointerMoved(PointerEventArgs e)
   {
      e.Handled = true;
      if (!IsDragging)
      {
         return;
      }

      var delta = DragPosition(e) - dragStartPoint;
      RaiseEvent(new DragEventArgs(delta) { RoutedEvent = DragDeltaEvent });
   }

   protected override void OnPointerReleased(PointerReleasedEventArgs e)
   {
      base.OnPointerReleased(e);
      if (e.ChangedButton != MouseButtons.Left)
      {
         return;
      }

      e.Handled = true;
      if (!IsDragging)
      {
         return;
      }

      IsDragging = false;
      e.Pointer.Capture(null);
      var delta = DragPosition(e) - dragStartPoint;
      RaiseEvent(new DragCompletedEventArgs(delta, false) { RoutedEvent = DragCompletedEvent });
   }

   private Vector2<double> DragPosition(PointerEventArgs e)
   {
      IUIComponent root = this;
      while (root.VisualParent is { } parent) root = parent;
      return root is IInputComponent ric ? e.GetPosition(ric) : e.GetPosition(this);
   }

   protected override Size<double> MeasureOverride(Size<double> availableSize)
   {
      sizes.Clear();
      foreach (var child in VisualChildren)
      {
         if (child is IMeasurableComponent measurableComponent)
         {
            measurableComponent.Measure(availableSize);
            sizes.Add(measurableComponent.DesiredSize);
         }
      }

      // No measurable children (e.g. a thumb/splitter with no template applied): desire nothing rather than indexing
      // an empty list. The element's own explicit Width/Height is still applied by Measure (Constrain).
      return sizes.Count > 0 ? sizes[0] : new Size<double>();
   }

   protected override Size<double> ArrangeOverride(Size<double> finalSize)
   {
      // The thumb fills the slot it was arranged into (e.g. the rect a Track computed for it), with its template child
      // - if any - stretched to fill. Returning the child's size instead collapsed an untemplated thumb to nothing.
      foreach (var child in VisualChildren)
      {
         if (child is IMeasurableComponent measurableComponent)
         {
            measurableComponent.Arrange(new Rect(finalSize));
         }
      }

      return finalSize;
   }

   protected override AutomationPeer OnCreateAutomationPeer() =>
      TemplatedParent == null ? new ThumbAutomationPeer(this) : null;

   // protected override void OnRender(DrawingContext context)
   // {
   //    if (!IsGeometryValid)
   //    {
   //       context.DrawRectangle(Background, new Rect(new Size(ActualWidth, ActualHeight)));
   //    }
   // }
}