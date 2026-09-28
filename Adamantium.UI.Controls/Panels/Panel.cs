using System.Collections.Specialized;
using Adamantium.Mathematics;
using Adamantium.UI.Controls.Base;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Collections;
using Adamantium.UI.Core.Graphics;
using Adamantium.UI.Core.Input;
using Adamantium.UI.Core.Media;

namespace Adamantium.UI.Controls.Panels;

public abstract class Panel: InputUIComponent, IContainer, INavigablePanel
{
   // A panel is a passive layout container - never a keyboard-focus target. That now comes for free from the
   // Focusable=false default (see InputUIComponent); no per-panel override needed.

   /// <summary>Tab and arrow navigation for a panel without a layout shape: children in tab-index or child order, forward
   /// on Next/Down/Right. Panels with rows or columns override this.</summary>
   public virtual IUIComponent Navigate(IUIComponent from, FocusNavigationDirection direction) =>
      TabNeighbour(from, IsForward(direction));

   /// <summary>The neighbour in TAB order: by <see cref="KeyboardNavigation.TabIndex"/> first, and by the order the
   /// children stand in for the ties. The arrows deliberately do NOT use this - they are a question about the layout,
   /// and an explicit tab order says nothing about which control is physically to the left of another.</summary>
   private IUIComponent TabNeighbour(IUIComponent from, bool forward)
   {
      List<IUIComponent> ordered = null;
      var position = 0;

      foreach (var child in VisualChildren)
      {
         // Skip what is not on screen: a virtualizing panel PARKS an off-screen container (hidden, still a child).
         if (child.Visibility != Visibility.Visible && !ReferenceEquals(child, from)) continue;
         if (KeyboardNavigation.GetTabIndex(child) != int.MaxValue) ordered ??= [];
         position++;
      }

      // Nobody asked for an order: the children's own is the answer, and there is nothing to sort.
      if (ordered == null || position == 0) return Neighbour(from, forward);

      foreach (var child in VisualChildren)
      {
         if (child.Visibility != Visibility.Visible && !ReferenceEquals(child, from)) continue;
         ordered.Add(child);
      }

      var order = new Dictionary<IUIComponent, int>(ordered.Count);
      for (var i = 0; i < ordered.Count; i++) order[ordered[i]] = i;
      ordered.Sort((left, right) =>
      {
         var byIndex = KeyboardNavigation.GetTabIndex(left).CompareTo(KeyboardNavigation.GetTabIndex(right));
         return byIndex != 0 ? byIndex : order[left].CompareTo(order[right]);
      });

      var at = ordered.IndexOf(from);
      if (at < 0) return null;

      var next = at + (forward ? 1 : -1);
      return next >= 0 && next < ordered.Count ? ordered[next] : null;
   }

   /// <summary>The next or previous visual child, or null at either end. Visual rather than <see cref="Children"/>,
   /// since a virtualizing host's containers are visual children only.</summary>
   protected IUIComponent Neighbour(IUIComponent from, bool forward)
   {
      IUIComponent previous = null;
      var passed = false;

      // IReadOnlyCollection, so no indexer: one pass, remembering the last one before and taking the first one after.
      foreach (var child in VisualChildren)
      {
         if (ReferenceEquals(child, from)) { passed = true; continue; }
         // Skip what is not on screen: a virtualizing panel PARKS an off-screen container (hidden, still a child) rather
         // than detaching it, and a parked one is not somewhere the focus can go.
         if (child.Visibility != Visibility.Visible) continue;

         if (passed) return forward ? child : previous;
         previous = child;
      }

      return passed && !forward ? previous : null;
   }

   /// <summary>Down and Right run with the children's order; Up and Left run against it.</summary>
   protected static bool IsForward(FocusNavigationDirection direction) =>
      direction is FocusNavigationDirection.Next or FocusNavigationDirection.Down or FocusNavigationDirection.Right;

   protected static bool IsVertical(FocusNavigationDirection direction) =>
      direction is FocusNavigationDirection.Up or FocusNavigationDirection.Down;

   protected static bool IsArrow(FocusNavigationDirection direction) =>
      direction is not (FocusNavigationDirection.Next or FocusNavigationDirection.Previous);

   public static readonly AdamantiumProperty BackgroundProperty = AdamantiumProperty.Register(nameof(Background),
      typeof(Brush), typeof(Panel),
      new PropertyMetadata(Brushes.Transparent, PropertyMetadataOptions.AffectsRender));

   public Brush Background
   {
      get => GetValue<Brush>(BackgroundProperty);
      set => SetValue(BackgroundProperty, value);
   }

   [Content]
   public MeasurableComponentsCollection Children { get; }

   protected Panel()
   {
      Children = new MeasurableComponentsCollection();
      Children.CollectionChanged += ChildrenChanged;
   }

   private void ChildrenChanged(object sender, NotifyCollectionChangedEventArgs e)
   {
      switch (e.Action)
      {
         case NotifyCollectionChangedAction.Add:
            var controls = e.NewItems.OfType<MeasurableUIComponent>();
            LogicalChildrenCollection.InsertRange(e.NewStartingIndex, controls);
            // Insert at the SAME index (not AddRange/append) so the visual order mirrors Children - an inserted child
            // (e.g. a reordered tab) must keep its slot in the paint order, not jump to the end.
            VisualChildrenCollection.InsertRange(e.NewStartingIndex, e.NewItems.OfType<IUIComponent>());
            break;
         case NotifyCollectionChangedAction.Remove:
            LogicalChildrenCollection.Remove(e.OldItems.OfType<MeasurableUIComponent>());
            VisualChildrenCollection.Remove(e.OldItems.OfType<IUIComponent>());
            break;
         case NotifyCollectionChangedAction.Replace:
            for (var i = 0; i < e.OldItems.Count; ++i)
            {
               var index = LogicalChildrenCollection.IndexOf((MeasurableUIComponent)e.OldItems[i]);
               var child = (MeasurableUIComponent)e.NewItems[i];
               LogicalChildrenCollection[index] = child;
               VisualChildrenCollection[index] = child;
            }
            break;

         case NotifyCollectionChangedAction.Reset:
            LogicalChildrenCollection.Clear();
            VisualChildrenCollection.Clear();
            break;
      }

      InvalidateMeasure();
   }

   protected override void OnRender(IDrawingContext context)
   {
      context.ForControl(this).DrawRectangle(Background, new Rect(new Size(ActualWidth, ActualHeight)));
  }

   // No HitTestCore override: a panel is hit across its bounds with or without a Background. Use IsHitTestVisible="False"
   // to make it pass-through.

   // A panel measures and arranges from Children, so that is where a child taken by another parent has to be removed:
   // dropping it from the visual collection alone would leave the panel still laying out a control it no longer owns.
   protected internal override void DisownVisualChild(IUIComponent child)
   {
      if (child is IMeasurableComponent measurable) Children.Remove(measurable);
      base.DisownVisualChild(child);
   }

   public void AddOrSetChildComponent(object component)
   {
      if (component is IMeasurableComponent measurable)
      {
         Children.Add(measurable);
      }
   }

   public void RemoveAllChildComponents()
   {
      Children.Clear();
   }

   public IReadOnlyList<object> GetChildComponents() => Children.Cast<object>().ToList();

   public void InsertChildComponent(int index, object component)
   {
      if (component is IMeasurableComponent measurable)
         Children.Insert(Math.Clamp(index, 0, Children.Count), measurable);
   }

   public void RemoveChildComponentAt(int index)
   {
      if (index >= 0 && index < Children.Count) Children.RemoveAt(index);
   }
}