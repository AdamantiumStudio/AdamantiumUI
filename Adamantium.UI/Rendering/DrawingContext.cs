using System;
using System.Collections.Generic;
using Adamantium.Graphics.Fonts;
using Adamantium.Mathematics;
using Adamantium.ProceduralGeometry;
using Adamantium.ProceduralGeometry.Shapes;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Graphics;
using Adamantium.UI.Core.Media;
using Adamantium.UI.Core.Media.Imaging;
using Adamantium.UI.Rendering.Payloads;

namespace Adamantium.UI.Rendering;

public class DrawingContext : IDrawingContext, IDrawingContextInternal, IDrawingSession
{
   private IUIComponent _currentComponent;
   
   private readonly List<DrawCommand> drawCommands = new List<DrawCommand>();

   internal DrawingContext()
   {
   }

   // The placement the draw being recorded moves with: a placed overload records through the matrix overload and marks
   // what it records, so the two never part ways.
   private Placement _placing;

   protected void CreateCommand(object payload)
   {
      if (_placing != null && payload is IPlacedPayload placed)
      {
         placed.PlaceBy(_placing);
      }

      var command = new DrawCommand(
         _currentComponent,
         _currentComponent.RenderId,
         payload,
         GetRenderDataFromComponent());
      drawCommands.Add(command);
   }

   public void Clear()
   {
      drawCommands.Clear();
      _openGroups = 0;
      _clip = null;
      _clipsPushed = 0;
   }

   private PlacedClip _clip;
   private int _clipsPushed;

   // Groups the element being recorded has pushed and not yet popped.
   private int _openGroups;

   // Inside a group's masks: what is drawn there is drawn at full strength, with no bands (RenderData.DrawsMask).
   private int _drawingMask;

   // Per-frame WorldTransform memo: the property recomposes the O(depth) chain on every call, and recording asks once
   // per draw command. Composed exactly as the property does.
   private readonly Dictionary<IUIComponent, Matrix4x4F> _worldMemo = new();

   public void BeginRecordFrame() => _worldMemo.Clear();

   private Matrix4x4F World(IUIComponent c)
   {
      if (_worldMemo.TryGetValue(c, out var world)) return world;

      var parent = c.RenderParent;
      world = parent != null ? c.LocalTransform * World(parent) : c.LocalTransform;
      _worldMemo[c] = world;
      return world;
   }

   public IReadOnlyList<IDrawCommand> GetDrawCommands()
   {
      // Taken once the element has drawn: a group it left open ends with its drawing.
      while (_openGroups > 0)
      {
         ((IDrawingSession)this).PopGroup();
      }

      _clip = null;
      return drawCommands;
   }

   public IDrawingSession ForControl(IUIComponent component)
   {
      _currentComponent = component;
      return this;
   }

   IDrawingSession IDrawingSession.DrawLine(Vector2<double> start, Vector2<double> end, Pen pen)
   {
      var payload = new LinePayload(start, end, pen);
      CreateCommand(payload);
      return this;
   }

   IDrawingSession IDrawingSession.DrawRectangle(Brush brush, Rect destinationRect, Pen pen)
   {
      return ((IDrawingSession)this).DrawRectangle(brush, destinationRect, CornerRadius.Empty, pen);
   }

   IDrawingSession IDrawingSession.DrawBorder(Brush background, Rect destinationRect, CornerRadius corners,
      Brush borderBrush, Thickness borderThickness)
   {
      var hasBorder = borderBrush.IsVisible()
         && (borderThickness.Left > 0 || borderThickness.Top > 0 || borderThickness.Right > 0 || borderThickness.Bottom > 0);
      // No border to draw -> this IS a plain rect, and saying so keeps one shape one payload (a frame payload with a
      // zero border would take the frame path in the shader for nothing).
      if (!hasBorder)
      {
         return ((IDrawingSession)this).DrawRectangle(background, destinationRect, corners);
      }

      var payload = new RectanglePayload(background, destinationRect, corners, borderBrush, borderThickness)
      {
         AntiAlias = _currentComponent?.UseAnalyticAA ?? true
      };
      CreateCommand(payload);
      return this;
   }

   IDrawingSession IDrawingSession.DrawRectangle(Brush brush, Rect destinationRect, CornerRadius corners, Pen pen)
   {
      // No visible fill and no pen: create no render unit (transparent is the default Background; hit-testing uses
      // bounds).
      if (!brush.IsVisible() && pen == null)
         return this;

      // The payload keeps the LIVE brush/pen and reads their immutable snapshots (Brush.Snapshot): a brush is animatable,
      // and a paint change repaints by RE-BAKING this payload rather than re-recording the element - so a payload holding a
      // snapshot taken here would pin the color forever.
      var payload = new RectanglePayload(
      brush,
      destinationRect,
      corners,
      pen)
      {
         // Whether this shape wants a fringe is the COMPONENT's answer, and here is the only place the component is
         // known - by record time it is gone.
         AntiAlias = _currentComponent?.UseAnalyticAA ?? true
      };
      CreateCommand(payload);

      return this;
   }

   IDrawingSession IDrawingSession.DrawEllipse(Rect destinationRect,
      Brush brush,
      double startAngle,
      double sweepAngle,
      EllipseType ellipseType,
      Pen pen,
      double ringThickness)
   {
      var payload = new EllipsePayload(
         brush,
         destinationRect,
         startAngle,
         sweepAngle,
         ellipseType,
         pen
      )
      {
         RingThickness = ringThickness
      };
      CreateCommand(payload);
      return this;
   }

   IDrawingSession IDrawingSession.DrawRegularPolygon(Brush brush, Rect destinationRect, int corners, Pen pen, double ringThickness, double startAngle)
   {
      // Same invisible-draw skip as DrawRectangle: nothing to fill and no stroke records nothing.
      if (!brush.IsVisible() && pen == null) return this;

      var payload = new RegularPolygonPayload(brush, destinationRect, corners, pen)
      {
         RingThickness = ringThickness,
         StartAngle = startAngle
      };
      CreateCommand(payload);
      return this;
   }

   IDrawingSession IDrawingSession.DrawGeometry(Brush brush, Geometry geometry, Pen pen)
   {
      // Same invisible-draw skip as DrawRectangle: a transparent/null fill with no stroke records nothing.
      if (!brush.IsVisible() && pen == null) return this;
      var payload = new GeometryPayload(brush, geometry, pen);
      CreateCommand(payload);
      return this;
   }

   IDrawingSession IDrawingSession.DrawGeometry(Brush brush, Geometry geometry, Pen pen, Matrix4x4F transform)
   {
      if (!brush.IsVisible() && pen == null) return this;
      CreateCommand(new GeometryPayload(brush, geometry, pen, transform));
      return this;
   }

   IDrawingSession IDrawingSession.DrawGeometry(Brush brush, Geometry geometry, Pen pen, Placement placement)
   {
      if (placement == null) return ((IDrawingSession)this).DrawGeometry(brush, geometry, pen);

      BeginPlaced(placement);
      try
      {
         return ((IDrawingSession)this).DrawGeometry(brush, geometry, pen, placement.Matrix);
      }
      finally
      {
         _placing = null;
      }
   }

   IDrawingSession IDrawingSession.DrawRectangle(Brush brush, Rect destinationRect, CornerRadius corners, Pen pen,
      Placement placement)
   {
      if (placement == null) return ((IDrawingSession)this).DrawRectangle(brush, destinationRect, corners, pen);
      if (!brush.IsVisible() && pen == null) return this;

      BeginPlaced(placement);
      try
      {
         CreateCommand(new RectanglePayload(brush, destinationRect, corners, pen)
         {
            AntiAlias = _currentComponent?.UseAnalyticAA ?? true
         });
      }
      finally
      {
         _placing = null;
      }

      return this;
   }

   IDrawingSession IDrawingSession.DrawEllipse(Rect destinationRect, Brush brush, Pen pen, Placement placement)
   {
      if (placement == null)
      {
         return ((IDrawingSession)this).DrawEllipse(destinationRect, brush, 0, 360, EllipseType.Sector, pen);
      }

      if (!brush.IsVisible() && pen == null) return this;

      BeginPlaced(placement);
      try
      {
         CreateCommand(new EllipsePayload(brush, destinationRect, 0, 360, EllipseType.Sector, pen));
      }
      finally
      {
         _placing = null;
      }

      return this;
   }

   IDrawingSession IDrawingSession.DrawRegularPolygon(Brush brush, Rect destinationRect, int corners, double startAngle,
      Pen pen, Placement placement)
   {
      if (placement == null)
      {
         return ((IDrawingSession)this).DrawRegularPolygon(brush, destinationRect, corners, pen, 0, startAngle);
      }

      if (!brush.IsVisible() && pen == null) return this;

      BeginPlaced(placement);
      try
      {
         CreateCommand(new RegularPolygonPayload(brush, destinationRect, corners, pen) { StartAngle = startAngle });
      }
      finally
      {
         _placing = null;
      }

      return this;
   }

   private void BeginPlaced(Placement placement)
   {
      placement.DrawnBy(_currentComponent);
      _placing = placement;
   }

   IDrawingSession IDrawingSession.DrawImage(ImageSource image, Brush filter, Rect destinationRect, CornerRadius corners)
   {
      var payload = new ImagePayload(filter, image, destinationRect, corners);
      CreateCommand(payload);
      return this;
   }

   IDrawingSession IDrawingSession.DrawImage(ImageSource image, Brush filter, Rect destinationRect, CornerRadius corners, Rect sourceUv)
   {
      var payload = new ImagePayload(filter, image, destinationRect, corners, sourceUv);
      CreateCommand(payload);
      return this;
   }

   IDrawingSession IDrawingSession.DrawImage(ImageSource image, Brush filter, Rect destinationRect, Matrix4x4F transform)
   {
      CreateCommand(new ImagePayload(filter, image, destinationRect, CornerRadius.Empty, localTransform: transform));
      return this;
   }

   IDrawingSession IDrawingSession.DrawColoredMesh(ColoredMesh mesh, Matrix4x4F transform)
   {
      ArgumentNullException.ThrowIfNull(mesh);
      if (mesh.Vertices.Count > 0) CreateCommand(new ColoredMeshPayload(mesh, transform));
      return this;
   }

   IDrawingSession IDrawingSession.DrawImageFrame(ImageSource image, Brush filter, Rect destinationRect, CornerRadius corners, int frameLayer)
   {
      var payload = new ImagePayload(filter, image, destinationRect, corners, null, frameLayer);
      CreateCommand(payload);
      return this;
   }

   IDrawingSession IDrawingSession.DrawImageFrame(ImageSource image, Brush filter, Rect destinationRect, CornerRadius corners,
      int frameLayer, Rect sourceUv)
   {
      var payload = new ImagePayload(filter, image, destinationRect, corners, sourceUv, frameLayer);
      CreateCommand(payload);
      return this;
   }

   public IDrawingSession PushImage(ImageSource image)
   {
      throw new NotImplementedException();
   }
   
   IDrawingSession IDrawingSession.DrawText(TextRenderingParameters renderingParameters, Size<double> desiredSize, TextLayout textLayout, Brush foreground, Brush background, Brush stroke)
   {
      // Only emit the text's background rect when it's actually visible. Every TextBlock passed its Background here
      // (default Brushes.Transparent), so each list item's text was creating an invisible fill unit + fringe on top of
      // the (now batched) glyphs - a big slice of a long list's cost. The glyph draw itself is always recorded below.
      if (background.IsVisible())
         CreateCommand(new RectanglePayload(background, new Rect(desiredSize), CornerRadius.Empty, null));
      var payload = new TextPayload(renderingParameters, desiredSize, textLayout, foreground, background, stroke);
      CreateCommand(payload);
      return this;
   }

   IDrawingSession IDrawingSession.DrawText(TextRenderingParameters renderingParameters, Size<double> desiredSize, TextLayout textLayout, Brush foreground, Brush background, Brush stroke, Matrix4x4F transform)
   {
      // The background sits where the run does, so it takes the same matrix - a rectangle of the element would stay
      // behind at its origin.
      if (background.IsVisible())
         ((IDrawingSession)this).DrawGeometry(background, new RectangleGeometry(new Rect(desiredSize)), null, transform);

      CreateCommand(new TextPayload(renderingParameters, desiredSize, textLayout, foreground, background, stroke, transform));
      return this;
   }

   IDrawingSession IDrawingSession.DrawText(TextRenderingParameters renderingParameters, Size<double> desiredSize, TextLayout textLayout, Brush foreground, Brush background, Brush stroke, Placement placement)
   {
      if (placement == null)
      {
         return ((IDrawingSession)this).DrawText(renderingParameters, desiredSize, textLayout, foreground, background, stroke);
      }

      BeginPlaced(placement);
      try
      {
         return ((IDrawingSession)this).DrawText(renderingParameters, desiredSize, textLayout, foreground, background,
            stroke, placement.Matrix);
      }
      finally
      {
         _placing = null;
      }
   }

   IDrawingSession IDrawingSession.DrawText(TextRenderingParameters renderingParameters, Size<double> desiredSize, TextLayout textLayout, Brush foreground, Brush background, Brush stroke, IReadOnlyList<GlyphEffect> effects)
   {
      if (background.IsVisible())
         CreateCommand(new RectanglePayload(background, new Rect(desiredSize), CornerRadius.Empty, null));

      CreateCommand(new TextPayload(renderingParameters, desiredSize, textLayout, foreground, background, stroke, null, effects));
      return this;
   }

   IDrawingSession IDrawingSession.DrawText(TextRenderingParameters renderingParameters, Size<double> desiredSize, TextLayout textLayout, Brush foreground, Brush background, Brush stroke, IReadOnlyList<GlyphEffect> effects, RectangleF window)
   {
      if (background.IsVisible())
         CreateCommand(new RectanglePayload(background, new Rect(desiredSize), CornerRadius.Empty, null));

      CreateCommand(new TextPayload(renderingParameters, desiredSize, textLayout, foreground, background, stroke, null, effects, window));
      return this;
   }

   IDrawingSession IDrawingSession.DrawGlyphs(GlyphRun run, Brush fill, Pen stroke, Matrix4x4F transform)
   {
      ArgumentNullException.ThrowIfNull(run);
      if (run.Glyphs.Count == 0) return this;

      if (fill.IsVisible())
      {
         var layout = run.Layout;
         CreateCommand(new TextPayload(PlacedGlyphs, layout.CalculatedLayoutSize, layout, fill, null, null, transform)
         {
            Ink = run.Bounds
         });
      }

      if (stroke != null)
      {
         ((IDrawingSession)this).DrawGeometry(null, run.Outline, stroke, transform);
      }

      return this;
   }

   // Placed glyphs stand where their run says: no area to align them in.
   private static readonly TextRenderingParameters PlacedGlyphs = new();

   // A group is recorded as marks around ordinary draws (GroupMarkKind): its masks are drawn between their marks - the
   // path and the glyphs as coverage, the soft mask as its picture - and its content after them.
   IDrawingSession IDrawingSession.PushGroup(DrawingGroupOptions options)
   {
      ArgumentNullException.ThrowIfNull(options);
      CreateCommand(GroupMarkPayload.Begin(options, RectClipOf(options)));
      var session = (IDrawingSession)this;
      _drawingMask++;
      try
      {
         if (options.Clip != null)
         {
            CreateCommand(GroupMarkPayload.MaskBegin(MaskKind.Alpha, default));
            session.DrawGeometry(Brushes.Black, options.Clip, null, options.ClipTransform);
            CreateCommand(GroupMarkPayload.MaskEnd);
         }

         if (options.TextClip is { Count: > 0 } glyphs)
         {
            CreateCommand(GroupMarkPayload.MaskBegin(MaskKind.Alpha, default));
            foreach (var clip in glyphs)
            {
               session.DrawGlyphs(clip.Run, Brushes.Black, null, clip.Transform);
            }

            CreateCommand(GroupMarkPayload.MaskEnd);
         }

         if (options.Mask != null)
         {
            CreateCommand(GroupMarkPayload.MaskBegin(options.MaskKind, options.MaskBackdrop));
            options.Mask.Render(this, options.MaskTransform, null);
            CreateCommand(GroupMarkPayload.MaskEnd);
         }
      }
      finally
      {
         _drawingMask--;
      }

      CreateCommand(GroupMarkPayload.ContentBegin);
      _openGroups++;
      return this;
   }

   IDrawingSession IDrawingSession.PushClip(Rect clip, CornerRadius corners, Placement placement)
   {
      ArgumentNullException.ThrowIfNull(placement);
      placement.DrawnBy(_currentComponent);
      _clip = new PlacedClip(clip, corners, placement, _clip, _clipsPushed++);
      return this;
   }

   IDrawingSession IDrawingSession.PopClip()
   {
      if (_clip == null)
      {
         throw new InvalidOperationException("No clip to end: PopClip without a PushClip.");
      }

      _clip = _clip.Outer;
      return this;
   }

   IDrawingSession IDrawingSession.PopGroup()
   {
      if (_openGroups == 0)
      {
         throw new InvalidOperationException("No group to end: PopGroup without a PushGroup.");
      }

      _openGroups--;
      CreateCommand(GroupMarkPayload.End);
      return this;
   }

   // The clip as a rectangle in the element's units, when cutting to it is all the group does and it lies along the axes
   // of the element: then the walk can cut by a scissor instead of a mask.
   private static Rect? RectClipOf(DrawingGroupOptions options)
   {
      if (options.Clip is not RectangleGeometry { Transform: null } rectangle || rectangle.CornerRadius != CornerRadius.Empty
          || options.TextClip is { Count: > 0 } || options.Mask != null || options.BlendMode != BlendMode.Normal
          || options.Opacity < 1)
      {
         return null;
      }

      var m = options.ClipTransform;
      if (MathF.Abs(m.M12) > 1e-6f || MathF.Abs(m.M21) > 1e-6f || m.M14 != 0f || m.M24 != 0f || m.M34 != 0f
          || m.M44 != 1f)
      {
         return null;
      }

      return rectangle.Rect.TransformToAABB(m);
   }

   private RenderData GetRenderDataFromComponent()
   {
      if (_drawingMask > 0)
      {
         return new RenderData(1f, World(_currentComponent), _currentComponent.ClipToBounds,
            _currentComponent.ClipRectangle) { DrawsMask = true, PlacedClip = _clip };
      }

      var renderData = new RenderData(GetEffectiveOpacity(),
         World(_currentComponent),
         _currentComponent.ClipToBounds,
         _currentComponent.ClipRectangle) { PlacedClip = _clip };
      // Baked HERE, on the record thread: the aura/shadow objects are edited from the loop thread, so the renderer gets
      // values, never the live objects. Read ONCE - both bakes want the aura, and a property read is not free.
      var aura = _currentComponent.Aura;
      renderData.Halo = HaloBake.From(aura, _currentComponent.Shadow);
      renderData.LivingHalo = HaloBake.Living(aura, GradientBake.PackPalette);
      //renderData.CustomEffect = _currentComponent.Effect TODO: implement custom effects for controls
      return renderData;
   }

   // Own Opacity times every ancestor's (WPF semantics); SelfOpacity applies to this element's draws only, never to
   // descendants.
   private float GetEffectiveOpacity()
   {
      var opacity = _currentComponent.Opacity * _currentComponent.SelfOpacity;
      for (var parent = _currentComponent.VisualParent; parent != null; parent = parent.VisualParent)
         opacity *= parent.Opacity;
      return (float)opacity;
   }
}