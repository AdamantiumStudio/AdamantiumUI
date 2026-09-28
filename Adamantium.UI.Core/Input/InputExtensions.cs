using Adamantium.Mathematics;

namespace Adamantium.UI.Core.Input;

public static class InputExtensions
{
   public static IInputComponent HitTest(this IUIComponent root, Vector2 p)
   {
      return root.GetInputElementsAt(p).FirstOrDefault();
   }

   /// <summary>The input elements under <paramref name="p"/>, front to back (reverse paint order), so
   /// <see cref="HitTest"/> returns the top-most.</summary>
   public static IEnumerable<IInputComponent> GetInputElementsAt(this IUIComponent root, Vector2 p)
   {
      var result = new List<IInputComponent>();
      Collect(root, p, result);
      return result;
   }

   /// <summary>
   /// ALL visual elements under <paramref name="p"/>, front-to-back - INCLUDING non-input visuals (a Border, a Shape, a
   /// TextBlock). Mouse routing must NOT target those (a Border should let clicks fall through to interactive content),
   /// so it uses <see cref="GetInputElementsAt"/>; the DESIGNER, however, must be able to select ANY authored element,
   /// not only interactive ones - that is what this is for.
   /// </summary>
   public static IEnumerable<IUIComponent> GetVisualsAt(this IUIComponent root, Vector2 p)
   {
      var result = new List<IUIComponent>();
      CollectVisuals(root, p, result);
      return result;
   }

   private static void CollectVisuals(IUIComponent element, Vector2 p, List<IUIComponent> result)
   {
      if (element.Visibility != Visibility.Visible
          || !element.IsHitTestVisible)
         return;

      // Same broad-phase rule as Collect: the box gates the SELF hit, but recursion into children is only pruned when
      // the element actually clips them (ClipToBounds), so an overflowing (but rendered) child stays selectable.
      var inBox = element.ClipRectangle.Contains(p);
      if (element.ClipToBounds && !inBox)
         return;

      // True arranged origin, not the (alignment-clamped) ClipRectangle.Location - see the note in Collect: overflowing
      // Center/Right content would otherwise offset every descended hit by the overflow amount.
      var local = p - element.Bounds.Location;
      foreach (var child in HitTestChildren(element, local))
         CollectVisuals(child, local, result);

      // Any visual on its actual geometry is a candidate (not only IInputComponent) - the only difference from the input
      // collector above, so non-interactive authored elements are reachable by the designer's selection.
      if (inBox && element.HitTestCore(local))
         result.Add(element);
   }

   /// <summary>The top-most input element whose bounds contain <paramref name="p"/>, skipping the narrow phase; the
   /// mouse-over fallback when <see cref="HitTest"/> misses.</summary>
   public static IInputComponent HitTestBounds(this IUIComponent root, Vector2 p)
   {
      var result = new List<IInputComponent>();
      Collect(root, p, result, boundsOnly: true);
      return result.FirstOrDefault();
   }

   private static void Collect(IUIComponent element, Vector2 p, List<IInputComponent> result, bool boundsOnly = false)
   {
      if (element.Visibility != Visibility.Visible || !element.IsHitTestVisible)
         return;

      // A disabled element still takes the press (and ignores it) rather than letting it fall through; its children are
      // not walked.
      if (!element.IsEnabled)
      {
         if (element is IInputComponent off && element.ClipRectangle.Contains(p)) result.Add(off);

         return;
      }

      // Undo a render transform first, so the element is hit where it is drawn rather than where it was laid out.
      if (element.RenderTransform != null)
      {
         var back = Matrix4x4F.Invert(element.LocalTransform);
         var mapped = Vector3F.TransformCoordinate(new Vector3F((float)p.X, (float)p.Y, 0), back);
         var own = new Vector2(mapped.X, mapped.Y);
         var size = element.RenderSize;

         // The element's OWN box, since the point is now in its own space - ClipRectangle states the same box in the
         // parent's, which the transform has just moved out from under it.
         var within = own.X >= 0 && own.Y >= 0 && own.X <= size.Width && own.Y <= size.Height;

         if (element.ClipToBounds && !within) return;

         foreach (var child in HitTestChildren(element, own))
            Collect(child, own, result, boundsOnly);

         if (within && element is IInputComponent hit && (boundsOnly || element.HitTestCore(own))) result.Add(hit);

         return;
      }

      // Broad phase. Whether the point is inside THIS element's own box gates the SELF hit below: the default narrow
      // phase (UIComponent.HitTestCore => true, Panel => Background.IsVisible()) ignores the point and trusts this, so
      // it must stay for self-hit or every filled element would register as hit everywhere.
      var inBox = element.ClipRectangle.Contains(p);

      // ...but only a clipping element prunes its children: without ClipToBounds, overflowing children are drawn and must
      // stay hittable.
      if (element.ClipToBounds && !inBox)
         return;

      // Recurse into all visual children, input or not, relative to the true arranged origin (Bounds), not the clamped
      // ClipRectangle origin.
      var local = p - element.Bounds.Location;
      foreach (var child in HitTestChildren(element, local))
         Collect(child, local, result, boundsOnly);

      // Narrow phase: only an INPUT element is a hit target (non-input visuals are pure pass-through containers), the
      // point must be inside its box (broad), and on its actual geometry, not just inside its bounding box - so a click
      // in a shape's empty bbox corner falls through to whatever is really there. boundsOnly skips the geometry test
      // (the mouse-over fallback: bounds containment is enough).
      if (inBox && element is IInputComponent input && (boundsOnly || element.HitTestCore(local)))
         result.Add(input);
   }

   // The children to recurse into for a hit-test, front-to-back. A container that can spatially resolve the point (a
   // virtualizing panel) returns just the candidate(s) - O(1) instead of walking thousands of tiles; else recurse ALL
   // visual children in paint order.
   private static IEnumerable<IUIComponent> HitTestChildren(IUIComponent element, Vector2 local)
   {
      if (element is IHitTestChildren provider)
      {
         var subset = provider.GetHitTestChildren(local);
         if (subset != null) return subset;
      }
      return ZSort(element.VisualChildren);
   }

   // Front-to-back paint order: higher ZIndex first, then later siblings (higher index) first. Fast path: when NO child
   // sets a non-zero ZIndex (the overwhelmingly common case) paint order IS child order, so just walk it in reverse with
   // ZERO allocation - the old unconditional LINQ Select+OrderBy+Select allocated several buffers PER visited node, which
   // over a deep hit-test of thousands of nodes per mouse move was a GC storm (the mouse-move freeze).
   private static IEnumerable<IUIComponent> ZSort(IEnumerable<IUIComponent> elements)
   {
      var list = elements as IReadOnlyList<IUIComponent> ?? elements.ToList();
      var anyZ = false;
      for (var i = 0; i < list.Count; i++)
         if (list[i].ZIndex != 0) { anyZ = true; break; }

      if (!anyZ)
         return ReverseOf(list);

      return list
         .Select((element, index) => (element, index))
         .OrderByDescending(x => x.element.ZIndex)
         .ThenByDescending(x => x.index)
         .Select(x => x.element);
   }

   private static IEnumerable<IUIComponent> ReverseOf(IReadOnlyList<IUIComponent> list)
   {
      for (var i = list.Count - 1; i >= 0; i--)
         yield return list[i];
   }
}
