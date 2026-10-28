using Adamantium.Mathematics;

namespace Adamantium.UI.Core;

public static class UIExtensions
{
   /// <summary>
   /// Enumerates the ancestors of an <see cref="IUIComponent"/> in the visual tree.
   /// </summary>
   /// <param name="visualComponent"></param>
   /// <returns>The visual's ancestors.</returns>
   public static IEnumerable<IUIComponent> GetVisualAncestors(this IUIComponent visualComponent)
   {
      visualComponent = visualComponent.VisualParent;

      while (visualComponent != null)
      {
         yield return visualComponent;
         visualComponent = visualComponent.VisualParent;
      }
   }


   public static IEnumerable<IUIComponent> GetSelfAndVisualAncestors(this IUIComponent visualComponent)
   {
      yield return visualComponent;

      foreach (var ancestor in visualComponent.GetVisualAncestors())
      {
         yield return ancestor;
      }
   }

   public static T GetVisualParent<T>(this IUIComponent visualComponent) where T : class
   {
      return visualComponent.VisualParent as T;
   }

   // The logical parent, or for a template part (which has none) its TemplatedParent, giving a continuous logical chain.
   public static IFundamentalUIComponent GetLogicalParentOrBridge(this IFundamentalUIComponent component)
      => component.LogicalParent ?? component.TemplatedParent as IFundamentalUIComponent;

   /// <summary>Enumerates the logical ancestors of a component, bridging template boundaries via TemplatedParent.</summary>
   public static IEnumerable<IFundamentalUIComponent> GetLogicalAncestors(this IFundamentalUIComponent component)
   {
      component = component.GetLogicalParentOrBridge();

      while (component != null)
      {
         yield return component;
         component = component.GetLogicalParentOrBridge();
      }
   }

   public static IEnumerable<IFundamentalUIComponent> GetSelfAndLogicalAncestors(this IFundamentalUIComponent component)
   {
      yield return component;

      foreach (var ancestor in component.GetLogicalAncestors())
      {
         yield return ancestor;
      }
   }

   /// <summary>A desktop point (physical) in this element's OWN logical coordinates: converted at the root, then back
   /// down through every offset and render or layout transform between it and the element.</summary>
   public static Vector2<double> PointToClient(this IUIComponent visualComponent, PixelPoint point)
   {
      var root = RootOf(visualComponent);
      return root.TranslatePoint(root.PointToClient(point), visualComponent);
   }

   /// <summary>A point of this element's own logical coordinates as a desktop point (physical pixels), through every
   /// offset and render or layout transform between the element and its root.</summary>
   public static PixelPoint PointToScreen(this IUIComponent visualComponent, Vector2<double> point)
   {
      var root = RootOf(visualComponent);
      return root.PointToScreen(visualComponent.TranslatePoint(point, root));
   }

   private static IRootVisualComponent RootOf(IUIComponent visualComponent)
   {
      while (visualComponent is not IRootVisualComponent)
      {
         visualComponent = visualComponent.VisualParent
                           ?? throw new InvalidOperationException("Control is not attached to visual tree.");
      }

      return (IRootVisualComponent)visualComponent;
   }
}