using System.Collections.Specialized;
using Adamantium.Mathematics;
using Adamantium.Mathematics.Triangulation;
using Adamantium.ProceduralGeometry;
using Adamantium.UI.Core.RoutedEvents;
using Polygon = Adamantium.Mathematics.Triangulation.Polygon;

namespace Adamantium.UI.Core.Media;

public class PathGeometry : Geometry
{
    private readonly Dictionary<PathFigure, MeshContour> figureToPolygon;
    private Dictionary<PathFigure, Vector2<double>[]> outlines;
    private Rect bounds;
    
    public PathGeometry()
    {
        figureToPolygon = new Dictionary<PathFigure, MeshContour>();
        outlines = new Dictionary<PathFigure, Vector2<double>[]>();
        Figures = new PathFigureCollection();
    }

    public static readonly AdamantiumProperty FillRuleProperty = AdamantiumProperty.Register(nameof(FillRule),
        typeof(FillRule), typeof(PathGeometry),
        new PropertyMetadata(FillRule.EvenOdd, PropertyMetadataOptions.AffectsRender));
    
    public static readonly AdamantiumProperty FiguresProperty =
        AdamantiumProperty.Register(nameof(Figures), typeof(PathFigureCollection), typeof(PathGeometry),
            new PropertyMetadata(null, PropertyMetadataOptions.AffectsMeasure, FiguresChangedCallback));
    
    private static void FiguresChangedCallback(AdamantiumComponent a, AdamantiumPropertyChangedEventArgs e)
    {
        if (a is PathGeometry pathGeometry)
        {
            if (e.OldValue is PathFigureCollection collection1) collection1.CollectionChanged -= pathGeometry.FiguresOnCollectionChanged;
            
            if (e.NewValue is PathFigureCollection collection2) collection2.CollectionChanged += pathGeometry.FiguresOnCollectionChanged;
        }
    }

    private void FiguresOnCollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
    {
        RaiseComponentUpdated();
    }

    protected internal override FillRule CurveFillRule => FillRule;

    public FillRule FillRule
    {
        get => GetValue<FillRule>(FillRuleProperty);
        set => SetValue(FillRuleProperty, value);
    }
    
    [Content]
    public PathFigureCollection Figures
    {
        get => GetValue<PathFigureCollection>(FiguresProperty);
        set => SetValue(FiguresProperty, value);
    }

    public override Rect Bounds => bounds;

    protected internal override List<(Vector2<double>[] Points, bool IsClosed)> FiguresCore()
    {
        var figures = new List<(Vector2<double>[] Points, bool IsClosed)>();
        foreach (var figure in Figures ?? [])
        {
            figure.ProcessSegments();
            if (figure.Points.Count > 0)
            {
                figures.Add((figure.Points.ToArray(), figure.IsClosed));
            }
        }

        return figures;
    }

    /// <summary>Its filled figures under its own <see cref="FillRule"/>.</summary>
    internal override (List<Vector2<double>[]> Rings, FillRule Rule) Area()
    {
        var rings = new List<Vector2<double>[]>();
        foreach (var figure in Figures ?? [])
        {
            figure.ProcessSegments();
            if (figure.IsFilled && figure.Points.Count >= 3)
            {
                var points = figure.Points.ToArray();
                if (Transform is { } transform)
                {
                    Vector2<double>.TransformCoordinate(points, transform.Matrix, points);
                }

                rings.Add(points);
            }
        }

        return (rings, FillRule);
    }

    public override Geometry Clone()
    {
        throw new System.NotImplementedException();
    }

    public override void RecalculateBounds()
    {
        if (Figures == null) return;
        
        var points = new List<Vector2<double>>();
        foreach (var figure in Figures)
        {
            figure.ProcessSegments();
            points.AddRange(figure.Points);
        }
        
        if (points.Count == 0) return;
        
        var maxX = points.Select(x=>x.X).Max();
        var maxY = points.Select(y=>y.Y).Max();
        var minX = points.Select(x => x.X).Min();
        var minY = points.Select(y => y.Y).Min();
        bounds = new Rect(new Vector2<double>(minX, minY), new Vector2<double>(maxX, maxY));
    }

    private protected override bool AppendCurves(Curves.PathCurveBuilder builder)
    {
        foreach (var figure in Figures ?? [])
        {
            figure.AppendCurves(builder);
        }

        return true;
    }

    protected internal override void ProcessGeometryCore(GeometryType geometryType)
    {
        figureToPolygon.Clear();
        outlines.Clear();   // was leaked: stale outlines of removed figures lingered and got re-added below
        Mesh.ClearContours();
        
        foreach (var figure in Figures)
        {
            figure.ProcessSegments();
            var meshContour = new MeshContour(figure.Points);
            figureToPolygon[figure] = meshContour;
            var outlinePoints = figure.Points.ToArray();
            outlines[figure] = outlinePoints;
        }
        
        var polygon = new Polygon();
        foreach (var polygonItem in figureToPolygon)
        {
            if (!polygonItem.Key.IsFilled) continue;

            polygon.AddContour(polygonItem.Value);
        }

        polygon.FillRule = FillRule;
        var points = polygon.FillIndirect(geometryType != GeometryType.Outlined);
        if (geometryType != GeometryType.Outlined) Mesh.SetPoints(points);
        
        foreach (var outline in outlines)
        {
            Mesh.AddContour(outline.Value, outline.Key.IsClosed);
        }
    }
}