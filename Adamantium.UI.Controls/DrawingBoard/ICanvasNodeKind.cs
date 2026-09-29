namespace Adamantium.UI.Controls.DrawingBoard;

/// <summary>One node kind in the application's catalog bound to the canvas; asked for a node whenever the canvas needs one.</summary>
public interface ICanvasNodeKind
{
    /// <summary>The word a node of this kind carries, and what a file names it by.</summary>
    string Kind { get; }

    /// <summary>What the palette shows. The kind itself when there is nothing better to say.</summary>
    string Title { get; }

    /// <summary>WHICH FAMILY OF WORK it belongs to - "Math", "Color". What a palette puts its sections in, the same way
    /// the tool rail groups tools; empty means it belongs to no section and stands on its own.
    /// <para>A section is not a SET: a set of kinds is a whole catalog bound to the canvas, and a graph made with one
    /// is not made with another. Sections organize what is inside one catalog.</para></summary>
    string Group => string.Empty;

    /// <summary>WHAT COLOR a node of this kind is - what a screenful of them is read by at a glance, and the color a
    /// new one is born with. Null leaves it to the theme, which is what a catalog that has not been asked to think
    /// about color says.</summary>
    Adamantium.Mathematics.Color? Accent { get; }

    /// <summary>A fresh specialization of this kind - one per node, never shared.</summary>
    ICanvasNodeSpecialization Create();

    /// <summary>A whole node of this kind, the application's own object; the canvas places it afterward.</summary>
    ICanvasNode Make();
}