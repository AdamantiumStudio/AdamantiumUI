using Adamantium.Core.Collections;

namespace Adamantium.UI.Core.Media;

public class Pen : IEquatable<Pen>
{
   // A Pen is immutable-per-change (every field set once; a NEW Pen is built on any change) EXCEPT its brush, which is a
   // live, animatable object. So the pen keeps the LIVE brush and exposes its immutable SNAPSHOT - the same contract a
   // payload has, and for the same reason: an animated stroke brush must stay visible through a pen recorded long ago.
   private readonly Brush _brush;

   /// <summary>The immutable snapshot of the stroke brush's current appearance (see <see cref="Media.Brush.Snapshot"/>).</summary>
   public Brush Brush => _brush?.Snapshot;

   public Double Thickness { get; }

   /// <summary>What <see cref="Thickness"/> is measured in. Honored by strokes of geometry.</summary>
   public PenThicknessUnit ThicknessUnit { get; init; }

   /// <summary>The narrowest the line is drawn, in device pixels, however far it is scaled down; 0 for no limit.
   /// Honored by strokes of geometry.</summary>
   public Double MinimumDeviceThickness { get; init; }

   /// <summary>How <see cref="Brush"/> is laid onto the stroke: over the shape's bounds, or along the line.</summary>
   public PenBrushMapping BrushMapping { get; init; }

   /// <summary>How the stroke paints where it crosses itself: in one pass, or in two that paint each pixel once.</summary>
   public PenOverlap Overlap { get; init; }

   /// <summary>How far along the line the dash pattern starts, in the same unit as <see cref="DashStrokeArray"/>.
   /// </summary>
   public Double DashOffset { get; }

   private AdamantiumCollection<Double> _dashStrokeArray;
   private readonly double[] _dashes;
   private readonly bool _isRenderCopy;

   /// <summary>The lengths of the dashes and gaps, in the units <see cref="ThicknessUnit"/> names - the geometry's, or
   /// the window's, as the width is - as a list the loop thread may edit. A copy
   /// for rendering (<see cref="CloneForRendering"/>) draws the dashes it was made with: editing its list changes
   /// nothing drawn.</summary>
   public AdamantiumCollection<Double> DashStrokeArray =>
      _dashStrokeArray ??= new AdamantiumCollection<double>((IEnumerable<double>)_dashes);

   /// <summary>The dashes of a pen of the render path - <see cref="CloneForRendering"/> - which nothing edits: what the
   /// renderer reads, without the list's lock on every value. For any other pen, the list's values now.</summary>
   internal IReadOnlyList<double> Dashes => _isRenderCopy ? _dashes : DashStrokeArray;

   public PenLineCap StartLineCap { get; }

   public PenLineCap EndLineCap { get; }

   // Cap on the STARTING end of each DASH/dot piece (the contour's true ends use Start/EndLineCap). Round makes round
   // dots; a zero-width dot + a round dash cap renders as a circle.
   public PenLineCap DashStartCap { get; }

   // Cap on the ENDING end of each dash. Separate from the start on purpose: with the two set differently a dash is no
   // longer symmetric - a concave bite behind a convex tip is an arrowhead, and a run of them is a direction.
   public PenLineCap DashEndCap { get; }

   public PenLineJoin PenLineJoin { get; }

   // Stroke trim: render only the arc-length fraction [TrimStart, TrimEnd] of the geometry (0..1) - e.g. a progress
   // bar / loading ring. Default 0..1 = the whole stroke. Applied on the GPU.
   public Double TrimStart { get; }

   public Double TrimEnd { get; }

   /// <summary>Whether the dash pattern is scaled (by at most half a period) so a whole number of periods fits a closed
   /// contour, with no long dash at the seam.</summary>
   public Boolean FitDashesToContour { get; }

   /// <summary>Dash offset expressed in PERIODS instead of the pattern's unit, added to <see cref="DashOffset"/>. One period is the
   /// whole pattern, so animating this 0 -> 1 marches the dashes exactly one step and lands back on itself - seamlessly,
   /// whatever the array says and whatever the contour fit scaled it to. <see cref="DashOffset"/> cannot do that: it makes the
   /// author work out the period by hand and then re-derive it every time the pattern changes.</summary>
   public Double DashPhase { get; }

   public Pen(
      Brush brush,
      Double thickness = 1.0,
      Double dashOffset = 0,
      IEnumerable<Double> dashStrokeArray = null,
      PenLineCap startLineCap = PenLineCap.Flat,
      PenLineCap endLineCap = PenLineCap.Flat,
      PenLineJoin penLineJoin = PenLineJoin.Miter,
      Double trimStart = 0.0,
      Double trimEnd = 1.0,
      PenLineCap dashStartCap = PenLineCap.ConvexRound,
      PenLineCap dashEndCap = PenLineCap.ConvexRound,
      Boolean fitDashesToContour = false,
      Double dashPhase = 0)
   {
      FitDashesToContour = fitDashesToContour;
      DashPhase = dashPhase;
      _brush = brush?.ForRendering();
      _dashes = dashStrokeArray?.ToArray() ?? [];
      _dashStrokeArray = new AdamantiumCollection<double>((IEnumerable<double>)_dashes);
      DashOffset = dashOffset;
      Thickness = thickness;
      StartLineCap = startLineCap;
      EndLineCap = endLineCap;
      PenLineJoin = penLineJoin;
      TrimStart = trimStart;
      TrimEnd = trimEnd;
      DashStartCap = dashStartCap;
      DashEndCap = dashEndCap;
   }

   /// <summary>Compare dash patterns BY VALUE. Deliberately here and not on <c>AdamantiumCollection</c>: that type is a
   /// general, MUTABLE list used across the engine, so giving it value equality would turn O(1) reference compares into
   /// O(n) ones for every other user, and a value hash on a mutable object silently loses it as a dictionary key. A dash
   /// pattern is 0-4 numbers, so comparing it costs nothing - and the count check exits first in the common empty case.</summary>
   private static bool SameDashes(IReadOnlyList<double> a, IReadOnlyList<double> b)
   {
      if (ReferenceEquals(a, b)) return true;
      if (a == null || b == null || a.Count != b.Count) return false;
      for (var i = 0; i < a.Count; i++)
         if (!a[i].Equals(b[i]))
            return false;
      return true;
   }

   /// <summary>A copy for the RENDER path: same values, same LIVE brush (so an animated stroke brush keeps animating
   /// through its snapshot), but a dash array nobody else holds. A payload storing this instead of the caller's pen no
   /// longer exposes the render thread to a pen the loop thread can still edit. Safe only because the comparison above is
   /// by value - a clone with a fresh collection would otherwise read as "changed" on every single frame. Made once and
   /// handed out again while the dashes stay; two threads asking at once may each make one, which is harmless.</summary>
   public Pen CloneForRendering()
   {
      if (_isRenderCopy)
      {
         return this;
      }

      if (_forRendering is { } made && SameDashes(made._dashes, DashStrokeArray))
      {
         return made;
      }

      made = new Pen(this, DashStrokeArray.ToArray(), _brush);
      _forRendering = made;
      return made;
   }

   private Pen _forRendering;

   private Pen(Pen of, double[] dashes, Brush brush)
   {
      _brush = brush;
      Thickness = of.Thickness;
      ThicknessUnit = of.ThicknessUnit;
      MinimumDeviceThickness = of.MinimumDeviceThickness;
      BrushMapping = of.BrushMapping;
      Overlap = of.Overlap;
      DashOffset = of.DashOffset;
      _dashes = dashes;
      _isRenderCopy = true;
      StartLineCap = of.StartLineCap;
      EndLineCap = of.EndLineCap;
      PenLineJoin = of.PenLineJoin;
      TrimStart = of.TrimStart;
      TrimEnd = of.TrimEnd;
      DashStartCap = of.DashStartCap;
      DashEndCap = of.DashEndCap;
      FitDashesToContour = of.FitDashesToContour;
      DashPhase = of.DashPhase;
   }

   internal Pen WithBrush(Brush brush) => _isRenderCopy ? new Pen(this, _dashes, brush?.ForRendering()) : new(
      brush,
      Thickness,
      DashOffset,
      DashStrokeArray,
      StartLineCap,
      EndLineCap,
      PenLineJoin,
      TrimStart,
      TrimEnd,
      DashStartCap,
      DashEndCap,
      FitDashesToContour,
      DashPhase)
   {
      ThicknessUnit = ThicknessUnit,
      MinimumDeviceThickness = MinimumDeviceThickness,
      BrushMapping = BrushMapping,
      Overlap = Overlap,
   };

   public bool Equals(Pen other)
   {
      if (ReferenceEquals(null, other)) return false;
      if (ReferenceEquals(this, other)) return true;
      return Equals(Brush, other.Brush) && Thickness.Equals(other.Thickness) && DashOffset.Equals(other.DashOffset) &&
             SameDashes(Dashes, other.Dashes) && StartLineCap == other.StartLineCap &&
             EndLineCap == other.EndLineCap && DashStartCap == other.DashStartCap &&
             DashEndCap == other.DashEndCap && PenLineJoin == other.PenLineJoin &&
             TrimStart.Equals(other.TrimStart) && TrimEnd.Equals(other.TrimEnd) &&
             // Load-bearing: a pen that compares EQUAL is treated as unchanged and the stroke is never re-pointed, so a
             // property missing from here is a property that silently cannot be animated (the marching ants stopped
             // moving the moment DashPhase was added and left out).
             DashPhase.Equals(other.DashPhase) && FitDashesToContour == other.FitDashesToContour &&
             ThicknessUnit == other.ThicknessUnit && MinimumDeviceThickness.Equals(other.MinimumDeviceThickness) &&
             BrushMapping == other.BrushMapping && Overlap == other.Overlap;
   }

   public override bool Equals(object obj)
   {
      if (ReferenceEquals(null, obj)) return false;
      if (ReferenceEquals(this, obj)) return true;
      if (obj.GetType() != this.GetType()) return false;
      return Equals((Pen)obj);
   }

   public override int GetHashCode()
   {
      return HashCode.Combine(
         // The dash array hashes by its COUNT, not its identity: Equals now compares the pattern by value, so two equal
         // pens with different collection instances must not land in different buckets.
         HashCode.Combine(Brush, Thickness, DashOffset, Dashes.Count, (int)StartLineCap, (int)EndLineCap, (int)PenLineJoin),
         HashCode.Combine(TrimStart, TrimEnd, (int)DashStartCap, (int)DashEndCap, DashPhase, FitDashesToContour),
         (int)ThicknessUnit, MinimumDeviceThickness, (int)BrushMapping, (int)Overlap);
   }
}