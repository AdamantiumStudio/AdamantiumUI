using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Diagnostics;
using Adamantium.UI.Core.RoutedEvents;
using Adamantium.UI.Diagnostics;

namespace Adamantium.UI.Sandbox.Diagnostics;

/// <summary>A debugging tool that writes every routed event and every handler on its route to a file, and a count per
/// event when detached. The sandbox attaches it when ADAM_EVENT_TRACE names the file.</summary>
public sealed class EventTraceTool : IDebugTool, IRoutedEventObserver
{
    private readonly object writeLock = new();
    private readonly string path;
    private readonly HashSet<RoutedEvent> watched = [];
    private readonly Dictionary<RoutedEvent, (int Raises, int Handlers, TimeSpan Spent)> totals = new();
    private StreamWriter writer;
    private bool attached;

    public EventTraceTool(string path)
    {
        this.path = path;
    }

    public void Attach(UIApplication application)
    {
        lock (writeLock)
        {
            writer = new StreamWriter(path, false);
            writer.WriteLine($"event trace {DateTime.Now:O}");
            totals.Clear();
            attached = true;
        }

        EventManager.RoutedEventRegistered += Watch;
        foreach (var routedEvent in EventManager.GetRoutedEvents())
        {
            Watch(routedEvent);
        }
    }

    public void Detach()
    {
        EventManager.RoutedEventRegistered -= Watch;
        lock (writeLock)
        {
            attached = false;
            foreach (var routedEvent in watched)
            {
                routedEvent.RemoveObserver(this);
            }

            watched.Clear();
            if (writer == null)
            {
                return;
            }

            try
            {
                writer.WriteLine("totals: raises, handlers, time in handlers");
                foreach (var (routedEvent, total) in totals.OrderByDescending(t => t.Value.Raises))
                {
                    writer.WriteLine(
                        $"{total.Raises,8} {total.Handlers,8} {total.Spent.TotalMilliseconds,10:F3} ms  {routedEvent}");
                }
            }
            catch (IOException)
            {
            }
            finally
            {
                Close();
            }
        }
    }

    public void OnRaised(IObservableComponent source, RoutedEventArgs e)
    {
        var line = $"> {e.RoutedEvent.Name} {e.RoutedEvent.RoutingStrategy} from {Describe(source)} " +
                   $"[thread {Environment.CurrentManagedThreadId}]";
        lock (writeLock)
        {
            var total = totals.GetValueOrDefault(e.RoutedEvent);
            totals[e.RoutedEvent] = (total.Raises + 1, total.Handlers, total.Spent);
            Write(line);
        }
    }

    public void OnHandlerInvoked(in RoutedEventHandlerCall call)
    {
        string outcome;
        if (call.Exception != null)
        {
            outcome = "threw " + call.Exception.GetType().Name;
        }
        else if (call.Skipped)
        {
            outcome = "skipped";
        }
        else
        {
            outcome = $"ran {call.Elapsed.TotalMilliseconds * 1000:F1} us";
        }

        var handled = (call.WasHandled, call.IsHandled) switch
        {
            (false, true) => " -> handled",
            (true, false) => " -> unhandled",
            _ => "",
        };
        var routedEvent = call.Args.RoutedEvent;
        var method = call.Handler.Method;
        var line = $"    {routedEvent.Name}: {Describe(call.Element)} {(call.IsClassHandler ? "class" : "own")} " +
                   $"{method.DeclaringType?.Name}.{method.Name} {outcome}{handled} " +
                   $"[thread {Environment.CurrentManagedThreadId}]";
        var elapsed = call.Elapsed;
        lock (writeLock)
        {
            var total = totals.GetValueOrDefault(routedEvent);
            totals[routedEvent] = (total.Raises, total.Handlers + 1, total.Spent + elapsed);
            Write(line);
        }
    }

    public void OnRouteFinished(IObservableComponent source, RoutedEventArgs e)
    {
    }

    private void Watch(RoutedEvent routedEvent)
    {
        lock (writeLock)
        {
            if (attached && watched.Add(routedEvent))
            {
                routedEvent.AddObserver(this);
            }
        }
    }

    private void Write(string line)
    {
        if (writer == null)
        {
            return;
        }

        try
        {
            writer.WriteLine(line);
        }
        catch (IOException)
        {
            Close();
        }
    }

    private void Close()
    {
        try
        {
            writer.Dispose();
        }
        catch (IOException)
        {
        }
        finally
        {
            writer = null;
        }
    }

    private static string Describe(IObservableComponent element)
    {
        if (element == null)
        {
            return "-";
        }

        var name = (element as IName)?.Name;
        return string.IsNullOrEmpty(name) ? element.GetType().Name : $"{element.GetType().Name}#{name}";
    }
}
