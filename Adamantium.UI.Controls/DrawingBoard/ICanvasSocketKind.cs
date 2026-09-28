namespace Adamantium.UI.Controls.DrawingBoard;

/// <summary>One kind a socket may carry and its color; the color belongs to the kind, so sockets of one kind look alike.</summary>
public interface ICanvasSocketKind
{
    /// <summary>The word a socket carries - "Number", "Color". Empty means "anything".</summary>
    string Kind { get; }

    /// <summary>What that looks like. Null leaves the pin wearing the theme's.</summary>
    Adamantium.Mathematics.Color? Color { get; }
}