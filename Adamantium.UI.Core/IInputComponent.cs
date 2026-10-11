using Adamantium.UI.Core.Input;
using Adamantium.UI.Core.RoutedEvents;

namespace Adamantium.UI.Core;

/// <summary>
/// Interface for all elements, which takes user input
/// </summary>
public interface IInputComponent: IMeasurableComponent
{
   /// <summary>
   /// Define is element could receive focus
   /// </summary>
   bool Focusable { get; set; }

   bool IsFocused { get; }

   Cursor Cursor { get; set; }

   /// <summary>True while a pointer is over the element or one of its descendants.</summary>
   bool IsPointerOver { get; }

   /// <summary>True while a pointer is over the element itself, not one of its descendants.</summary>
   bool IsPointerDirectlyOver { get; }

   /// <summary>
   /// Return true if Keyboard focused on element
   /// </summary>
   bool IsKeyboardFocused { get; }

   /// <summary>
   /// Occurs when the keyboard is focused on this element.
   /// </summary>
   event KeyboardGotFocusEventHandler GotKeyboardFocus;

   /// <summary>
   /// Occurs when a key is pressed while the keyboard is focused on this element.
   /// </summary>
   event KeyEventHandler KeyDown;

   /// <summary>
   /// Occurs when a key is released while the keyboard is focused on this element.
   /// </summary>
   event KeyEventHandler KeyUp;

   /// <summary>
   /// Occurs when the keyboard is no longer focused on this element.
   /// </summary>
   event KeyboardFocusChangedEventHandler LostKeyboardFocus;

   /// <summary>Occurs when the element takes a pointer's capture.</summary>
   event PointerEventHandler GotPointerCapture;

   /// <summary>Occurs when the element no longer holds a pointer's capture.</summary>
   event PointerEventHandler PointerCaptureLost;

   /// <summary>Occurs when a pointer comes over the element or one of its descendants.</summary>
   event PointerEventHandler PointerEntered;

   /// <summary>Occurs when a pointer is no longer over the element or any of its descendants.</summary>
   event PointerEventHandler PointerExited;

   /// <summary>Occurs when a pointer moves over the element, or anywhere while the element holds its capture.</summary>
   event PointerEventHandler PointerMoved;

   /// <summary>Occurs when a pointer presses on the element: a mouse button, a finger or a pen tip.</summary>
   event PointerPressedEventHandler PointerPressed;

   /// <summary>Occurs when a pointer lets go over the element.</summary>
   event PointerReleasedEventHandler PointerReleased;

   /// <summary>Occurs when the wheel turns over the element.</summary>
   event PointerWheelEventHandler PointerWheelChanged;

   /// <summary>Occurs when the OS takes a pointer away mid-press - a system gesture claimed the finger.</summary>
   event PointerEventHandler PointerCanceled;

   /// <summary>
   /// Occurs when a drag enters this element. Requires <c>DragDrop.AllowDrop="True"</c> - see <see cref="DragDropEvents"/>.
   /// </summary>
   event DragDropEventHandler DragEnter;

   /// <summary>
   /// Occurs on every move while a drag is over this element - set <c>Effects</c> to say what would happen.
   /// </summary>
   event DragDropEventHandler DragOver;

   /// <summary>
   /// Occurs when a drag leaves this element.
   /// </summary>
   event DragDropEventHandler DragLeave;

   /// <summary>
   /// Occurs when a payload is dropped on this element.
   /// </summary>
   event DragDropEventHandler Drop;

   /// <summary>
   /// Tunnels ahead of <see cref="DragEnter"/> - a container hears about the drag before its content does.
   /// </summary>
   event DragDropEventHandler PreviewDragEnter;

   /// <summary>
   /// Tunnels ahead of <see cref="DragOver"/>; set <c>Handled</c> to answer for the whole subtree.
   /// </summary>
   event DragDropEventHandler PreviewDragOver;

   /// <summary>
   /// Tunnels ahead of <see cref="DragLeave"/>.
   /// </summary>
   event DragDropEventHandler PreviewDragLeave;

   /// <summary>
   /// Tunnels ahead of <see cref="Drop"/>.
   /// </summary>
   event DragDropEventHandler PreviewDrop;

   /// <summary>Occurs on the focused element when the mouse itself moves, unbound by the screen's edges.</summary>
   event RawMouseEventHandler RawMouseMove;

   public event RoutedEventHandler GotFocus;

   public event RoutedEventHandler LostFocus;

   event RoutedEventHandler Loaded;

   public event RoutedEventHandler Unloaded;

   public event RoutedEventHandler Initialized;

   /// <summary>
   /// Occurs when the keyboard is focused on this element.
   /// </summary>
   event KeyboardGotFocusEventHandler PreviewGotKeyboardFocus;

   /// <summary>
   /// Occurs when a key is pressed while the keyboard is focused on this element.
   /// </summary>
   event KeyEventHandler PreviewKeyDown;

   /// <summary>
   /// Occurs when a key is released while the keyboard is focused on this element.
   /// </summary>
   event KeyEventHandler PreviewKeyUp;

   /// <summary>
   /// Occurs when the keyboard is no longer focused on this element.
   /// </summary>
   event KeyboardFocusChangedEventHandler PreviewLostKeyboardFocus;

   /// <summary>Tunnels ahead of <see cref="PointerPressed"/>.</summary>
   event PointerPressedEventHandler PreviewPointerPressed;

   /// <summary>Tunnels ahead of <see cref="PointerReleased"/>.</summary>
   event PointerReleasedEventHandler PreviewPointerReleased;

   /// <summary>Tunnels ahead of <see cref="PointerMoved"/>.</summary>
   event PointerEventHandler PreviewPointerMoved;

   /// <summary>Tunnels ahead of <see cref="PointerWheelChanged"/>.</summary>
   event PointerWheelEventHandler PreviewPointerWheelChanged;

   /// <summary>
   /// Occurs when this element gets text in a device-independent manner.
   /// </summary>
   event TextInputEventHandler TextInput;

   /// <summary>
   /// Occurs when this element gets text in a device-independent manner.
   /// </summary>
   event TextInputEventHandler PreviewTextInput;

   /// <summary>
   /// Attempts to focus the keyboard on this element.
   /// </summary>
   /// <returns>true if keyboard focus is moved to this element or already was on this element;
   /// otherwise, false.</returns>
   bool Focus();
}
