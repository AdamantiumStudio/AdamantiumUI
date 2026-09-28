using Adamantium.Mathematics;
using Adamantium.UI.Core.Input;

namespace Adamantium.UI.Core;

/// <summary>Input settings the USER configured in the OS. Honouring them is what keeps the app feeling native, so they
/// are queried from the platform rather than guessed.</summary>
public static class PlatformSettings
{
   /// <summary>The platform that answers these, registered once at startup; null falls back to the defaults below.</summary>
   public static INativePlatformSettings Platform { get; set; }

   /// <summary>Longest gap between two clicks that still counts as a double-click, in milliseconds. 500 is the default
   /// every desktop OS ships with, and what we use until a platform says otherwise.</summary>
   public static UInt32 DoubleClickTime => Platform?.DoubleClickTime ?? 500;

   /// <summary>How far apart two clicks may land, PER AXIS, and still be one double-click. 4x4 is the desktop default.
   /// A platform reporting zero falls back to it: zero would mean no two clicks ever count as a double one.</summary>
   public static Size DoubleClickSize =>
      Platform?.DoubleClickSize is { Width: > 0, Height: > 0 } size ? size : new Size(4, 4);

   /// <summary>How far the pointer must travel, PER AXIS, before a press becomes a drag - the user's own setting, so a
   /// shaky hand or a high-DPI mouse doesn't turn every click into a drag. 4x4 is the desktop default.</summary>
   public static Size DragThreshold => Platform?.DragThreshold ?? new Size(4, 4);

   /// <summary>How long the pointer must rest before it counts as a HOVER, in milliseconds - the user's dwell
   /// preference, and the pace for every "hold still and it opens" gesture. 400 is the desktop default; a platform
   /// reporting 0 (or none registered) falls back to it.</summary>
   public static UInt32 HoverTime => Platform?.HoverTime is { } time and > 0 ? time : 400;

   /// <summary>Every monitor as one rectangle, in PHYSICAL pixels, or an empty one when the platform does not say.
   /// Used to check that a remembered window position still exists - see <see cref="IsOnScreen"/>.</summary>
   public static Rect VirtualScreen => Platform?.VirtualScreen ?? default;

   /// <summary>Whether a saved window rectangle is still reachable: its top-left and a grabbable caption strip are on a
   /// monitor. True when the platform cannot tell.</summary>
   public static bool IsOnScreen(Rect bounds)
   {
      var screen = VirtualScreen;
      if (screen.Width <= 0 || screen.Height <= 0) return true;

      const double grabbable = 48;
      return bounds.X + bounds.Width - grabbable > screen.X
             && bounds.X + grabbable < screen.X + screen.Width
             && bounds.Y + grabbable < screen.Y + screen.Height
             && bounds.Y + bounds.Height > screen.Y;
   }

   /// <summary>Whether a <paramref name="delta"/> measured in <paramref name="measuredIn"/>'s space exceeds the OS drag
   /// threshold per axis, after converting to physical pixels.</summary>
   public static bool ExceedsDragThreshold(Vector2 delta, IUIComponent measuredIn)
      => ExceedsDragThreshold(delta, PhysicalPerUnit(measuredIn));

   /// <summary>The same question for a distance measured on the DESKTOP - between two cursor positions, say. Both sides
   /// are physical here, so this one needs nothing to convert with.</summary>
   public static bool ExceedsDragThreshold(PixelPoint delta)
      => ExceedsDragThreshold(new Vector2(delta.X, delta.Y), Vector2.One);

   // Where the comparison is actually made, with both sides in PHYSICAL pixels. Internal so the arithmetic can be
   // tested without standing up a window to carry a DPI scale.
   internal static bool ExceedsDragThreshold(Vector2 delta, Vector2 physicalPerUnit)
   {
      var threshold = DragThreshold;
      return Math.Abs(delta.X * physicalPerUnit.X) > threshold.Width
             || Math.Abs(delta.Y * physicalPerUnit.Y) > threshold.Height;
   }

   /// <summary>Physical pixels per unit of <paramref name="element"/>'s space, including scaling ancestors and window
   /// DPI; (1, 1) outside a window.</summary>
   public static Vector2 PhysicalPerUnit(IUIComponent element)
   {
      if (element?.RootVisual is not IWindow window) return Vector2.One;

      var dpi = window.DpiScale;
      if (dpi.X <= 0 || dpi.Y <= 0) dpi = Vector2.One;

      var world = element.WorldTransform;
      var x = Math.Abs(world.M11);
      var y = Math.Abs(world.M22);

      return new Vector2(
         (float)((x > 0 ? x : 1) * dpi.X),
         (float)((y > 0 ? y : 1) * dpi.Y));
   }
}
