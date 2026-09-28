using Adamantium.Mathematics;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Graphics;
using Adamantium.UI.Core.Media;

namespace Adamantium.UI.Controls.Shapes;

/// <summary>A regular polygon with <see cref="Corners"/> corners inscribed in the box, the first on +x, drawn by an SDF
/// pass. <see cref="RingThickness"/> hollows it, leaving <see cref="Shape.Stroke"/> free.</summary>
public class RegularPolygon : Shape
{
   static RegularPolygon()
   {
      // Box-shape default, as Ellipse and Rectangle have: fill the layout slot.
      StretchProperty.OverrideMetadata(typeof(RegularPolygon), new PropertyMetadata(Stretch.Fill,
         PropertyMetadataOptions.AffectsMeasure | PropertyMetadataOptions.AffectsRender));
   }

   public static readonly AdamantiumProperty CornersProperty = AdamantiumProperty.Register(nameof(Corners),
      typeof(int), typeof(RegularPolygon),
      new PropertyMetadata(3, PropertyMetadataOptions.AffectsMeasure | PropertyMetadataOptions.AffectsRender));

   public static readonly AdamantiumProperty RingThicknessProperty = AdamantiumProperty.Register(nameof(RingThickness),
      typeof(double), typeof(RegularPolygon),
      new PropertyMetadata(0.0, PropertyMetadataOptions.AffectsMeasure | PropertyMetadataOptions.AffectsRender));

   public static readonly AdamantiumProperty StartAngleProperty = AdamantiumProperty.Register(nameof(StartAngle),
      typeof(double), typeof(RegularPolygon),
      new PropertyMetadata(0.0, PropertyMetadataOptions.AffectsRender));

   /// <summary>How many corners. Below three is not a polygon and is raised to three, the way the tessellator does it.</summary>
   public int Corners
   {
      get => GetValue<int>(CornersProperty);
      set => SetValue(CornersProperty, value);
   }

   /// <summary>Leave a ring this thick (DIPs, inward from the outline) instead of a solid shape.</summary>
   public double RingThickness
   {
      get => GetValue<double>(RingThicknessProperty);
      set => SetValue(RingThicknessProperty, value);
   }

   /// <summary>Where corner 0 sits, in DEGREES from the +x axis, positive the same way round as an ellipse's start angle.
   /// A triangle points right at 0, up at -90, and stands flat on its base at 90 - which is what most polygons in a UI
   /// actually need. The angle offsets the PARAMETER rather than rotating the result, so a turned polygon keeps filling
   /// the same box instead of swinging out of its slot.</summary>
   public double StartAngle
   {
      get => GetValue<double>(StartAngleProperty);
      set => SetValue(StartAngleProperty, value);
   }

   protected override void OnRender(IDrawingContext context)
   {
      var destRect = Rect.Deflate(StrokeThickness / 2);
      context.ForControl(this).DrawRegularPolygon(Fill, destRect, Corners, GetPen(), RingThickness, StartAngle);
   }
}
