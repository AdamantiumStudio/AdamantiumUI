using Adamantium.Mathematics;
using Adamantium.ProceduralGeometry;
using Adamantium.ProceduralGeometry.Shapes;

namespace Adamantium.UI.Core.Media;

public sealed class RectangleGeometry : Geometry
{
   private Rect bounds;
      
   public static readonly AdamantiumProperty CornerRadiusProperty = AdamantiumProperty.Register(nameof(CornerRadius),
      typeof (CornerRadius), typeof (RectangleGeometry), new PropertyMetadata(new CornerRadius(0)));

   public static readonly AdamantiumProperty RectProperty = AdamantiumProperty.Register(nameof(Rect),
      typeof(Rect), typeof(RectangleGeometry), new PropertyMetadata(Rect.Empty));

   public RectangleGeometry()
   {
      IsClosed = true;
   }

   public RectangleGeometry(RectangleGeometry copy)
   {
      Mesh = copy.Mesh;
      Rect = copy.Rect;
      CornerRadius = copy.CornerRadius;
   }
   
   public RectangleGeometry(Rect size) : this(size, new CornerRadius(0))
   {
      
   }
   
   public RectangleGeometry(Size<double> size) : this(new Rect(size), new CornerRadius(0))
   {
      
   }

   public RectangleGeometry(Rect size, CornerRadius corners) : this()
   {
      Rect = size;
      CornerRadius = corners;
   }

   public CornerRadius CornerRadius
   {
      get => GetValue<CornerRadius>(CornerRadiusProperty);
      set => SetValue(CornerRadiusProperty, value);
   }

   public Rect Rect
   {
      get => GetValue<Rect>(RectProperty);
      set => SetValue(RectProperty, value);
   }

   private void CreateRectangle(Rect rect, CornerRadius corners, GeometryType geometryType)
   {
      Rect = rect;
      bounds = rect;
      CornerRadius = corners;

      var translation = Matrix4x4.CreateTranslation<double>((float)rect.Width / 2 + (float)rect.X,
         (float)rect.Height / 2 + (float)rect.Y, 0);
      Mesh = Shapes.Rectangle.GenerateGeometry(
         geometryType, 
         rect.X,
         rect.Y,
         (float)rect.Width,
         (float)rect.Height,
         CornerRadius, 
         TesselationFactor, 
         translation);
   }


   public override Rect Bounds => bounds;

   public override Geometry Clone()
   {
      return new RectangleGeometry(this);
   }

   public override void RecalculateBounds()
   {
      bounds = Rect;
      if (Transform != null)
      {
         bounds = bounds.TransformToAABB(Transform.Matrix);
      }
   }

   protected internal override void ProcessGeometryCore(GeometryType geometryType)
   {
      CreateRectangle(Rect, CornerRadius, geometryType);
   }

   // Its sides, and a quarter of a circle at each rounded corner, clockwise from the top left.
   private protected override bool AppendCurves(Curves.PathCurveBuilder builder)
   {
      var rect = Rect;
      if (rect.Width <= 0 || rect.Height <= 0)
      {
         return true;
      }

      var limit = Math.Min(rect.Width, rect.Height) / 2;
      var corners = CornerRadius;
      var topLeft = Math.Clamp(corners.TopLeft, 0, limit);
      var topRight = Math.Clamp(corners.TopRight, 0, limit);
      var bottomRight = Math.Clamp(corners.BottomRight, 0, limit);
      var bottomLeft = Math.Clamp(corners.BottomLeft, 0, limit);
      const double reach = 0.5522847498307936;

      builder.BeginFigure(new Vector2<double>(rect.X + topLeft, rect.Y), true);
      builder.LineTo(new Vector2<double>(rect.Right - topRight, rect.Y));
      Corner(builder, new Vector2<double>(rect.Right, rect.Y), new Vector2<double>(rect.Right, rect.Y + topRight), topRight, reach);
      builder.LineTo(new Vector2<double>(rect.Right, rect.Bottom - bottomRight));
      Corner(builder, new Vector2<double>(rect.Right, rect.Bottom), new Vector2<double>(rect.Right - bottomRight, rect.Bottom),
         bottomRight, reach);
      builder.LineTo(new Vector2<double>(rect.X + bottomLeft, rect.Bottom));
      Corner(builder, new Vector2<double>(rect.X, rect.Bottom), new Vector2<double>(rect.X, rect.Bottom - bottomLeft), bottomLeft,
         reach);
      builder.LineTo(new Vector2<double>(rect.X, rect.Y + topLeft));
      Corner(builder, new Vector2<double>(rect.X, rect.Y), new Vector2<double>(rect.X + topLeft, rect.Y), topLeft, reach);
      builder.EndFigure(true);
      return true;
   }

   private static void Corner(Curves.PathCurveBuilder builder, Vector2<double> corner, Vector2<double> end, double radius,
      double reach)
   {
      if (radius <= 0)
      {
         return;
      }

      var start = builder.Current;
      builder.CubicTo(start + (corner - start) * reach, end + (corner - end) * reach, end);
   }
}