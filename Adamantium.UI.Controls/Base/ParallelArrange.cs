using System.Collections.Concurrent;
using System.Runtime.ExceptionServices;
using Adamantium.UI.Core.RoutedEvents;

namespace Adamantium.UI.Controls.Base;

internal static class ParallelArrange
{
    [ThreadStatic]
    private static bool deferring;

    [ThreadStatic]
    private static List<(MeasurableUIComponent Element, SizeChangedEventArgs Args)> pending;

    public static bool TryDefer(MeasurableUIComponent element, SizeChangedEventArgs args)
    {
        if (!deferring)
        {
            return false;
        }

        (pending ??= []).Add((element, args));
        return true;
    }

    public static void Run(int count, Action<int> arrangeAt)
    {
        ConcurrentBag<(int Start, (MeasurableUIComponent Element, SizeChangedEventArgs Args)[] Changes)> collected = [];
        Exception arrangeFault = null;
        try
        {
            Parallel.ForEach(Partitioner.Create(0, count), range =>
            {
                var outer = deferring;
                var start = pending?.Count ?? 0;
                deferring = true;
                try
                {
                    for (var i = range.Item1; i < range.Item2; i++)
                    {
                        arrangeAt(i);
                    }
                }
                finally
                {
                    deferring = outer;
                    if (pending != null && pending.Count > start)
                    {
                        collected.Add((range.Item1, [.. pending.Skip(start)]));
                        pending.RemoveRange(start, pending.Count - start);
                    }
                }
            });
        }
        catch (Exception fault)
        {
            arrangeFault = fault;
        }

        var raiseFaults = collected.IsEmpty ? null : Raise(collected);
        if (raiseFaults == null)
        {
            if (arrangeFault != null)
            {
                ExceptionDispatchInfo.Throw(arrangeFault);
            }

            return;
        }

        if (arrangeFault is AggregateException all)
        {
            raiseFaults.InsertRange(0, all.InnerExceptions);
        }
        else if (arrangeFault != null)
        {
            raiseFaults.Insert(0, arrangeFault);
        }

        if (raiseFaults.Count == 1)
        {
            ExceptionDispatchInfo.Throw(raiseFaults[0]);
        }

        throw new AggregateException(raiseFaults);
    }

    private static List<Exception> Raise(
        ConcurrentBag<(int Start, (MeasurableUIComponent Element, SizeChangedEventArgs Args)[] Changes)> collected)
    {
        List<Exception> faults = null;
        foreach (var (_, changes) in collected.OrderBy(c => c.Start))
        {
            foreach (var (element, args) in changes)
            {
                if (TryDefer(element, args))
                {
                    continue;
                }

                try
                {
                    element.RaiseEvent(args);
                }
                catch (Exception fault)
                {
                    (faults ??= []).Add(fault);
                }
            }
        }

        return faults;
    }
}
