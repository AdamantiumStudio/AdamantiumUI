namespace Adamantium.UI.Core.Input.Raw;

/// <summary>The kind of a raw pointer event. Values are platform-neutral and arbitrary, not OS message numbers.</summary>
public enum RawMouseEventType : uint
{
   MouseMove,
   /// <summary>The pointer entered the window. No platform raises this yet - the over-chain is derived from moves.</summary>
   EnterWindow,
   LeaveWindow,

   LeftButtonDown,
   LeftButtonUp,
   RightButtonDown,
   RightButtonUp,
   MiddleButtonDown,
   MiddleButtonUp,
   X1ButtonDown,
   X1ButtonUp,
   X2ButtonDown,
   X2ButtonUp,

   // There is deliberately NO double-click type here. A double click is not a thing a platform reports, it is a thing
   // counted from two presses and the time between them - and the counting is done once, in MouseDevice.MouseDown, so
   // that every backend behaves the same and ClickCount is right everywhere. Three members used to sit here that nothing
   // produced and that would have broken ClickCount had anything produced them.

   MouseWheel,

   /// <summary>Relative motion for a universe's mouse-look: an unbounded delta instead of a position, produced while the
   /// cursor is hidden and held centred (see <c>IWindowWorkerService.SetRelativeMouseMode</c>).</summary>
   RawMouseMove,
   RawLeftButtonDown,
   RawRightButtonDown,
   RawMiddleButtonDown,
   RawLeftButtonUp,
   RawRightButtonUp,
   RawMiddleButtonUp,
}
