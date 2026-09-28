using Adamantium.UI.Core;
using Adamantium.UI.Core.Graphics;
using Adamantium.UI.Core.Input;
using Adamantium.UI.Core.RoutedEvents;

namespace Adamantium.UI.Controls.DrawingBoard;

/// <summary>What the left button does, as a swappable object, so a new tool is a new class. Panning and zooming stay with the
/// canvas.</summary>
public interface ICanvasTool
{
    /// <summary>Whether a gesture is in progress that spans releases of the button - a line being drawn point to point.
    /// The canvas asks so that swapping tools, or losing the pointer, can finish it rather than leave it hanging.
    /// </summary>
    bool IsBusy { get; }

    /// <summary>What this tool is called, for the button that picks it and the tip that explains it.
    /// <para>Stated by the TOOL and not by the markup around it, which is the whole point: adding a tool is a class,
    /// not a class plus a command plus a flag plus a button plus an icon. What a tool is called and what it looks like
    /// are facts about the tool.</para></summary>
    string Name => GetType().Name;

    /// <summary>The KEY of the picture to show - a theme resource, resolved live, so a tool keeps its look through a
    /// theme swap and a theme can give it a different one. Empty means no picture, and the rail falls back to the
    /// name.</summary>
    string Icon => string.Empty;

    /// <summary>The key that picks this tool, or <see cref="Key.None"/> for none. The canvas reads it and spends the
    /// key only when no tool wanted it first; an application that disagrees sets a different one on the tool it
    /// built.</summary>
    Key Shortcut => Key.None;

    /// <summary>A longer line for the tip, under the name. Empty by default - most tools are what their name says.
    /// </summary>
    string Description => string.Empty;

    /// <summary>The tool's family, or empty; a family shares one rail button that opens a list, so a large family does not
    /// overflow the rail.</summary>
    string Group => string.Empty;

    /// <summary>Whether this tool has anything to do in a given mode. Everywhere by default - select, pan and delete
    /// mean the same thing whatever is on the plane - and a tool that makes one KIND of thing says where it belongs.
    /// The rail offers only what the canvas's mode admits.</summary>
    bool WorksIn(CanvasMode mode) => true;

    /// <summary>The pointer this tool wears. The arrow by default, and a tool that draws should say otherwise: which
    /// tool is in hand is otherwise only told by a button in a rail the eye is not on, and a tool put down by the right
    /// button announces itself nowhere at all. The pointer is the one place a person is already looking.</summary>
    Cursor Cursor => Cursors.Arrow;

    void OnPressed(InfiniteCanvas canvas, CanvasPointerEventArgs e);

    void OnMoved(InfiniteCanvas canvas, CanvasPointerEventArgs e);

    void OnReleased(InfiniteCanvas canvas, CanvasPointerEventArgs e);

    /// <summary>A KEY, offered before the canvas reads it for itself. Empty by default: most tools answer the pointer
    /// only, and making every one of them carry an empty method would say nothing about them.
    /// <para>First refusal, and it matters: the canvas spends Delete on the selection, Escape on letting go and space on
    /// panning, and a tool with a caret in a word needs all three to mean what they mean while typing.</para></summary>
    void OnKey(InfiniteCanvas canvas, KeyEventArgs e)
    {
    }

    /// <summary>Characters, already turned into text by the keyboard layout - which is the only thing that knows what a
    /// key means on the user's own keyboard.</summary>
    void OnText(InfiniteCanvas canvas, TextInputEventArgs e)
    {
    }

    /// <summary>Draw what is being made but is not in the scene yet, and whatever the tool shows about itself. Called
    /// last, over the scene, in SCREEN coordinates.</summary>
    void Render(IDrawingSession session, InfiniteCanvas canvas);

    /// <summary>Finish or abandon whatever is in progress - the tool is being put down, or the pointer is gone.</summary>
    void Cancel(InfiniteCanvas canvas);
}
