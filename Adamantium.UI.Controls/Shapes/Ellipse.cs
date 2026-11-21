using Adamantium.Mathematics;
using Adamantium.ProceduralGeometry.Shapes;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Graphics;
using Adamantium.UI.Core.Media;

namespace Adamantium.UI.Controls.Shapes;

public class Ellipse : Shape
{
   static Ellipse()
   {
      // Box-shape default (as in WPF): fill the layout slot. Path/Polygon/Line keep the base Stretch.None.
      StretchProperty.OverrideMetadata(typeof(Ellipse), new PropertyMetadata(Stretch.Fill,
         PropertyMetadataOptions.AffectsMeasure | PropertyMetadataOptions.AffectsRender));
   }

   public Ellipse()
   {
   }

   public static readonly AdamantiumProperty StartAngleProperty = AdamantiumProperty.Register(nameof(StartAngle),
      typeof(Double), typeof(Ellipse),
      new PropertyMetadata(0.0, PropertyMetadataOptions.AffectsRender));

   public static readonly AdamantiumProperty SweepAngleProperty = AdamantiumProperty.Register(nameof(SweepAngle),
      typeof(Double), typeof(Ellipse),
      new PropertyMetadata(360.0, PropertyMetadataOptions.AffectsRender));

   public static readonly AdamantiumProperty EllipseTypeProperty = AdamantiumProperty.Register(nameof(EllipseType),
      typeof(EllipseType), typeof(Ellipse),
      new PropertyMetadata(EllipseType.Sector,
         PropertyMetadataOptions.AffectsRender));

   /// <summary>Where the arc starts, in the ellipse's parametric degrees (<see cref="ArcSpan"/>): 0 at three o'clock,
   /// growing clockwise; any value, -90 starting at twelve.</summary>
   public Double StartAngle
   {
      get => GetValue<Double>(StartAngleProperty);
      set => SetValue(StartAngleProperty, value);
   }

   /// <summary>How far the arc runs from <see cref="StartAngle"/>, in degrees: negative counterclockwise, 0 nothing, a
   /// turn or more the whole ellipse. Kept as written; drawn as at most a turn.</summary>
   public Double SweepAngle
   {
      get => GetValue<Double>(SweepAngleProperty);
      set => SetValue(SweepAngleProperty, value);
   }

   /// <summary>The part of the ellipse drawn: <see cref="StartAngle"/> and <see cref="SweepAngle"/> taken round the
   /// turn.</summary>
   public ArcSpan Span => new(StartAngle, SweepAngle);
   
   public EllipseType EllipseType
   {
      get => GetValue<EllipseType>(EllipseTypeProperty);
      set => SetValue(EllipseTypeProperty, value);
   }

   /// <summary>Leave a RING this thick (DIPs, inward from the outline) instead of a solid shape - with a sweep, an annular
   /// sector. It is GEOMETRY: a ring gauge drawn this way keeps its pen for a real outline, and its thickness is no longer
   /// a stroke width that also has to serve as the shape.</summary>
   public static readonly AdamantiumProperty RingThicknessProperty = AdamantiumProperty.Register(nameof(RingThickness),
      typeof(Double), typeof(Ellipse),
      new PropertyMetadata(0.0, PropertyMetadataOptions.AffectsMeasure | PropertyMetadataOptions.AffectsRender));

   public Double RingThickness
   {
      get => GetValue<Double>(RingThicknessProperty);
      set => SetValue(RingThicknessProperty, value);
   }

   protected override void OnRender(IDrawingContext context)
   {
      var span = Span;
      if (span.IsEmpty)
      {
         return;
      }

      var destRect = Rect.Deflate(StrokeThickness / 2);
      context.ForControl(this).DrawEllipse(destRect, Fill, span.Start, span.Sweep, EllipseType, GetPen(), RingThickness);
   }

   // Tight hit test: what is drawn, not its box - measured on the outline OnRender draws. The fill: inside the ellipse,
   // inside the part of it drawn - a sector's wedge, an edge-to-edge segment's side of its chord - and out of a ring's
   // hole. The pen: on any piece of the outline it strokes, the arc, a sector's radii, a ring's inner arc and ends.
   public override bool HitTestCore(Vector2<double> localPoint)
   {
      var span = Span;
      var drawn = Rect.Deflate(StrokeThickness / 2);
      double rx = drawn.Width / 2.0, ry = drawn.Height / 2.0;
      if (span.IsEmpty || rx <= 0 || ry <= 0) return false;
      double x = localPoint.X - (drawn.X + rx), y = localPoint.Y - (drawn.Y + ry);

      // On the circle the ellipse is a squashed one of: there the parametric angle is the point's own.
      var onTheArc = span.IsWhole || span.Holds(double.RadiansToDegrees(Math.Atan2(y / ry, x / rx)));
      // A band thicker than the shape leaves no hole: the solid shape is drawn, as OnRender's DrawEllipse draws it.
      var ring = RingThickness > 0 && rx > RingThickness && ry > RingThickness ? RingThickness : 0;

      var half = GetPen() != null ? StrokeThickness / 2 : 0;
      if (half > 0 && IsOnTheOutline(span, onTheArc, x, y, rx, ry, ring, half)) return true;

      if (!IsVisibleBrush(Fill) || !IsInside(x, y, rx, ry)) return false;
      if (ring > 0 && IsInside(x, y, rx - ring, ry - ring)) return false;   // the hole
      if (span.IsWhole) return true;
      return EllipseType == EllipseType.EdgeToEdge && ring == 0 ? IsOnTheArcsSide(span, x / rx, y / ry) : onTheArc;
   }

   // Within the pen's half width of the outline the shape is stroked along: the arc; a ring's inner arc and its radial
   // ends; a sector's two radii. An edge-to-edge arc is open - nothing runs across its chord.
   private bool IsOnTheOutline(ArcSpan span, bool onTheArc, double x, double y, double rx, double ry, double ring,
      double half)
   {
      if (onTheArc && IsOnTheEllipse(x, y, rx, ry, half)) return true;
      if (ring > 0 && onTheArc && IsOnTheEllipse(x, y, rx - ring, ry - ring, half)) return true;
      if (span.IsWhole) return false;

      // A ring's ends run from the rim to the inner rim, a sector's radii to the center.
      if (ring == 0 && EllipseType != EllipseType.Sector) return false;
      var clockwise = span.Clockwise;
      foreach (var degrees in (ReadOnlySpan<double>)[clockwise.Start, clockwise.Start + clockwise.Sweep])
      {
         var angle = double.DegreesToRadians(degrees);
         double cos = Math.Cos(angle), sin = Math.Sin(angle);
         double innerX = ring > 0 ? (rx - ring) * cos : 0, innerY = ring > 0 ? (ry - ring) * sin : 0;
         if (IsNearTheSegment(x, y, rx * cos, ry * sin, innerX, innerY, half))
         {
            return true;
         }
      }

      return false;
   }

   private static bool IsOnTheEllipse(double x, double y, double rx, double ry, double half) =>
      IsInside(x, y, rx + half, ry + half) && !IsInside(x, y, rx - half, ry - half);

   private static bool IsNearTheSegment(double x, double y, double fromX, double fromY, double toX, double toY, double half)
   {
      double alongX = toX - fromX, alongY = toY - fromY;
      var length = alongX * alongX + alongY * alongY;
      var along = length > 0 ? Math.Clamp(((x - fromX) * alongX + (y - fromY) * alongY) / length, 0, 1) : 0;
      double offX = x - (fromX + alongX * along), offY = y - (fromY + alongY * along);
      return offX * offX + offY * offY <= half * half;
   }

   private static bool IsInside(double x, double y, double rx, double ry) =>
      rx > 0 && ry > 0 && x * x / (rx * rx) + y * y / (ry * ry) < 1.0;

   // An edge-to-edge segment is the ellipse on the arc's side of the chord between its ends - on the circle as on the
   // ellipse, a squash keeping which side of a line a point is.
   private static bool IsOnTheArcsSide(ArcSpan span, double x, double y)
   {
      var clockwise = span.Clockwise;
      double from = double.DegreesToRadians(clockwise.Start), to = double.DegreesToRadians(clockwise.Start + clockwise.Sweep);
      double middle = (from + to) / 2;
      double fromX = Math.Cos(from), fromY = Math.Sin(from);
      double chordX = Math.Cos(to) - fromX, chordY = Math.Sin(to) - fromY;
      double Side(double px, double py) => chordX * (py - fromY) - chordY * (px - fromX);
      var arcsSide = Side(Math.Cos(middle), Math.Sin(middle));
      return Side(x, y) * arcsSide >= 0;
   }
}