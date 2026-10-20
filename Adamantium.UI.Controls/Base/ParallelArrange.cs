using System.Collections.Concurrent;
using System.Runtime.ExceptionServices;
using Adamantium.UI.Controls.Panels;
using Adamantium.UI.Core;
using Adamantium.UI.Core.RoutedEvents;

namespace Adamantium.UI.Controls.Base;

internal sealed class ParallelArrange
{
    private readonly object gate = new();
    private List<(IUIComponent Item, long Order, MeasurableUIComponent Element, SizeChangedEventArgs Args)> deferred;
    private long order;
    private bool closed;

    public static void Run(Panel panel, int count, Func<IUIComponent, int> positionOf, Action<int> arrangeAt)
    {
        var arrange = new ParallelArrange();
        var outer = panel.ArrangingInParallel;
        Exception arrangeFault = null;
        panel.ArrangingInParallel = arrange;
        try
        {
            Parallel.ForEach(Partitioner.Create(0, count), range =>
            {
                for (var i = range.Item1; i < range.Item2; i++)
                {
                    arrangeAt(i);
                }
            });
        }
        catch (Exception fault)
        {
            arrangeFault = fault;
        }
        finally
        {
            panel.ArrangingInParallel = outer;
        }

        arrange.Raise(positionOf, arrangeFault);
    }

    public static bool TryDefer(MeasurableUIComponent element, SizeChangedEventArgs args)
    {
        IUIComponent item = element;
        for (var node = item.VisualParent; node != null; node = node.VisualParent)
        {
            if (node is Panel { ArrangingInParallel: { } arrange } && arrange.TryAdd(item, element, args))
            {
                return true;
            }

            item = node;
        }

        return false;
    }

    private bool TryAdd(IUIComponent item, MeasurableUIComponent element, SizeChangedEventArgs args)
    {
        lock (gate)
        {
            if (closed)
            {
                return false;
            }

            (deferred ??= []).Add((item, ++order, element, args));
            return true;
        }
    }

    private void Raise(Func<IUIComponent, int> positionOf, Exception arrangeFault)
    {
        List<(IUIComponent Item, long Order, MeasurableUIComponent Element, SizeChangedEventArgs Args)> changes;
        lock (gate)
        {
            closed = true;
            changes = deferred;
        }

        List<Exception> faults = null;
        if (changes != null)
        {
            try
            {
                changes.Sort((a, b) =>
                {
                    var byPosition = Position(positionOf, a.Item).CompareTo(Position(positionOf, b.Item));
                    return byPosition != 0 ? byPosition : a.Order.CompareTo(b.Order);
                });
            }
            catch (Exception fault)
            {
                (faults ??= []).Add(fault);
            }

            foreach (var (_, _, element, args) in changes)
            {
                try
                {
                    if (!TryDefer(element, args))
                    {
                        element.RaiseEvent(args);
                    }
                }
                catch (Exception fault)
                {
                    (faults ??= []).Add(fault);
                }
            }
        }

        if (faults == null)
        {
            if (arrangeFault != null)
            {
                ExceptionDispatchInfo.Throw(arrangeFault);
            }

            return;
        }

        if (arrangeFault is AggregateException all)
        {
            faults.InsertRange(0, all.Flatten().InnerExceptions);
        }
        else if (arrangeFault != null)
        {
            faults.Insert(0, arrangeFault);
        }

        if (faults.Count == 1)
        {
            ExceptionDispatchInfo.Throw(faults[0]);
        }

        throw new AggregateException(faults);
    }

    private static int Position(Func<IUIComponent, int> positionOf, IUIComponent item)
    {
        var position = positionOf(item);
        return position < 0 ? int.MaxValue : position;
    }
}
