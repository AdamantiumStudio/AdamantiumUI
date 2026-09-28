using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Adamantium.UI.Core.Dispatcher;
using Adamantium.UI.Platforms;

namespace Adamantium.UI.Threading;

internal class DispatcherOperationExecutor
{
    private Queue<IDispatcherOperation>[] operationQueue = Enumerable
        .Range(0, (int) DispatcherPriority.MaxValue + 1).
        Select(_ => new Queue<IDispatcherOperation>()).ToArray();
    private IApplicationPlatform platform;
    private object locker = new object();

    public DispatcherOperationExecutor(IApplicationPlatform platform)
    {
        this.platform = platform;
    }

    public void Invoke(Action action, DispatcherPriority priority)
    {
        if (action == null) throw new ArgumentNullException(nameof(action));
            
        var operation = new DispatcherOperation(action, priority);
        AddOperation(operation);
    }

    public Task InvokeAsync(Action action, DispatcherPriority priority)
    {
        if (action == null) throw new ArgumentNullException(nameof(action));
            
        var operation = new DispatcherOperation(action, priority);
        AddOperation(operation);
        return operation.Task;
    }
        
    public Task InvokeAsync(Delegate method, object args, int numArgs)
    {
        if (method == null) throw new ArgumentNullException(nameof(method));
            
        var operation = new DispatcherOperation(method, args, numArgs);
        AddOperation(operation);
        return operation.Task;
    }

    // Operations are dequeued under the lock but run outside it, since running foreign code under it can deadlock.
    public void Execute()
    {
        for (var i = (int) DispatcherPriority.MaxValue; i >= (int) DispatcherPriority.MinValue; i--)
        {
            var queue = operationQueue[i];

            while (true)
            {
                IDispatcherOperation operation;

                lock (locker)
                {
                    if (queue.Count == 0) break;

                    operation = queue.Dequeue();
                }

                operation.Run();
            }
        }
    }

    private void AddOperation(IDispatcherOperation operation)
    {
        lock (locker)
        {
            var queue = operationQueue[(int)operation.Priority];
            bool sendNotification = queue.Count == 0;
            queue.Enqueue(operation);

            if (sendNotification)
            {
                platform?.Signal();
            }
        }
            
    }
}