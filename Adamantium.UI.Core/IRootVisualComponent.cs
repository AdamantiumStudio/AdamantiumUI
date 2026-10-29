using Adamantium.Mathematics;

namespace Adamantium.UI.Core;

public interface IRootVisualComponent : IUIComponent
{
   /// <summary>This surface is drawn ONCE - a bitmap bake, a designer preview, an off-screen test - so nothing inside it
   /// may leave work for "the next frame": there is none. A window says false, and its content is free to arrive over the
   /// frames that follow (see ContentPresenter.DeferContent).</summary>
   bool RendersOnce => false;

   /// <summary>A desktop point (physical pixels) in this surface's own LOGICAL coordinates. The one place the two
   /// units meet, and it needs this surface's scale to do it - see <see cref="PixelPoint"/>.</summary>
   Vector2<double> PointToClient(PixelPoint point);

   /// <summary>A point of this surface's own LOGICAL coordinates as a desktop point (physical pixels).</summary>
   PixelPoint PointToScreen(Vector2<double> point);

   void AttachContextAndInitialize(IUIContext context);

   /// <summary>The window of the operating system this root is shown in: a window is its own; an overlay window is shown
   /// in the window that holds it. What needs the real window - its scale, its input method - asks here.</summary>
   IWindow Window => this as IWindow;

   /// <summary>Where this root sits in what holds it: a window on the DESKTOP, in physical pixels; an overlay window in
   /// the window it is shown in. <see cref="Left"/>/<see cref="Top"/> are the same place as bindable numbers - a
   /// position is authored and serialized as two numbers - so anything that COMPUTES a position works with this, and
   /// only the property boundary is loose. A point on the desktop goes through <see cref="PointToClient"/>.</summary>
   PixelPoint Position { get; set; }

   /// <summary>The position as the OS last reported it, readable from any thread without the lag of
   /// <see cref="Position"/>; used for drawing, e.g. Mica.</summary>
   PixelPoint LivePosition => Position;

   double Left { get; set; }

   double Top { get; set; }
        
   string Title { get; set; }
   
   Double ClientWidth { get; set; }

   Double ClientHeight { get; set; }
   
   IUIContext UIContext { get; }
}