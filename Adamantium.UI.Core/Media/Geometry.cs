using Adamantium.Core.TypeParsing;
using Adamantium.Graphics.Core.Models;
using Adamantium.Mathematics;
using Adamantium.ProceduralGeometry;
using Adamantium.UI.Core.Media.Fill;
using Adamantium.UI.Core.RoutedEvents;
using Adamantium.UI.Core.TypeParsers;

namespace Adamantium.UI.Core.Media;

[TypeParser(typeof(GeometryParser))]
public abstract class Geometry : AdamantiumComponent
{
   public bool IsProcessed { get; private set; }
      
   public Mesh Mesh { get; set; }
      
   protected Int32 TesselationFactor { get; set; }

   public static readonly AdamantiumProperty TransformProperty = AdamantiumProperty.Register(nameof(Transform),
      typeof(Transform), 
      typeof(Geometry),
      new PropertyMetadata(null, PropertyMetadataOptions.AffectsMeasure, TransformChangedCallback)
      {
         WriteCheckCallback = KeptWhenFrozen
      });

   // A frozen geometry is not moved; only the transform it has, written again - a binding refreshed - is let through.
   // A value cleared is refused too: what lay under it would come through.
   private static void KeptWhenFrozen(AdamantiumComponent a, object value)
   {
      if (a is Geometry { RefusesChange: true } geometry &&
          (value == AdamantiumProperty.UnsetValue || !ReferenceEquals(value, geometry.Transform)))
      {
         throw new InvalidOperationException("The geometry is frozen: it has let go of what it was made of.");
      }
   }

   public static readonly AdamantiumProperty IsClosedProperty =
      AdamantiumProperty.Register(nameof(IsClosed), typeof(bool), typeof(Geometry), new PropertyMetadata(true));

   private static void TransformChangedCallback(AdamantiumComponent a, AdamantiumPropertyChangedEventArgs e)
   {
      if (a is Geometry geometry)
      {
         if (e.OldValue is Transform transform)
         {
            transform.PropertyChanged -= geometry.TransformOnPropertyChanged;
         }

         if (e.NewValue is Transform transform1)
         {
            transform1.PropertyChanged += geometry.TransformOnPropertyChanged;
         }

         if (geometry.InheritanceParent is Drawings.Drawing drawing)
         {
            drawing.ReplaceOwned(e.OldValue as Transform, e.NewValue as Transform);
         }

         geometry.InvalidateGeometry();
         geometry.RaiseComponentUpdated();
      }
   }

   private void TransformOnPropertyChanged(object sender, AdamantiumPropertyChangedEventArgs e)
   {
      // A frozen geometry keeps what it draws; quietly, so whoever else the transform tells is told too.
      if (RefusesChange)
      {
         return;
      }

      InvalidateGeometry();
      RaiseComponentUpdated();
   }

   public Transform Transform
   {
      get => GetValue<Transform>(TransformProperty);
      set => SetValue(TransformProperty, value);
   }

   public bool IsClosed
   {
      get => GetValue<bool>(IsClosedProperty);
      set => SetValue(IsClosedProperty, value);
   }

   protected Geometry()
   {
      Mesh = new Mesh();
      TesselationFactor = 20;
      IsClosed = true;
   }
     
   public abstract Rect Bounds { get; }

   public abstract Geometry Clone();

   public virtual Boolean IsEmpty()
   {
      return Mesh.Points.Length == 0;
   }

   public abstract void RecalculateBounds();

   /// <summary>Builds the mesh on the calling thread. A geometry whose contours are its fill's edge - data cut on a
   /// worker, a map's tiles - also gets its fill made ready for drawing here, so it reaches the drawing thread ready.</summary>
   public void ProcessGeometry(GeometryType geometryType)
   {
      if (!IsProcessed)
      {
         ProcessGeometryCore(geometryType);
         if (Transform != null)
         {
            var matrix = Transform.Matrix;
            Mesh.ApplyTransform(matrix);
         }
         RecalculateBounds();
         // Processed from here: the stroke is made from the geometry's curves, which a geometry with none of its own
         // reads off this mesh - through this method.
         IsProcessed = true;
         _readyFill = ContoursAreFillBoundary ? PreparedFill.Of(this, Version) : null;
         _readyStroke = ContoursAreStrokePaths ? PreparedStroke.Of(this, Version, layOut: true) : null;
      }
   }

   private PreparedFill _readyFill;
   private PreparedStroke _readyStroke;

   /// <summary>The figures of the geometry in the order they were given, each the points of its outline - curves
   /// flattened - and whether it closes, with the geometry's <see cref="Transform"/>, running as drawn even under a
   /// mirroring one: what text along the geometry follows. New arrays each call.</summary>
   public List<(Vector2<double>[] Points, bool IsClosed)> GetFigures()
   {
      var given = FiguresCore();
      if (given == null)
      {
         ProcessGeometry(TrianglesAreGiven ? GeometryType.Both : GeometryType.Outlined);
         var fromMesh = new List<(Vector2<double>[] Points, bool IsClosed)>();
         var mirrored = Transform is { } mirror &&
                        mirror.Matrix.M11 * mirror.Matrix.M22 - mirror.Matrix.M12 * mirror.Matrix.M21 < 0;
         foreach (var contour in Mesh.Contours)
         {
            if (contour.Points is { Length: > 0 } points)
            {
               var copy = (Vector2<double>[])points.Clone();
               if (mirrored)
               {
                  Array.Reverse(copy);
               }

               fromMesh.Add((copy, contour.IsGeometryClosed));
            }
         }

         return fromMesh;
      }

      if (Transform is { } transform)
      {
         var matrix = transform.Matrix;
         foreach (var (points, _) in given)
         {
            Vector2<double>.TransformCoordinate(points, matrix, points);
         }
      }

      return given;
   }

   /// <summary>The figures as given, before any transform, in new arrays; null for a geometry that keeps none apart
   /// from its mesh.</summary>
   protected internal virtual List<(Vector2<double>[] Points, bool IsClosed)> FiguresCore() => null;

   /// <summary>What the geometry fills, as a combination reads it: the rings of its filled figures with its
   /// <see cref="Transform"/> - an open one closed, as a fill closes it - and the rule telling inside from out. By
   /// default every figure, even-odd; a geometry with figures it does not fill, or a rule of its own, says so.</summary>
   internal virtual (List<Vector2<double>[]> Rings, FillRule Rule) Area()
   {
      var rings = new List<Vector2<double>[]>();
      foreach (var (points, _) in GetFigures())
      {
         if (points.Length >= 3)
         {
            rings.Add(points);
         }
      }

      return (rings, FillRule.EvenOdd);
   }

   /// <summary>The contours a stroke runs along, as the renderer strokes them: the ones <see cref="ProcessGeometry"/>
   /// made, while the geometry is as it was then; otherwise made now, from the mesh as it is, and not kept.</summary>
   internal PreparedStroke StrokeForDrawing =>
      _readyStroke is { } ready && ready.Version == Version ? ready : PreparedStroke.Of(this, Version, layOut: false);

   /// <summary>Whether the mesh's contours are lines made to be stroked - a map's roads - so the contours a stroke runs
   /// along are made when the geometry is processed, on whatever thread processes it.</summary>
   protected internal virtual bool ContoursAreStrokePaths => false;

   /// <summary>Whether the mesh's triangles are given with the geometry rather than worked out from its outline: such a
   /// geometry keeps them even where its fill is drawn from its curves.</summary>
   protected internal virtual bool TrianglesAreGiven => false;

   /// <summary>The fill as the renderer draws it: the one <see cref="ProcessGeometry"/> made, while the geometry is as
   /// it was then; otherwise made now, from the mesh as it is, and not kept. Null when it has no triangles.</summary>
   internal PreparedFill FillForDrawing =>
      _readyFill is { } ready && ready.Version == Version ? ready : PreparedFill.Of(this, Version);

   private Curves.PathCurves _curves;

   /// <summary>The outline as quadratic curves, its <see cref="Transform"/> applied - what the GPU fills and strokes at
   /// any zoom. Made once for each <see cref="Version"/>, on whatever thread asks first.</summary>
   internal Curves.PathCurves CurvesForDrawing
   {
      get
      {
         if (_curves is { } made && made.Version == Version)
         {
            return made;
         }

         var version = Version;
         var builder = new Curves.PathCurveBuilder();
         if (AppendCurves(builder))
         {
            return _curves = builder.Build(version, Transform?.Matrix, CurveFillRule);
         }

         // The figures as points, the transform applied already: a geometry that keeps no curves of its own.
         foreach (var (points, isClosed) in GetFigures())
         {
            if (points.Length == 0)
            {
               continue;
            }

            builder.BeginFigure(points[0], EnclosesArea);
            for (var i = 1; i < points.Length; i++)
            {
               builder.LineTo(points[i]);
            }

            builder.EndFigure(isClosed);
         }

         return _curves = builder.Build(version, null, CurveFillRule);
      }
   }

   /// <summary>Whether its figures enclose an area a fill paints; false for lines made only to be stroked.</summary>
   protected internal virtual bool EnclosesArea => true;

   /// <summary>Which points the curves enclose: the geometry's own rule where it has one, else non-zero.</summary>
   protected internal virtual FillRule CurveFillRule => FillRule.NonZero;

   /// <summary>Gives the geometry's figures to <paramref name="builder"/> as curves, before its
   /// <see cref="Transform"/>; false for one that keeps none, whose figures are then given as their points.</summary>
   private protected virtual bool AppendCurves(Curves.PathCurveBuilder builder) => false;

   /// <summary>Bumped every time the content is invalidated. A geometry can be REFILLED in place (a Polygon reopens its
   /// one StreamGeometry with new points on every render), so the instance stays the same while what it describes does
   /// not - and a render payload comparing instances alone would call that "unchanged" and keep the stale GPU mesh.</summary>
   public int Version { get; private set; }

   public void InvalidateGeometry()
   {
      if (RefusesChange)
      {
         throw new InvalidOperationException("The geometry is frozen: it has let go of what it was made of.");
      }

      IsProcessed = false;
      Version++;
   }
   
   protected internal abstract void ProcessGeometryCore(GeometryType geometryType);

   /// <summary>Whether the geometry no longer changes - it let go of what it was made of - so a change, of its own or of
   /// its transform's, is refused where it is made rather than when it is next drawn.</summary>
   protected virtual bool RefusesChange => false;

   /// <summary>Whether the mesh's contours are exactly the edge of its fill - outer rings and holes - so the renderer
   /// takes them as they are rather than working the edge out of the triangles.</summary>
   protected internal virtual bool ContoursAreFillBoundary => false;

   /// <summary>The box the fill was cut to, in the geometry's own units, when it is a piece cut from a larger area - a
   /// map tile's square. Its edge along the box's sides is where it was cut, not where the area ends, so it is not
   /// antialiased. Null when the whole edge is the area's.</summary>
   protected internal virtual Rect? FillCutBox => null;

   protected override void OnComponentUpdated()
   {
      base.OnComponentUpdated();
      InvalidateGeometry();
   }

   public static CombinedGeometry Combine(Geometry geometry1, Geometry geometry2, GeometryCombineMode combineMode, Transform transform = null)
   {
      return new CombinedGeometry(){Geometry1 = geometry1, Geometry2 = geometry2, GeometryCombineMode = combineMode, Transform = transform};
   }
}