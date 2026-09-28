namespace Adamantium.UI.Controls.DrawingBoard;

/// <summary>What a node is, apart from its common shell: its kind's sockets and edited state, and the content its body
/// template binds to. Swapped on a kind change, keeping the node.</summary>
public interface ICanvasNodeSpecialization
{
    /// <summary>Puts this kind's sockets on the node. Called when it is installed, with the node's own lists to fill -
    /// they live on the node, because an inspector edits them and a wire sits on them.</summary>
    void Shape(ICanvasNode node);
}