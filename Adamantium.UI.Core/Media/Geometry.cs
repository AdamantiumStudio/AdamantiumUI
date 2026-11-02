using Adamantium.Core.TypeParsing;
using Adamantium.Graphics.Core.Models;
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
      new PropertyMetadata(null, PropertyMetadataOptions.AffectsMeasure, TransformChangedCallback));

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

   public Boolean IsEmpty()
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
         _readyFill = ContoursAreFillBoundary ? PreparedFill.Of(this, Version) : null;
         IsProcessed = true;
      }
   }

   private PreparedFill _readyFill;

   /// <summary>The fill as the renderer draws it: the one <see cref="ProcessGeometry"/> made, while the geometry is as
   /// it was then; otherwise made now, from the mesh as it is, and not kept. Null when it has no triangles.</summary>
   internal PreparedFill FillForDrawing =>
      _readyFill is { } ready && ready.Version == Version ? ready : PreparedFill.Of(this, Version);

   /// <summary>Bumped every time the content is invalidated. A geometry can be REFILLED in place (a Polygon reopens its
   /// one StreamGeometry with new points on every render), so the instance stays the same while what it describes does
   /// not - and a render payload comparing instances alone would call that "unchanged" and keep the stale GPU mesh.</summary>
   public int Version { get; private set; }

   public void InvalidateGeometry()
   {
      IsProcessed = false;
      Version++;
   }
   
   protected internal abstract void ProcessGeometryCore(GeometryType geometryType);

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