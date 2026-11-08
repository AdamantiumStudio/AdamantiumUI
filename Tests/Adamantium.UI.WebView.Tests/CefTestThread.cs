using System;
using System.Collections.Concurrent;
using System.Runtime.ExceptionServices;
using System.Threading;

namespace Adamantium.UI.WebView.Tests;

/// <summary>The one thread CEF is called on in a run, as an application's UI loop is: CEF is initialized on it and
/// takes every later call there, whichever thread the test framework runs a test on.</summary>
internal sealed class CefTestThread : IDisposable
{
    private readonly BlockingCollection<Action> work = new();
    private readonly Thread thread;

    public CefTestThread()
    {
        thread = new Thread(() =>
        {
            foreach (var action in work.GetConsumingEnumerable())
            {
                action();
            }
        }) { IsBackground = true, Name = "CEF" };
        thread.Start();
    }

    /// <summary>Runs <paramref name="function"/> on the thread and returns what it does, or throws what it threw.</summary>
    public T Run<T>(Func<T> function)
    {
        T result = default;
        Exception failure = null;
        using var done = new ManualResetEventSlim();
        work.Add(() =>
        {
            try
            {
                result = function();
            }
            catch (Exception exception)
            {
                failure = exception;
            }

            done.Set();
        });
        done.Wait();
        if (failure != null)
        {
            ExceptionDispatchInfo.Throw(failure);
        }

        return result;
    }

    public void Run(Action action) => Run<object>(() =>
    {
        action();
        return null;
    });

    public void Dispose()
    {
        work.CompleteAdding();
        thread.Join();
        work.Dispose();
    }
}
