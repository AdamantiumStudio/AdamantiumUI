using System.Collections.Generic;
using Adamantium.Graphics.Fonts;
using Adamantium.Mathematics;
using Adamantium.ProceduralGeometry;
using Adamantium.ProceduralGeometry.Shapes;
using Adamantium.UI.Core.Media;
using Adamantium.UI.Core.Media.Imaging;

namespace Adamantium.UI.Core.Graphics;

public interface IDrawingSession
{
    IDrawingSession DrawLine(Vector2<double> start, Vector2<double> end, Pen pen);
    IDrawingSession DrawRectangle(Brush brush, Rect destinationRect, Pen pen = null);
    IDrawingSession DrawRectangle(Brush brush, Rect destinationRect, CornerRadius corners, Pen pen = null);

    /// <summary>A filled rect with a BORDER drawn inside it - each side its own thickness, each corner its own radius.
    /// <para>Not a pen: a pen is one width offset from a contour, and four different widths are not an offset of
    /// anything. Fill and border are ONE draw on purpose - as two shapes they share an outline, both anti-alias it, and
    /// the two halves of that edge composite into a dark hairline all the way round.</para></summary>
    IDrawingSession DrawBorder(Brush background, Rect destinationRect, CornerRadius corners, Brush borderBrush, Thickness borderThickness);
    /// <param name="ringThickness">Leave a RING this thick (DIPs, inward from the outline) instead of a solid shape: an
    /// annulus, or with a sweep an annular sector. It is geometry, not a stroke - so the pen stays free to outline it.</param>
    IDrawingSession DrawEllipse(Rect destinationRect, Brush brush, Double startAngle, Double sweepAngle, EllipseType ellipseType, Pen pen = null, Double ringThickness = 0);
    /// <summary>A REGULAR POLYGON inscribed in the rect: N corners evenly round it, the first on the +x axis. Three is a
    /// triangle and enough of them is a circle, so one shape covers chevrons, ticks, diamonds and hexagons.</summary>
    /// <param name="ringThickness">Leave a RING this thick (DIPs, inward) instead of a solid shape - a hollow triangle,
    /// as geometry rather than a stroke.</param>
    /// <param name="startAngle">Where corner 0 sits, in DEGREES from the +x axis - what stands a triangle on a corner
    /// instead of pointing it right.</param>
    IDrawingSession DrawRegularPolygon(Brush brush, Rect destinationRect, int corners, Pen pen = null, Double ringThickness = 0, Double startAngle = 0);

    IDrawingSession DrawGeometry(Brush brush, Geometry geometry, Pen pen = null);

    /// <summary>Draws the geometry placed by <paramref name="transform"/>, WITHOUT touching the geometry itself. A
    /// <see cref="Media.Drawings.Drawing"/> replays this way: the same shape appears at several sizes and positions in
    /// one frame, and each placement must stay a separate instance of ONE shared mesh. Baking the placement into the
    /// geometry instead would give every size its own tessellation and defeat the instancing outright.</summary>
    IDrawingSession DrawGeometry(Brush brush, Geometry geometry, Pen pen, Matrix4x4F transform);

    /// <summary>Draws the geometry where <paramref name="placement"/> puts it. Rewriting the placement's matrix later
    /// moves the drawing without recording it again - a camera over many objects, each with its own placement.</summary>
    IDrawingSession DrawGeometry(Brush brush, Geometry geometry, Pen pen, Placement placement);

    /// <summary>Draws the rect where <paramref name="placement"/> puts it; moving the placement moves it without recording
    /// it again. The pen is in the drawing's own units and scales with the placement, never thinner on screen than its
    /// <see cref="Pen.MinimumDeviceThickness"/>.</summary>
    IDrawingSession DrawRectangle(Brush brush, Rect destinationRect, CornerRadius corners, Pen pen, Placement placement);

    /// <summary>Draws the ellipse the rect holds where <paramref name="placement"/> puts it - see
    /// <see cref="DrawRectangle(Brush, Rect, CornerRadius, Pen, Placement)"/>.</summary>
    IDrawingSession DrawEllipse(Rect destinationRect, Brush brush, Pen pen, Placement placement);

    /// <summary>Draws the regular polygon the rect holds where <paramref name="placement"/> puts it - see
    /// <see cref="DrawRectangle(Brush, Rect, CornerRadius, Pen, Placement)"/>.</summary>
    IDrawingSession DrawRegularPolygon(Brush brush, Rect destinationRect, int corners, Double startAngle, Pen pen,
        Placement placement);

    IDrawingSession DrawImage(ImageSource image, Brush filter, Rect destinationRect, CornerRadius corners);

    /// <summary>Draws a normalized (0..1) SUB-RECT of the image into <paramref name="destinationRect"/> - a mosaic tile
    /// shows just its fragment of one shared photo without cropping/copying the bitmap.</summary>
    IDrawingSession DrawImage(ImageSource image, Brush filter, Rect destinationRect, CornerRadius corners, Rect sourceUv);

    /// <summary>Draws the image into <paramref name="destinationRect"/> placed by <paramref name="transform"/> - turned
    /// and sheared with it, not laid upright into its bounds: a picture inside a turned drawing, a PDF page's image.
    /// </summary>
    IDrawingSession DrawImage(ImageSource image, Brush filter, Rect destinationRect, Matrix4x4F transform);

    /// <summary>Draws one FRAME of an animated image: its frames are the layers of a single texture, and the frame is
    /// chosen in the shader. Advancing an animation therefore uploads nothing and allocates nothing.</summary>
    IDrawingSession DrawImageFrame(ImageSource image, Brush filter, Rect destinationRect, CornerRadius corners, int frameLayer);

    /// <summary>Draws a normalized (0..1) SUB-RECT of one frame of an animated image into
    /// <paramref name="destinationRect"/> - the part of a cropped or fitted animation its element shows.</summary>
    IDrawingSession DrawImageFrame(ImageSource image, Brush filter, Rect destinationRect, CornerRadius corners, int frameLayer,
        Rect sourceUv);
    IDrawingSession DrawText(TextRenderingParameters renderingParameters, 
        Size<double> desiredSize, 
        TextLayout textLayout,
        Brush foreground, 
        Brush background,
        Brush stroke);
    /// <summary>Draws the text run placed by <paramref name="transform"/>. Needed for the same reason DrawGeometry has
    /// one: a <see cref="Media.Drawings.Drawing"/> puts several runs at their own spots inside a single element, and the
    /// text area cannot say where - it aligns the run WITHIN the layout, while the quad itself is placed by the unit.</summary>
    IDrawingSession DrawText(TextRenderingParameters renderingParameters,
        Size<double> desiredSize,
        TextLayout textLayout,
        Brush foreground,
        Brush background,
        Brush stroke,
        Matrix4x4F transform);
    /// <summary>Draws the text run where <paramref name="placement"/> puts it; moving the placement moves the run
    /// without recording it again.</summary>
    IDrawingSession DrawText(TextRenderingParameters renderingParameters,
        Size<double> desiredSize,
        TextLayout textLayout,
        Brush foreground,
        Brush background,
        Brush stroke,
        Placement placement);
    /// <summary>Draws the text with <paramref name="effects"/>: ranges of it moved and recolored, the layout as it
    /// is.</summary>
    IDrawingSession DrawText(TextRenderingParameters renderingParameters,
        Size<double> desiredSize,
        TextLayout textLayout,
        Brush foreground,
        Brush background,
        Brush stroke,
        IReadOnlyList<GlyphEffect> effects);
    /// <summary>Draws only the glyphs of the layout touching <paramref name="window"/>, in its units: a block drawing one
    /// frame of a layout another block owns.</summary>
    IDrawingSession DrawText(TextRenderingParameters renderingParameters,
        Size<double> desiredSize,
        TextLayout textLayout,
        Brush foreground,
        Brush background,
        Brush stroke,
        IReadOnlyList<GlyphEffect> effects,
        RectangleF window);

    /// <summary>Draws glyphs the caller placed (a PDF page's text) where <paramref name="transform"/> puts the run: filled
    /// with <paramref name="fill"/> through the glyph atlas, then outlined with <paramref name="stroke"/> as paths. Either
    /// may be null.</summary>
    IDrawingSession DrawGlyphs(GlyphRun run, Brush fill, Pen stroke, Matrix4x4F transform);

    /// <summary>Draws triangles with a color at each corner where <paramref name="transform"/> puts them, each triangle's
    /// color running smoothly between its corners - exact at any zoom, as a mesh gradient is.</summary>
    IDrawingSession DrawColoredMesh(ColoredMesh mesh, Matrix4x4F transform);

    /// <summary>Starts a group: everything drawn until the matching <see cref="PopGroup"/> is drawn apart and laid down as
    /// <paramref name="options"/> say - cut, masked, blended, faded. Groups nest.</summary>
    IDrawingSession PushGroup(DrawingGroupOptions options);

    /// <summary>Ends the group the last <see cref="PushGroup"/> started; one left open ends with the element's
    /// drawing.</summary>
    IDrawingSession PopGroup();

    /// <summary>Cuts what is drawn until <see cref="PopClip"/> to <paramref name="clip"/>, its corners rounded by
    /// <paramref name="corners"/>, both in the units of <paramref name="placement"/>: rewriting the placement's matrix
    /// moves the cut without drawing anything again. A clip pushed inside another cuts by both. The cut follows the
    /// axes of the screen, so a placement may move, scale and turn by quarter turns.</summary>
    IDrawingSession PushClip(Rect clip, CornerRadius corners, Placement placement);

    /// <summary>Ends the clip the last <see cref="PushClip"/> started; one left open ends with the element's drawing.
    /// </summary>
    IDrawingSession PopClip();

    IDrawingSession PushImage(ImageSource image);
}