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

   /// <summary>Marks an element - a menu, a ribbon, a popup - that keeps a focus of its own: a command from inside it
   /// that nothing inside takes goes to the element with the focus around it, the field being edited.</summary>
   public static readonly AdamantiumProperty IsFocusScopeProperty = AdamantiumProperty.RegisterAttached("IsFocusScope",
      typeof(bool), typeof(AdamantiumComponent), new PropertyMetadata(false));

   /// <summary>Marks a focus scope whose commands give the focus back, as a ribbon's do: once a button, a drop-down or
   /// a gallery inside it has run its command, the focus goes back to the element that has it in the scope
   /// around - the field being edited.</summary>
   public static readonly AdamantiumProperty ReturnsFocusProperty = AdamantiumProperty.RegisterAttached("ReturnsFocus",
      typeof(bool), typeof(AdamantiumComponent), new PropertyMetadata(false));

   // What had the focus last in each focus scope, and in each window outside every scope; weak both ways.
   private static readonly ConditionalWeakTable<IUIComponent, WeakReference<IInputComponent>> focusedInScope = new();

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
      AnnounceFocusMove(previous, null, NavigationMethod.Unspecified, InputModifiers.None);
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

      // Let go of, not moved by a click: the keyboard is as much in use as it was, for what takes the focus next.
      var ringVisible = IsFocusVisible;
      Release(Focused);
      IsFocusVisible = ringVisible;
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
         AnnounceFocusMove(previous, component, navigationMethod, modifiers);

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

   /// <summary>Shows where the focus is without moving it, and marks the keyboard as in use until the focus next moves,
   /// for whoever asks <see cref="IsFocusVisible"/> before moving it - a menu opening. A focus the mouse put somewhere
   /// wears no ring; the first navigation key and Alt ask for the ring rather than moving. False when there is nothing
   /// to show.</summary>
   public static bool Reveal()
   {
      if (IsFocusVisible)
      {
         return false;
      }

      IsFocusVisible = true;
      if (Focused == null)
      {
         return false;
      }

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
   private static void AnnounceFocusMove(IInputComponent previous, IInputComponent current, NavigationMethod method,
      InputModifiers modifiers)
   {
      IsFocusVisible = method is NavigationMethod.Tab or NavigationMethod.Directional;

      // A fresh args per raise: RaiseEvent only fills Source/OriginalSource when they are null, so one shared object
      // would carry the LOST element into the GOT event - and IsFocused is derived from exactly that. The GOT one says
      // how the focus came, and with which modifiers: Shift+Tab arriving at a field of parts starts at its last.
      previous?.RaiseEvent(new RoutedEventArgs(LostFocusEvent));
      current?.RaiseEvent(new KeyboardGotFocusEventArgs(previous, current, method, modifiers) { RoutedEvent = GotFocusEvent });

      AncestorState.Transition(previous, current,
         Keyboard.GotKeyboardFocusWithinEvent, Keyboard.LostKeyboardFocusWithinEvent,
         evt => new RoutedEventArgs(evt));

      if (current != null && AutomationEvents.IsListening)
      {
         AutomationEvents.Raise(current, AutomationEvent.FocusChanged);
      }

      if (current is IUIComponent element && GetFocusScope(element) is { } scope)
      {
         focusedInScope.AddOrUpdate(scope, new WeakReference<IInputComponent>(current));
      }

      CommandManager.InvalidateRequerySuggested();
   }

   /// <summary>The modifiers held as the focus came, as <paramref name="gotFocus"/> - a <see cref="GotFocusEvent"/> -
   /// carries them, and as the keyboard holds them: a key that moved it says so itself, and focus given by a control in
   /// answer to a key it handled (a grid's cell editor on Shift+Tab) is read off the keyboard.</summary>
   public static InputModifiers ModifiersOf(RoutedEventArgs gotFocus) =>
      (gotFocus is KeyboardGotFocusEventArgs focus ? focus.Modifiers : InputModifiers.None) | Keyboard.Modifiers;

   public static bool GetIsFocusScope(IAdamantiumComponent element) => element.GetValue<bool>(IsFocusScopeProperty);

   public static void SetIsFocusScope(IAdamantiumComponent element, bool value) =>
      element.SetValue(IsFocusScopeProperty, value);

   public static bool GetReturnsFocus(IAdamantiumComponent element) => element.GetValue<bool>(ReturnsFocusProperty);

   public static void SetReturnsFocus(IAdamantiumComponent element, bool value) =>
      element.SetValue(ReturnsFocusProperty, value);

   // Where the focus goes back to once a command from source has run: the element that has it in the first scope
   // around that does not give it back itself (a quick-access bar sits inside a ribbon); null when source's scope keeps it.
   internal static IInputComponent FocusToReturnTo(IUIComponent source)
   {
      var scope = GetFocusScope(source);
      if (scope == null || !GetReturnsFocus(scope))
      {
         return null;
      }

      while (GetReturnsFocus(scope))
      {
         if (ParentOf(scope) is not { } around)
         {
            return null;
         }

         scope = GetFocusScope(around);
      }

      return GetFocusedElement(scope);
   }

   // Whether a command from source gives the focus back at all: its scope does, or one around it - a ribbon's drop-down
   // is a scope of its own inside the ribbon's.
   internal static bool ReturnsFocusFrom(IUIComponent source)
   {
      for (var scope = GetFocusScope(source); scope != null; scope = ParentOf(scope) is { } around ? GetFocusScope(around) : null)
      {
         if (GetReturnsFocus(scope))
         {
            return true;
         }
      }

      return false;
   }

   // Back to field after a command - unless the command moved the focus from where it was before (a search box, a
   // dialog); gone with a source taken out of the tree counts as not moved.
   internal static void ReturnFocus(IInputComponent field, IInputComponent before)
   {
      var focused = Focused;
      if (field != null && !ReferenceEquals(field, focused) && (focused == null || ReferenceEquals(focused, before)))
      {
         field.Focus();
      }
   }

   /// <summary>The focus scope <paramref name="element"/> is in: the nearest element marked
   /// <see cref="IsFocusScopeProperty"/> at or above it, else the top of its tree - its window. A popup's content is in
   /// the popup, though it is shown on the window's overlay.</summary>
   public static IUIComponent GetFocusScope(IUIComponent element)
   {
      IUIComponent top = null;
      for (var node = element; node != null; node = ParentOf(node))
      {
         if (GetIsFocusScope(node))
         {
            return node;
         }

         top = node;
      }

      return top;
   }

   /// <summary>What had the focus last inside <paramref name="scope"/> (a focus scope or a window), while it is still
   /// there, shown, and can have it.</summary>
   public static IInputComponent GetFocusedElement(IUIComponent scope)
   {
      if (scope == null || !focusedInScope.TryGetValue(scope, out var reference) ||
          !reference.TryGetTarget(out var focused) || !CanFocus(focused) || focused is not IUIComponent element ||
          !IsShown(element) || !ReferenceEquals(GetFocusScope(element), scope))
      {
         return null;
      }

      return focused;
   }

   /// <summary>The element above <paramref name="element"/>: its visual parent, or for a popup's content, which has
   /// none, the popup that shows it.</summary>
   public static IUIComponent ParentOf(IUIComponent element) =>
      element.VisualParent ?? (element as IFundamentalUIComponent)?.LogicalParent as IUIComponent;

   // The last focused element per window, restored when that window is activated; weak so closed windows can go.
   private static readonly ConditionalWeakTable<IUIComponent, IInputComponent> FocusByRoot = new();

   // The window an element is in - through the popup that shows it, for a popup's content, whose visual tree ends at
   // its overlay root: focus in a window's popup is that window's to remember, release and restore.
   private static IUIComponent RootOf(IUIComponent node)
   {
      while (node != null && ParentOf(node) is { } parent) node = parent;
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

   /// <summary>The window is active: the focus goes back where it was in it - in one of its popups too - and a window
   /// seen for the first time, with nowhere to go back to, starts at its first stop; otherwise it would come up with no
   /// focus at all and stay dead until something in it was clicked. Unspecified, not Tab: activation is not a keyboard
   /// move, so no ring lights up.</summary>
   public static void EnterWindow(IInputComponent root)
   {
      if (!TryRestoreFocus(root))
      {
         KeyboardNavigation.MoveInto(root, NavigationMethod.Unspecified);
      }
   }

   /// <summary>The window is no longer active: remember where the focus was in it and let it go. Keeping it would leave
   /// the ring lit in a window the keyboard has left, and leave the ONE focused element pointing into a window that no
   /// longer receives keys - which is what stopped the newly activated window from responding at all.</summary>
   public static void LeaveWindow(IUIComponent root)
   {
      if (Focused == null || !ReferenceEquals(RootOf(Focused), root))
         return;

      Remember(Focused);
      IsLeavingWindow = true;
      try
      {
         Release(Focused);
      }
      finally
      {
         IsLeavingWindow = false;
      }
   }

   /// <summary>True while <see cref="LeaveWindow"/> lets the focus go: the element will get it back when the window is
   /// activated again, so what it holds for the keyboard - a selection, say - is kept.</summary>
   public static bool IsLeavingWindow { get; private set; }

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