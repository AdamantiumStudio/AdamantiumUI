using System.Runtime.CompilerServices;
using Adamantium.UI.Core.Automation;
using Adamantium.UI.Core.RoutedEvents;

namespace Adamantium.UI.Core.Input;

public static class FocusManager
{
   public static readonly RoutedEvent GotFocusEvent = EventManager.RegisterRoutedEvent("GotFocus",
      RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(FocusManager));

   /// <summary>Raised on the focused element when <see cref="Reveal"/> shows where the focus is; bubbles up the chain
   /// that holds it.</summary>
   public static readonly RoutedEvent FocusRevealedEvent = EventManager.RegisterRoutedEvent("FocusRevealed",
      RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(FocusManager));

   public static readonly RoutedEvent LostFocusEvent = EventManager.RegisterRoutedEvent("LostFocus",
      RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(FocusManager));

   public static IInputComponent Focused { get; private set; }

   public static IInputComponent Scope { get; private set; }

   // Weakly keyed: a scope dropped without leaving the tree is not kept by having once held the focus.
   private static readonly ConditionalWeakTable<IInputComponent, IInputComponent> focusScopes = new();

   static FocusManager()
   {
      PointerEvents.PreviewPointerPressedEvent.RegisterClassHandler<IInputComponent>(new PointerPressedEventHandler(OnPreviewPointerPressed));
   }

   private static void OnPreviewPointerPressed(object sender, PointerPressedEventArgs e)
   {
      if (e.OriginalSource == e.Source)
      {
         var element = e.OriginalSource as IInputComponent;
         if (element is IFocusableInParts parts && !parts.TakesFocusAt(e))
         {
            element = element.GetSelfAndVisualAncestors().OfType<IInputComponent>().Skip(1).FirstOrDefault(CanFocus);
         }
         else if (element != null && !CanFocus(element))
         {
            // Walk up to the first FOCUSABLE self-or-ancestor (skip non-focusable input parts like a TextBox's inner
            // TextPresenter). FirstOrDefault() without the predicate returns `element` itself (self is first), which is
            // exactly the non-focusable part we're trying to skip - so focus never reached the real control.
            element = element.GetSelfAndVisualAncestors().OfType<IInputComponent>().FirstOrDefault(CanFocus);
         }

         // A CLICK, so the move is announced by Focus itself (see AnnounceFocusMove) - including that this one must not
         // light a focus ring: the click already said where you are.
         if (element != null) Focus(element, NavigationMethod.Mouse);
      }
   }

   private static IEnumerable<IInputComponent> GetFocusScopeAncestors(IInputComponent scope)
   {
      var inputList = scope.GetSelfAndVisualAncestors().OfType<IInputComponent>();
      foreach (var inputElement in inputList)
      {
         if (CanFocus(inputElement))
         {
            yield return inputElement;
         }
      }
   }

   public static Boolean CanFocus(IInputComponent inputComponent)
   {
      return inputComponent != null && inputComponent.IsEnabled && inputComponent.Visibility == Visibility.Visible &&
             inputComponent.Focusable;
   }

   public static void ResetFocus()
   {
      Trace(Focused, null);
      Focused = null;
   }

   // WHO TOOK THE FOCUS. ADAM_FOCUS_TRACE=<path> writes every move of it with the stack that made it - a probe for
   // focus going somewhere nobody asked for, and off unless asked for.
   private static void Trace(IInputComponent from, IInputComponent to)
   {
      if (Environment.GetEnvironmentVariable("ADAM_FOCUS_TRACE") is not { Length: > 0 } path) return;

      try
      {
         System.IO.File.AppendAllText(path,
            $"{DateTime.Now:HH:mm:ss.fff} {Name(from)} -> {Name(to)}{Environment.NewLine}" +
            Environment.StackTrace + Environment.NewLine + Environment.NewLine);
      }
      catch (System.IO.IOException)
      {
      }
   }

   private static string Name(IInputComponent component) =>
      component == null ? "nothing" : $"{component.GetType().Name}({(component as IUIComponent)?.Name})";

   /// <summary>Gives up the focus if it is on <paramref name="element"/>, announcing the move so the ring goes out with
   /// it. What a control calls when it LEAVES the visual tree: the focus cannot stay on something that is no longer on
   /// screen - the keyboard would go on walking a tree nobody can see (measured: after a tab swap, Tab kept moving
   /// through the page that had just been replaced).</summary>
   public static void Release(IInputComponent element)
   {
      if (element == null)
         return;

      // Clear the keyboard device's pointer too: it is an app-lifetime singleton and would keep a removed element alive.
      var keyboard = KeyboardDevice.CurrentDevice;
      if (keyboard != null && ReferenceEquals(keyboard.FocusedComponent, element))
      {
         keyboard.SetFocusedElement(null);
      }

      // ...and the scope map, which is written on every focus move. Its key is weak but what a scope remembers is not,
      // so a departed element would sit there as a live scope's remembered element.
      DropFromScopes(element);

      if (!ReferenceEquals(Focused, element))
         return;

      var previous = Focused;
      Focused = null;
      AnnounceFocusMove(previous, null, NavigationMethod.Unspecified);
   }

   /// <summary>Moves the focus out of <paramref name="hidden"/>, which has just stopped being shown, onto the nearest
   /// ancestor that can hold it - so keys go on reaching something on screen, as they reach a game's surface again once
   /// the menu laid over it closes. With no ancestor to take it the focus is let go of, so no key reaches a hidden
   /// element.</summary>
   public static void LeaveHidden(IUIComponent hidden)
   {
      if (Focused == null || !IsWithin(Focused, hidden))
      {
         return;
      }

      for (var node = hidden.VisualParent; node != null; node = node.VisualParent)
      {
         if (node is IInputComponent candidate && CanFocus(candidate))
         {
            Return(candidate, IsFocusVisible);
            return;
         }
      }

      Release(Focused);
   }

   private static bool IsShown(IUIComponent element)
   {
      for (var node = element.VisualParent; node != null; node = node.VisualParent)
      {
         if (node.Visibility != Visibility.Visible)
         {
            return false;
         }
      }

      return true;
   }

   private static bool IsWithin(IUIComponent element, IUIComponent container)
   {
      for (var node = element; node != null; node = node.VisualParent)
      {
         if (ReferenceEquals(node, container))
         {
            return true;
         }
      }

      return false;
   }

   private static void DropFromScopes(IInputComponent element)
   {
      List<IInputComponent> stale = null;
      foreach (var pair in (IEnumerable<KeyValuePair<IInputComponent, IInputComponent>>)focusScopes)
      {
         if (ReferenceEquals(pair.Key, element) || ReferenceEquals(pair.Value, element))
            (stale ??= new List<IInputComponent>()).Add(pair.Key);
      }

      if (stale == null) return;
      foreach (var scope in stale) focusScopes.Remove(scope);
   }

   public static bool Focus(IInputComponent component, NavigationMethod navigationMethod = NavigationMethod.Unspecified,
      InputModifiers modifiers = InputModifiers.None)
   {
      if (component != null)
      {
         // Focusable=false is honored on every path, not just mouse clicks.
         if (!CanFocus(component)) return false;

         if (ReferenceEquals(Focused, component)) return true;

         var scope = GetFocusScopeAncestors(component).FirstOrDefault();
         var previous = Focused;
         Trace(previous, component);
         Focused = component;
         Remember(component);   // per WINDOW, so switching away and back comes back HERE - see FocusByRoot

         // Announce the move HERE, not only on the mouse path. Focus set programmatically - which is every keyboard
         // move, and every control that focuses itself - used to change nothing visible at all, because the events that
         // drive IsFocused were raised by the mouse handler alone.
         AnnounceFocusMove(previous, component, navigationMethod);

         if (scope != null)
         {
            Scope = scope;
            SetFocusedElement(component, scope, navigationMethod, modifiers);
            return true;
         }
      }
      else if (Focused != null)
      {
         // If control is null, set focus to the topmost focus scope.
         foreach (var scope in GetFocusScopeAncestors(Focused).Reverse().ToList())
         {
            if (focusScopes.TryGetValue(scope, out var remembered))
            {
               Focus(remembered, navigationMethod, modifiers);
            }
         }
      }
      return false;
   }

   /// <summary>True while the focus was last moved BY THE KEYBOARD. What a focus visual keys off: a ring that also
   /// appeared on every click is noise, since a click already says where you are.</summary>
   public static bool IsFocusVisible { get; private set; }

   /// <summary>Shows where the focus is without moving it. A focus the mouse put somewhere wears no ring, and the first
   /// Tab asks for the ring rather than leaving. False when there is nothing to show.</summary>
   public static bool Reveal()
   {
      if (Focused == null || IsFocusVisible)
      {
         return false;
      }

      IsFocusVisible = true;
      Focused.RaiseEvent(new RoutedEventArgs(FocusRevealedEvent));
      return true;
   }

   /// <summary>True while <see cref="Return"/> is putting the focus back: nothing is scrolled to bring it into view.</summary>
   public static bool IsReturning { get; private set; }

   /// <summary>Puts the focus back on <paramref name="component"/> - a row a list shows again - with the ring only if it
   /// had one. A return is not a move: nothing is scrolled to it.</summary>
   public static bool Return(IInputComponent component, bool ringVisible)
   {
      IsReturning = true;
      try
      {
         return Focus(component, ringVisible ? NavigationMethod.Directional : NavigationMethod.Unspecified);
      }
      finally
      {
         IsReturning = false;
      }
   }

   // One place that tells everyone the focus moved: the two elements themselves, and every element that gained or lost
   // "the focus is somewhere inside me" (the same ancestor-chain machinery IsPointerOver runs on - a composite control has
   // to know the focus is in its editor without being the focused element itself).
   private static void AnnounceFocusMove(IInputComponent previous, IInputComponent current, NavigationMethod method)
   {
      IsFocusVisible = method is NavigationMethod.Tab or NavigationMethod.Directional;

      // A fresh args per raise: RaiseEvent only fills Source/OriginalSource when they are null, so one shared object
      // would carry the LOST element into the GOT event - and IsFocused is derived from exactly that.
      previous?.RaiseEvent(new RoutedEventArgs(LostFocusEvent));
      current?.RaiseEvent(new RoutedEventArgs(GotFocusEvent));

      AncestorState.Transition(previous, current,
         Keyboard.GotKeyboardFocusWithinEvent, Keyboard.LostKeyboardFocusWithinEvent,
         evt => new RoutedEventArgs(evt));

      if (current != null && AutomationEvents.IsListening)
      {
         AutomationEvents.Raise(current, AutomationEvent.FocusChanged);
      }
   }

   // The last focused element per window, restored when that window is activated; weak so closed windows can go.
   private static readonly ConditionalWeakTable<IUIComponent, IInputComponent> FocusByRoot = new();

   private static IUIComponent RootOf(IUIComponent node)
   {
      while (node?.VisualParent is { } parent) node = parent;
      return node;
   }

   private static void Remember(IInputComponent element)
   {
      if (RootOf(element) is not { } root) return;
      FocusByRoot.Remove(root);
      FocusByRoot.Add(root, element);
   }

   /// <summary>Puts the focus back where it was in <paramref name="root"/> - what a window does when it is activated.
   /// False when this window has no place to go back to (it has never been focused, or what it remembers is gone), which
   /// is how the caller knows to enter it at its first stop instead.</summary>
   public static bool TryRestoreFocus(IInputComponent root)
   {
      if (root == null || !FocusByRoot.TryGetValue(root, out var remembered) || !CanFocus(remembered)
          || !IsShown(remembered))
      {
         return false;
      }

      // It has to still be IN this window: the remembered element may have been removed with a closed tab, or moved to
      // another window entirely by a tear-off.
      if (!ReferenceEquals(RootOf(remembered), root))
         return false;

      return Focus(remembered);
   }

   /// <summary>The window is no longer active: remember where the focus was in it and let it go. Keeping it would leave
   /// the ring lit in a window the keyboard has left, and leave the ONE focused element pointing into a window that no
   /// longer receives keys - which is what stopped the newly activated window from responding at all.</summary>
   public static void LeaveWindow(IUIComponent root)
   {
      if (Focused == null || !ReferenceEquals(RootOf(Focused), root))
         return;

      Remember(Focused);
      Release(Focused);
   }

   public static void SetFocusedElement(IInputComponent component, IInputComponent scope,
      NavigationMethod navigationMethod = NavigationMethod.Unspecified,
      InputModifiers modifiers = InputModifiers.None)
   {
      if (scope == null)
      {
         throw new ArgumentNullException(nameof(scope));
      }

      focusScopes.AddOrUpdate(scope, component);

      if (Scope == scope)
      {
         KeyboardDevice.CurrentDevice.SetFocusedElement(component, navigationMethod, modifiers);
      }
   }
}