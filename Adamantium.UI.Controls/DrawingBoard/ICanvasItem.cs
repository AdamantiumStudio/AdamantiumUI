using Adamantium.Mathematics;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Graphics;

namespace Adamantium.UI.Controls.DrawingBoard;

/// <summary>One thing ON the canvas - a stroke of ink, a shape, a piece of text.
/// <para>Items are DATA, not elements: they have no property storage, no node in the visual tree and no node in the hit
/// tree. That is what lets a drawing hold tens of thousands of them, and it is the only reason they are not simply
/// controls - a control on the canvas is a perfectly good way to put something there, and costs nothing at zoom.</para>
/// </summary>
public interface ICanvasItem
{
    /// <summary>What it covers, in WORLD units. The canvas draws the items whose bounds it can see and no others, so
    /// this is what makes the cost of a frame depend on what is visible rather than on what exists.</summary>
    Rect Bounds { get; }

    /// <summary>What to call it in a list of what is on the plane. The type's name by default, so a third-party item
    /// shows something sensible without being asked to; anything that can say more - which shape it is, what the text
    /// says - should.</summary>
    string Title => GetType().Name;

    /// <summary>Which grips of the manipulation frame this item offers. Everything by default, which is what a box
    /// wants; something reshaped another way - by its own points, or by whatever it is attached to - says so.</summary>
    CanvasHandles Handles => CanvasHandles.All;

    /// <summary>Paint-order position from the back (0 is under everything), stamped by the scene; writing it asks to move
    /// the item there.</summary>
    int Order { get; set; }

    /// <summary>The narrowest name of this item ("Rectangle", "Bezier"), else the class name; the inspector matches its
    /// sections against it.</summary>
    string Sort => GetType().Name;

    /// <summary>Whether it has a place of its own; false for a wire, which follows its sockets and is skipped by alignment.</summary>
    bool IsPlaced => true;

    /// <summary>Which kind of work this belongs to. A DRAWING by default, because that is what almost everything on a
    /// plane is; a node and the wire between two of them say otherwise. The canvas shows, picks and selects only what
    /// belongs to the mode it is in - see <see cref="CanvasMode"/>.</summary>
    CanvasMode Mode => CanvasMode.Drawing;

    /// <summary>A new item like this one at the same place, for copy and duplicate; null when it cannot be copied (a wire, an
    /// application control).</summary>
    ICanvasItem Copy() => null;

    /// <summary>The one color this item reads as, or null; each item knows where its own color lives.</summary>
    Color? Paint => null;

    /// <summary>...and paints it that color. Does nothing for an item that has no color to set, which is the honest
    /// answer rather than a refusal: whoever paints a selection paints what can be painted and leaves the rest.</summary>
    void PaintWith(Color color) { }

    /// <summary>Whether a world point is ON this item. The tolerance is a WORLD length the canvas works out from a
    /// screen one - what counts as a hit has to be the same distance under the cursor at any zoom.</summary>
    bool HitTest(Vector2 world, double tolerance);

    /// <summary>Draw it. The points are handed over already in SCREEN coordinates by the canvas, which is what keeps the
    /// numbers reaching the GPU small however far from the origin the item is.</summary>
    void Render(IDrawingSession session, InfiniteCanvas canvas);

    /// <summary>Moves it by a WORLD distance. Separate from <see cref="Resize"/> because moving is the one edit an item
    /// can always do exactly - a stroke moves by one field, not by a walk over its points.</summary>
    void Move(Vector2 worldDelta);

    /// <summary>The least it can be resized to, in world units; zero for ink and shapes, the content's own size for a
    /// control.</summary>
    Size Smallest => new();

    /// <summary>Puts it inside a new box, in WORLD units - what a resize grip does. Afterwards <see cref="Bounds"/> is
    /// that box, so dragging a grip a second time starts from where the first one left off instead of drifting.</summary>
    void Resize(Rect world);
}
