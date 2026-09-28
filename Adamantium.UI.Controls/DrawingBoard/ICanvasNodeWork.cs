using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Adamantium.UI.Controls.DrawingBoard;

/// <summary>What a node computes, carried by its <see cref="ICanvasNodeSpecialization"/>; <see cref="CanvasGraphRunner"/>
/// handles order and scheduling. Asynchronous via <see cref="ValueTask{TResult}"/>, free for immediate answers.</summary>
public interface ICanvasNodeWork
{
    /// <param name="inputs">What arrived, one entry per input socket and in the node's own socket order, so a node can
    /// read them either way: by what a socket CARRIES, or by where it sits.</param>
    /// <param name="token">Dropped when what this pass was working out has changed underneath it - a node that takes
    /// real time is expected to look at it and give up, because the answer it is making is already stale.</param>
    ValueTask<object> Evaluate(IReadOnlyList<CanvasArrival> inputs, CancellationToken token);
}
