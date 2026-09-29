using System;
using System.Collections.Generic;
using Adamantium.Mathematics;
using Adamantium.ProceduralGeometry;
using Adamantium.ProceduralGeometry.Shapes;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Graphics;
using Adamantium.UI.Core.Media;

namespace Adamantium.UI.Controls.DrawingBoard;

/// <summary>A rectangle, ellipse, line, arrow or regular polygon: a world-unit box with a fill and an outline. The shape is
/// its box, so a resize is one field.</summary>
public class ShapeItem : ICanvasItem, ICanvasTransformed, ICanvasPoints
{
    private CanvasArrowBrush _paint;

    // The outline, kept and rebuilt only when what it is made of changes. A pen is immutable-per-change, so building one
    // per frame per shape would allocate for every shape on screen every frame - and the width DOES change on every step
    // of the zoom, which is exactly why the cached one is keyed on the width it was built for.
    private Pen _pen;
    private double _penWidth = double.NaN;
    private Brush _penBrush;
    private Rect _world;
    private int _sides = 5;
    private double _headLength = 3.2;
    private double _headWidth = 2.6;

    // Where a polygon's first corner sits. Straight UP: a polygon inscribed from angle zero starts at the right-hand
    // side, so a triangle comes out lying on its side - which is not what anybody drawing a triangle means.
    private const double StartAngle = -90;

    public ShapeItem(CanvasShape shape, Rect world, Brush stroke, double thickness, Brush fill = null)
    {
        Shape = shape;
        World = Normalize(world);
        Stroke = stroke;
        Thickness = thickness;
        Fill = fill;
    }

    public CanvasShape Shape { get; }

    /// <summary>A second shape just like this one. Everything that makes it what it is, including the two bits that say
    /// which way a run leans and which end it starts at - a copied arrow that pointed the other way would be a copy of
    /// something else.</summary>
    // ...WITH PAINT OF ITS OWN. A copy handed the original's brushes is the same object as far as a color written
    // into one goes: recoloring the copy recolored what it was copied from. See Brush.Copy.
    public ICanvasItem Copy() => new ShapeItem(Shape, World, Stroke?.Copy(), Thickness, Fill?.Copy())
    {
        Flipped = Flipped,
        Reversed = Reversed,
        Sides = Sides,
        StartHead = StartHead,
        EndHead = EndHead,
        HeadLength = HeadLength,
        HeadWidth = HeadWidth,
        Corner = Corner
    };

    /// <summary>WHAT THIS STANDS FOR, when the canvas made it for an object of the application's own drawing - null for
    /// a shape drawn by hand on the plane.</summary>
    public ICanvasObject Model { get; internal set; }

    /// <summary>Its normalized box in world units, without the outline; for an application object it reads and writes that
    /// object's own box.</summary>
    public Rect World
    {
        get => Model == null ? _world : new Rect(Model.Left, Model.Top, Model.Width, Model.Height);
        set
        {
            _world = Normalize(value);

            if (Model == null) return;

            Model.Left = _world.X;
            Model.Top = _world.Y;
            Model.Width = _world.Width;
            Model.Height = _world.Height;
        }
    }

    /// <summary>Which DIAGONAL of its box a <see cref="CanvasShape.Line"/> lies on. The box is normalized, so without
    /// this a line dragged up and to the right would come back leaning the other way.</summary>
    public bool Flipped { get; set; }

    /// <summary>Which END of that diagonal the line STARTS at - the end an arrow's <see cref="StartHead"/> sits on.
    /// <para>A second bit, because a box and one of them cannot say both. A line dragged straight up and one dragged
    /// straight down occupy the same box on the same diagonal and differ only in this; with one bit, pulling one end of
    /// an arrow past the other turned the arrow round, which is exactly the gesture somebody placing an arrow makes.
    /// </para></summary>
    public bool Reversed { get; set; }

    public Brush Stroke { get; set; }

    public Brush Fill { get; set; }

    /// <summary>The OUTLINE's color and not the fill's: a shape is drawn by its edge - an unfilled one has nothing
    /// else - so the outline is the color it reads as.</summary>
    public Color? Paint => (Stroke as SolidColorBrush)?.Color;

    public void PaintWith(Color color) => Stroke = new SolidColorBrush(color);

    /// <summary>How wide the outline is, in WORLD units - it belongs to the drawing, so it grows with the zoom. The
    /// tool states it in screen pixels and converts, the same division the pen makes.</summary>
    public double Thickness { get; set; }

    /// <summary>How round a <see cref="CanvasShape.Rectangle"/>'s corners are, in WORLD units - so a rounded box keeps
    /// its shape through a zoom like everything else drawn on the plane. Nothing to the others.
    /// <para>All FOUR of them: a card with two rounded corners and two square ones is an ordinary shape, and the
    /// renderer has always drawn four independent radii - it was this that could only say one number.</para></summary>
    public CornerRadius Corner { get; set; }

    /// <summary>One corner at a time. A radius is a struct and a struct cannot be written half at a time, so an
    /// inspector line editing the top left has nothing else to bind to.</summary>
    public double CornerTopLeft
    {
        get => Corner.TopLeft;
        set => Corner = new CornerRadius(value, Corner.TopRight, Corner.BottomRight, Corner.BottomLeft);
    }

    public double CornerTopRight
    {
        get => Corner.TopRight;
        set => Corner = new CornerRadius(Corner.TopLeft, value, Corner.BottomRight, Corner.BottomLeft);
    }

    public double CornerBottomRight
    {
        get => Corner.BottomRight;
        set => Corner = new CornerRadius(Corner.TopLeft, Corner.TopRight, value, Corner.BottomLeft);
    }

    public double CornerBottomLeft
    {
        get => Corner.BottomLeft;
        set => Corner = new CornerRadius(Corner.TopLeft, Corner.TopRight, Corner.BottomRight, value);
    }

    /// <summary>How many sides a <see cref="CanvasShape.Polygon"/> has. Three at the least - two sides are a line, and
    /// a polygon that can be told to have one would draw nothing at all.</summary>
    public int Sides
    {
        get => _sides;
        set => _sides = Math.Max(3, value);
    }

    /// <summary>How it is turned and leaned, about the middle of its own box. <see cref="Bounds"/> stays the box of the
    /// shape UNTURNED, so resizing it means the same thing at any angle.</summary>
    public CanvasTransform Transform { get; set; } = CanvasTransform.None;

    /// <summary>The turn one number at a time. A transform is a struct and a struct cannot be written half at a time,
    /// so an inspector line editing only the angle has nothing else to bind to - the same reason the corner radii are
    /// four properties.</summary>
    public double Angle
    {
        get => Transform.Angle;
        set => Transform = Transform with { Angle = value };
    }

    public double SkewX
    {
        get => Transform.SkewX;
        set => Transform = Transform with { SkewX = value };
    }

    public double SkewY
    {
        get => Transform.SkewY;
        set => Transform = Transform with { SkewY = value };
    }

    /// <summary>What sits on the START of a <see cref="CanvasShape.Arrow"/> - the end the drag began at. Nothing by
    /// default: an arrow usually points one way, and one that pointed both ways by default would have to be undone
    /// every time.</summary>
    public CanvasArrowHead StartHead { get; set; } = CanvasArrowHead.None;

    /// <summary>What sits on the END of a <see cref="CanvasShape.Arrow"/> - the end the drag finished at, which is
    /// where a person drawing an arrow is pointing.</summary>
    public CanvasArrowHead EndHead { get; set; } = CanvasArrowHead.Triangle;

    /// <summary>How long a head is, in LINE THICKNESSES. Not in world units: a head stated in world units comes adrift
    /// from its own line as soon as the line is made thicker.</summary>
    public double HeadLength
    {
        get => _headLength;
        set => _headLength = Math.Max(0.1, value);
    }

    /// <summary>How wide a head is across its base, in LINE THICKNESSES.</summary>
    public double HeadWidth
    {
        get => _headWidth;
        set => _headWidth = Math.Max(0.1, value);
    }

    /// <summary>Where it is and how big, one number at a time - a box cannot be written half at a time, and an
    /// inspector line editing only the width has nothing else to bind to.</summary>
    // EVERY ONE OF THESE GOES THROUGH World, and that is not tidiness: with a model attached the box IS the model's,
    // and a write that went to the field instead would be a shape that moved on the plane and nowhere else - drawn
    // where the model still says it is, which is where it started.
    public double X
    {
        get => World.X;
        set => World = new Rect(value, World.Y, World.Width, World.Height);
    }

    public double Y
    {
        get => World.Y;
        set => World = new Rect(World.X, value, World.Width, World.Height);
    }

    public double Width
    {
        get => World.Width;
        set => World = new Rect(World.X, World.Y, Math.Max(1e-9, value), World.Height);
    }

    public double Height
    {
        get => World.Height;
        set => World = new Rect(World.X, World.Y, World.Width, Math.Max(1e-9, value));
    }

    /// <summary>Where it stands in paint order - stamped by the scene. See ICanvasItem.Order.</summary>
    public int Order { get; set; }

    /// <summary>The shape's own name ("Rectangle", "Arrow"), so the inspector shows its specific lines; see
    /// <see cref="ICanvasItem.Sort"/>.</summary>
    public string Sort => Shape.ToString();

    /// <summary>Its box, outline included since the outline is drawn inside; for a line or arrow, what the stroke and heads
    /// actually reach.</summary>
    public Rect Bounds => IsRun ? Reach() : World;


    // What a line or an arrow actually COVERS, in world units: the two ends, each widened by half the stroke, plus the
    // corners of whichever heads it wears. Taken from the same numbers the heads are DRAWN from rather than from a
    // guess at how far one reaches, because the two have to agree or the frame is wrong by however much they differ.
    private Rect Reach()
    {
        Ends(out var from, out var to);

        var half = Math.Max(Thickness, 0) / 2;
        var minX = Math.Min(from.X, to.X);
        var maxX = Math.Max(from.X, to.X);
        var minY = Math.Min(from.Y, to.Y);
        var maxY = Math.Max(from.Y, to.Y);

        Take(EndHead, to, from);
        Take(StartHead, from, to);

        return new Rect(minX - half, minY - half, maxX - minX + 2 * half, maxY - minY + 2 * half);

        void Take(CanvasArrowHead kind, Vector2 tip, Vector2 tail)
        {
            if (Shape != CanvasShape.Arrow || kind == CanvasArrowHead.None) return;
            if (!ArrowHead.Points(tail, tip, Thickness, HeadLength, HeadWidth, out var left, out var right)) return;

            minX = Math.Min(minX, Math.Min(left.X, right.X));
            maxX = Math.Max(maxX, Math.Max(left.X, right.X));
            minY = Math.Min(minY, Math.Min(left.Y, right.Y));
            maxY = Math.Max(maxY, Math.Max(left.Y, right.Y));
        }
    }

    /// <summary>Which shape it is - that is the whole of what distinguishes one of these from another in a list.</summary>
    public string Title => Shape.ToString();

    /// <summary>The two ENDS of a line or an arrow, which are the only thing about it worth grabbing. Every other shape
    /// is a box and answers with nothing.</summary>
    public IReadOnlyList<Vector2> Points
    {
        get
        {
            if (!IsRun) return Array.Empty<Vector2>();

            Ends(out var from, out var to);
            return new[] { from, to };
        }
    }

    /// <summary>Which grips the frame offers. A line and an arrow offer NONE of the box's: their box is a diagonal, so
    /// its corners are not on the shape at all and dragging one of them moves both ends at once - which is the opposite
    /// of what placing an arrow needs. They are reshaped by their ends instead, the way a curve is.</summary>
    public CanvasHandles Handles => IsRun ? CanvasHandles.Body : CanvasHandles.All;

    /// <summary>Moves ONE end and leaves the other where it is. The whole point: an arrow is put in place by pinning one
    /// end to what it comes from and pulling the other to what it points at, and a box round it can only ever scale
    /// both at once.</summary>
    public void MovePoint(int index, Vector2 world)
    {
        if (!IsRun) return;

        Ends(out var from, out var to);

        if (index == 0) from = world;
        else if (index == 1) to = world;
        else return;

        SetEnds(from, to);
    }

    // A line or an arrow: the two shapes that are a RUN between two points rather than a box with something drawn in it.
    private bool IsRun => Shape is CanvasShape.Line or CanvasShape.Arrow;

    /// <summary>The middle of its own box - what a turn turns about.</summary>
    private Vector2 Middle => new(World.X + World.Width / 2, World.Y + World.Height / 2);

    public void Move(Vector2 worldDelta)
    {
        var box = World;

        World = new Rect(box.X + worldDelta.X, box.Y + worldDelta.Y, box.Width, box.Height);
    }

    public void Resize(Rect world)
    {
        if (world.Width <= 0 || world.Height <= 0) return;

        var box = Normalize(world);

        // What comes in is the FRAME, and for a line or an arrow the frame is not the box: it holds the stroke and the
        // heads as well. Assigned straight across, an arrow took its own head's width as new length every time anybody
        // touched a grip, and grew a little each time. Mapped instead, so that shrinking the frame shrinks the drawing.
        if (IsRun)
        {
            var was = Bounds;

            if (was.Width > 0 && was.Height > 0)
            {
                var scaleX = box.Width / was.Width;
                var scaleY = box.Height / was.Height;

                var run = World;

                World = new Rect(
                    box.X + (run.X - was.X) * scaleX,
                    box.Y + (run.Y - was.Y) * scaleY,
                    run.Width * scaleX,
                    run.Height * scaleY);

                return;
            }
        }

        // The box that comes in IS the shape's box - the outline lives inside it, so there is nothing to take off.
        World = box;
    }

    public bool HitTest(Vector2 world, double tolerance)
    {
        // A TURNED shape answers by un-turning the question. Everything below is written against a box that stands
        // square, and there is no reason to write it twice: the point is brought back into the shape's own frame and
        // the plain arithmetic answers as it always did.
        if (Transform.IsSomething) world = Transform.Undo(world, Middle);

        // OUT to the tolerance and IN by the whole outline: the box is the outer edge now, so there is nothing of the
        // shape outside it, and everything of the outline is in the band just inside.
        var reach = tolerance;
        var band = Thickness + tolerance;

        switch (Shape)
        {
            case CanvasShape.Line:
            case CanvasShape.Arrow:
            {
                // The LINE only, heads included by the tolerance: a head sits on the end of the line, so what it adds
                // to the reachable area is at most its own half-width - and aiming at an arrow means aiming at the
                // line, not at the triangle on its tip.
                Ends(out var from, out var to);
                return Distance(world, from, to) <= reach;
            }

            case CanvasShape.Ellipse:
            {
                var rx = World.Width / 2;
                var ry = World.Height / 2;
                if (rx <= 0 || ry <= 0) return false;

                var nx = (world.X - (World.X + rx)) / (rx + reach);
                var ny = (world.Y - (World.Y + ry)) / (ry + reach);
                if (nx * nx + ny * ny > 1) return false;

                // Filled, anywhere inside counts. Hollow, only the ring does - otherwise an outline drawn round half the
                // drawing would swallow every press inside it.
                if (Fill != null) return true;

                var ix = (world.X - (World.X + rx)) / Math.Max(1e-9, rx - band);
                var iy = (world.Y - (World.Y + ry)) / Math.Max(1e-9, ry - band);
                return ix * ix + iy * iy >= 1;
            }

            case CanvasShape.Polygon:
            {
                // The corners themselves, not the box: half a triangle's box is empty, and a press in that half would
                // otherwise pick it up.
                var fitted = Fit(World, Sides);
                var inside = false;
                var nearest = double.MaxValue;
                var previous = Vertex(fitted, Sides, Sides - 1);

                for (var i = 0; i < Sides; i++)
                {
                    var current = Vertex(fitted, Sides, i);

                    if (current.Y > world.Y != previous.Y > world.Y &&
                        world.X < (previous.X - current.X) * (world.Y - current.Y) /
                        (previous.Y - current.Y) + current.X)
                        inside = !inside;

                    nearest = Math.Min(nearest, Distance(world, previous, current));
                    previous = current;
                }

                if (Fill != null) return inside || nearest <= reach;
                return nearest <= (inside ? band : reach);
            }

            default:
            {
                var outer = new Rect(World.X - reach, World.Y - reach,
                    World.Width + reach * 2, World.Height + reach * 2);
                if (!Contains(outer, world)) return false;
                if (Fill != null) return true;

                var inner = new Rect(World.X + band, World.Y + band,
                    Math.Max(0, World.Width - band * 2), Math.Max(0, World.Height - band * 2));
                return !Contains(inner, world);
            }
        }
    }

    public void Render(IDrawingSession session, InfiniteCanvas canvas)
    {
        if (canvas == null) return;

        var topLeft = canvas.WorldToScreen(new Vector2(World.X, World.Y));
        var bottomRight = canvas.WorldToScreen(new Vector2(World.X + World.Width, World.Y + World.Height));
        var box = new Rect(topLeft.X, topLeft.Y, bottomRight.X - topLeft.X, bottomRight.Y - topLeft.Y);

        // Never thinner than a pixel: an outline zoomed out should thin to a hair, not vanish. NO outline at all is a
        // different statement, and one somebody makes on purpose - a filled shape with no edge - so a thickness of zero
        // passes straight through instead of being floored to a hairline.
        var width = Thickness > 0 ? Math.Max(Thickness * canvas.Scale, 1.0) : 0;
        var pen = PenFor(width);

        // INSIDE the box. A pen straddles the path it is given, so the path is pulled in by half the width and the
        // outline's outer edge lands exactly on the box - which is what keeps a shape the size it was drawn at however
        // thick its outline is later made. A line has no inside, a box too small to inset keeps what it has, and with
        // no pen there is nothing to make room for.
        var half = width / 2;
        var path = pen == null || Shape == CanvasShape.Line || box.Width <= width || box.Height <= width
            ? box
            : new Rect(box.X + half, box.Y + half, box.Width - width, box.Height - width);

        // TURNED shapes go through a geometry and a MATRIX. The plain calls below take a box that stands square and
        // there is nothing in them to turn; the same shape as a geometry can be handed a transform, which the drawing
        // applies to the real outline rather than to an approximation of it. A shape that is not turned keeps the plain
        // path, which is the one the batches can take.
        if (Transform.IsSomething && Turned(session, path, pen, width, canvas)) return;

        switch (Shape)
        {
            case CanvasShape.Line:
            case CanvasShape.Arrow:
            {
                if (pen == null) return;

                Ends(box, out var from, out var to);

                // THROUGH THE ARROW PASS when the color is a plain one, which is what a line on a plane always is: the
                // whole shape - shaft and both heads - is then decided per pixel from these two points, with no
                // geometry built, kept or handed over. A line is the same thing with nothing on its ends.
                if (PaintRun(session, from, to, width)) return;

                // A line and an arrow are drawn from their END POINTS, so a turn is applied to those rather than to a
                // geometry - which is also what keeps the head pointing along the line it is on.
                if (Transform.IsSomething)
                {
                    var middle = canvas.WorldToScreen(Middle);
                    from = Transform.Apply(from, middle);
                    to = Transform.Apply(to, middle);
                }

                if (Shape != CanvasShape.Arrow)
                {
                    session.DrawLine(from, to, pen);
                    break;
                }

                // The shaft stops at each head's base, so its round cap does not poke past a thick arrow's tip.
                session.DrawLine(Base(from, to, StartHead, width), Base(to, from, EndHead, width), pen);

                Head(session, to, from, EndHead, width, pen);
                Head(session, from, to, StartHead, width, pen);
                break;
            }

            case CanvasShape.Ellipse:
                session.DrawEllipse(path, Fill, 0, 360, EllipseType.Sector, pen);
                break;

            case CanvasShape.Polygon:
                // Fit, not inscribe. The session puts the corners on the ELLIPSE the box holds, so a triangle asked for
                // in a square touches the top edge at one point and leaves the bottom quarter and both sides empty -
                // the shape then sits loose inside the frame that was dragged out for it. Fit hands over the bigger box
                // in which the polygon's OWN bounds land on the one that was asked for.
                session.DrawRegularPolygon(Fill, Fit(path, Sides), Sides, pen, 0, StartAngle);
                break;

            default:
                // Every radius travels with the zoom like the box does, and none is ever more than half the shorter
                // side - past that a "rounder" corner only eats the straight edges and the shape stops changing.
                var limit = Math.Min(path.Width, path.Height) / 2;
                var corners = new CornerRadius(
                    Math.Min(Corner.TopLeft * canvas.Scale, limit),
                    Math.Min(Corner.TopRight * canvas.Scale, limit),
                    Math.Min(Corner.BottomRight * canvas.Scale, limit),
                    Math.Min(Corner.BottomLeft * canvas.Scale, limit));

                if (corners.TopLeft > 0.5 || corners.TopRight > 0.5 ||
                    corners.BottomRight > 0.5 || corners.BottomLeft > 0.5)
                    session.DrawRectangle(Fill, path, corners, pen);
                else session.DrawRectangle(Fill, path, pen);
                break;
        }
    }

    // A turned shape, drawn as its own geometry with the turn as a MATRIX about the middle of the box on screen.
    // False for the shapes that have no geometry to turn - a line and an arrow are drawn from their end points, and
    // those are turned where they are worked out instead.
    private bool Turned(IDrawingSession session, Rect path, Pen pen, double width, InfiniteCanvas canvas)
    {
        // ASKED OF THE TRANSFORM ITSELF, so what is drawn, what is picked and the frame drawn round it are one
        // statement rather than three that have to agree.
        var turn = Transform.Matrix(canvas.WorldToScreen(Middle));

        switch (Shape)
        {
            case CanvasShape.Ellipse:
                session.DrawGeometry(Fill, new EllipseGeometry(path), pen, turn);
                return true;

            case CanvasShape.Polygon:
                session.DrawGeometry(Fill, new RegularPolygonGeometry(Fit(path, Sides), Sides, StartAngle), pen, turn);
                return true;

            case CanvasShape.Rectangle:
                var limit = Math.Min(path.Width, path.Height) / 2;
                session.DrawGeometry(Fill, new RectangleGeometry(path, new CornerRadius(
                    Math.Min(Corner.TopLeft * canvas.Scale, limit),
                    Math.Min(Corner.TopRight * canvas.Scale, limit),
                    Math.Min(Corner.BottomRight * canvas.Scale, limit),
                    Math.Min(Corner.BottomLeft * canvas.Scale, limit))), pen, turn);
                return true;

            default:
                return false;
        }
    }

    // Which corners of the box the line actually runs between, in WORLD units.
    private void Ends(out Vector2 from, out Vector2 to) => Ends(World, out from, out to);

    // The same question of ANY box the line has been mapped into - the screen one, while it is being drawn. One place
    // decides which corners the line runs between, so what is drawn and what the ends report can never disagree.
    private void Ends(Rect box, out Vector2 from, out Vector2 to)
    {
        var start = Flipped ? new Vector2(box.X + box.Width, box.Y) : new Vector2(box.X, box.Y);
        var finish = Flipped
            ? new Vector2(box.X, box.Y + box.Height)
            : new Vector2(box.X + box.Width, box.Y + box.Height);

        from = Reversed ? finish : start;
        to = Reversed ? start : finish;
    }

    // Sets the normalized box plus the lean and direction bits for a line between two points; the drag tool uses it too.
    internal void SetEnds(Vector2 from, Vector2 to)
    {
        World = new Rect(Math.Min(from.X, to.X), Math.Min(from.Y, to.Y),
            Math.Abs(to.X - from.X), Math.Abs(to.Y - from.Y));

        Flipped = (to.X - from.X) * (to.Y - from.Y) < 0;

        Reversed = Flipped
            ? from.X < to.X
            : Math.Abs(to.X - from.X) > 1e-9
                ? from.X > to.X
                : from.Y > to.Y;
    }

    // The whole run - shaft and both heads - as ONE quad whose fragments decide the shape. Returns false when the run
    // cannot be painted that way and the geometry below has to draw it after all: a turn is applied to a geometry and a
    // fill that is not a plain color says nothing about where a line is.
    /// <summary>Whether lines and arrows draw through the one-quad arrow pass rather than a stroked line plus a mesh per head.</summary>
    public static bool PaintsRuns = true;

    private bool PaintRun(IDrawingSession session, Vector2 from, Vector2 to, double width)
    {
        if (!PaintsRuns) return false;
        if (Transform.IsSomething || Stroke is not SolidColorBrush solid || width <= 0) return false;

        _paint ??= new CanvasArrowBrush();
        _paint.From = from;
        _paint.To = to;
        _paint.Thickness = width;
        _paint.Color = solid.Color;
        _paint.StartHead = Shape == CanvasShape.Arrow ? (int)StartHead : 0;
        _paint.EndHead = Shape == CanvasShape.Arrow ? (int)EndHead : 0;
        _paint.HeadLength = width * HeadLength;
        _paint.HeadWidth = width * HeadWidth / 2;

        // The numbers just changed and the brush is the same object, so the paint has to be told - a Vector2 written
        // back with the same value leaves the property system nothing to notice, and the arrow stays baked where it
        // first was. The same reason ink carries one.
        _paint.Revision++;

        // The box is what the record measures overlap and clipping by; the pass places the shape from the two ends
        // rather than from it, so it only has to CONTAIN the arrow.
        var reach = Math.Max(width / 2, _paint.HeadWidth) + 1;

        session.DrawRectangle(_paint, new Rect(
            Math.Min(from.X, to.X) - reach, Math.Min(from.Y, to.Y) - reach,
            Math.Abs(to.X - from.X) + 2 * reach, Math.Abs(to.Y - from.Y) + 2 * reach));

        return true;
    }

    // Where the shaft stops at a headed end: at a triangle's base, or half a thickness short of a barbed head's tip so the
    // round cap lands on it.
    private Vector2 Base(Vector2 tip, Vector2 tail, CanvasArrowHead kind, double width)
    {
        if (kind == CanvasArrowHead.None) return tip;

        var along = tip - tail;
        var span = Math.Sqrt(along.X * along.X + along.Y * along.Y);
        if (span <= 1e-9) return tip;

        // BARBS: far enough back that the shaft's ROUND end clears the head's own outer edge. Half a thickness - where
        // a flat end would go - leaves a disc sitting on the axis where the head has narrowed to almost nothing, and it
        // pokes out through the side as a bulge just behind the point.
        var reach = Math.Max(width, 1e-6) * HeadLength;
        var wide = Math.Max(width, 1e-6) * HeadWidth / 2;
        var wanted = kind == CanvasArrowHead.Triangle
            ? reach
            : width / 2 * Math.Sqrt(reach * reach + wide * wide) / Math.Max(wide, 1e-6);

        var back = Math.Min(wanted, span);

        return new Vector2(tip.X - along.X / span * back, tip.Y - along.Y / span * back);
    }

    // One head, in SCREEN units, pointing from `from` at `tip`.
    private void Head(IDrawingSession session, Vector2 tip, Vector2 from, CanvasArrowHead kind, double width, Pen pen)
    {
        if (kind == CanvasArrowHead.None) return;
        if (!ArrowHead.Points(from, tip, width, HeadLength, HeadWidth, out var left, out var right)) return;

        // A new geometry per record: the renderer holds geometries by reference, and rewriting one it reads flickers. The
        // mesh cache is keyed by content, so an unchanged head costs nothing.
        var geometry = new StreamGeometry();
        var figure = geometry.Open();

        if (kind == CanvasArrowHead.Barbs)
        {
            // ONE stroked polyline and not two separate lines. Two lines meet at the tip with nothing joining them, and
            // at any real thickness the corner does not close: the tip comes out as two overlapping stumps with a notch
            // between them. A single figure has a JOIN there, and the pen's join is a miter, which is the point.
            figure.BeginFigure(left, false, false).LineTo(tip).LineTo(right);
            session.DrawGeometry(null, geometry, pen);
            return;
        }

        figure.BeginFigure(tip, true, true).LineTo(left).LineTo(right).CloseFigure();
        session.DrawGeometry(Stroke, geometry);
    }

    private Pen PenFor(double width)
    {
        if (Stroke == null || Thickness <= 0) return null;
        if (_pen != null && ReferenceEquals(_penBrush, Stroke) && Math.Abs(_penWidth - width) < 1e-6) return _pen;

        _penBrush = Stroke;
        _penWidth = width;
        _pen = new Pen(Stroke, width, penLineJoin: PenLineJoin.Miter);

        return _pen;
    }

    // The box to hand the session so a polygon's OWN bounds land on `box`. The corners sit on the ellipse the box holds,
    // and only a shape with a corner in every quadrant fills it; the rest need a bigger ellipse, off-center by however
    // lopsided they are. Written as the box that carries that ellipse, because that is what the session takes.
    private static Rect Fit(Rect box, int sides)
    {
        double minX = double.MaxValue, maxX = double.MinValue, minY = double.MaxValue, maxY = double.MinValue;
        for (var i = 0; i < sides; i++)
        {
            var angle = (StartAngle + i * 360.0 / sides) * Math.PI / 180;
            var x = Math.Cos(angle);
            var y = Math.Sin(angle);

            minX = Math.Min(minX, x);
            maxX = Math.Max(maxX, x);
            minY = Math.Min(minY, y);
            maxY = Math.Max(maxY, y);
        }

        var rx = box.Width / Math.Max(1e-9, maxX - minX);
        var ry = box.Height / Math.Max(1e-9, maxY - minY);
        var cx = box.X + box.Width / 2 - rx * (minX + maxX) / 2;
        var cy = box.Y + box.Height / 2 - ry * (minY + maxY) / 2;

        return new Rect(cx - rx, cy - ry, rx * 2, ry * 2);
    }

    /// <summary>A polygon's corners in world units, as fitted into its box; empty for other shapes. The one statement of the
    /// fitting rule, used for export too.</summary>
    public IReadOnlyList<Vector2> Outline
    {
        get
        {
            if (Shape != CanvasShape.Polygon) return Array.Empty<Vector2>();

            var sides = Sides;
            var fitted = Fit(World, sides);
            var corners = new Vector2[sides];

            for (var i = 0; i < sides; i++) corners[i] = Vertex(fitted, sides, i);

            return corners;
        }
    }

    private static Vector2 Vertex(Rect fitted, int sides, int index)
    {
        var rx = fitted.Width / 2;
        var ry = fitted.Height / 2;
        var angle = (StartAngle + index * 360.0 / sides) * Math.PI / 180;

        return new Vector2(fitted.X + rx + rx * Math.Cos(angle), fitted.Y + ry + ry * Math.Sin(angle));
    }

    // A box dragged up and to the left has a negative width, and everything that reads a box - hit tests, grips, the
    // renderer - would have to cope with that everywhere instead of here once.
    private static Rect Normalize(Rect rect) =>
        new(Math.Min(rect.X, rect.X + rect.Width), Math.Min(rect.Y, rect.Y + rect.Height),
            Math.Abs(rect.Width), Math.Abs(rect.Height));

    private static bool Contains(Rect rect, Vector2 point) =>
        point.X >= rect.X && point.X <= rect.X + rect.Width &&
        point.Y >= rect.Y && point.Y <= rect.Y + rect.Height;

    private static double Distance(Vector2 point, Vector2 from, Vector2 to)
    {
        var dx = to.X - from.X;
        var dy = to.Y - from.Y;
        var lengthSquared = dx * dx + dy * dy;

        var t = lengthSquared <= double.Epsilon
            ? 0
            : Math.Clamp(((point.X - from.X) * dx + (point.Y - from.Y) * dy) / lengthSquared, 0, 1);

        var px = point.X - (from.X + t * dx);
        var py = point.Y - (from.Y + t * dy);

        return Math.Sqrt(px * px + py * py);
    }
}
