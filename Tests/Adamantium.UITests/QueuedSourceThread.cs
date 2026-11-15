using System;
using System.Collections.Generic;
using System.Threading;
using Adamantium.UI.Core.Data;

namespace Adamantium.UITests;

/// <summary>A source's thread that keeps the updates handed to it until told to run them - on a thread of its own, as a
/// game world's would. It holds every source, or only those <see cref="Holding"/> says.</summary>
internal sealed class QueuedSourceThread : ISourceThread
{
    private readonly List<Action> _waiting = [];

    public Func<object, bool> Holding { get; init; } = _ => true;

    public int Waiting => _waiting.Count;

    public bool Holds(object source) => Holding(source);

    public void Run(Action update) => _waiting.Add(update);

    /// <summary>Runs every waiting update on another thread, and returns once they have run.</summary>
    public void RunAll()
    {
        var updates = _waiting.ToArray();
        _waiting.Clear();
        Exception failed = null;
        var thread = new Thread(() =>
        {
            try
            {
                foreach (var update in updates)
                    update();
            }
            catch (Exception exception)
            {
                failed = exception;
            }
        });
        thread.Start();
        thread.Join();
        if (failed != null)
            throw new AggregateException(failed);
    }
}
