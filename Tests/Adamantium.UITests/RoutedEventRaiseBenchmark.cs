using System;
using System.Diagnostics;
using Adamantium.UI.Controls.Decorators;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Input;
using Adamantium.UI.Core.RoutedEvents;
using NUnit.Framework;

namespace Adamantium.UITests;

/// <summary>
/// What raising a routed event costs on a route as deep as a real window's: 20 elements, a handler on every fifth.
/// Pointer moves bubble past a class handler on every element, the preview tunnels with none. Explicit: a measurement,
/// not a pass/fail, taken once the JIT has gone quiet (the first rounds run on unoptimized code). Run it with
/// --filter FullyQualifiedName~RoutedEventRaiseBenchmark.
/// </summary>
[TestFixture, Explicit("A measurement, not an assertion - run it by name.")]
public class RoutedEventRaiseBenchmark
{
    private const int Depth = 20;
    private const int Raises = 200_000;

    [Test]
    public void RaisingThroughTwentyLevels()
    {
        var chain = new Border[Depth];
        for (var i = 0; i < Depth; i++)
        {
            chain[i] = new Border();
            if (i > 0)
            {
                chain[i - 1].Child = chain[i];
            }
        }

        var heard = 0;
        void OnMove(object sender, PointerEventArgs e) => heard++;
        for (var i = 0; i < Depth; i += 5)
        {
            chain[i].PointerMoved += OnMove;
            chain[i].AddHandler(PointerEvents.PreviewPointerMovedEvent, new PointerEventHandler(OnMove));
        }

        var leaf = chain[Depth - 1];
        var route = 0;
        for (IObservableComponent node = leaf; node != null; node = node.ObservableParent)
        {
            route++;
        }

        Assert.That(route, Is.EqualTo(Depth), "the chain must be one visual route");

        Measure("bubble, class handler on each", leaf, PointerEvents.PointerMovedEvent);
        Measure("tunnel, no class handler    ", leaf, PointerEvents.PreviewPointerMovedEvent);
        TestContext.WriteLine($"(heard {heard:N0} - kept so nothing is optimized away)");
    }

    private static void Measure(string label, Border leaf, RoutedEvent routedEvent)
    {
        var args = new PointerEventArgs(Mouse.Pointer, InputModifiers.None, 0) { RoutedEvent = routedEvent };

        var quietSince = Stopwatch.StartNew();
        var compiled = System.Runtime.JitInfo.GetCompiledMethodCount();
        var warming = Stopwatch.StartNew();
        while (quietSince.ElapsedMilliseconds < 500 && warming.ElapsedMilliseconds < 10_000)
        {
            RaiseMany(leaf, args, 10_000);
            var now = System.Runtime.JitInfo.GetCompiledMethodCount();
            if (now != compiled)
            {
                compiled = now;
                quietSince.Restart();
            }
        }

        var best = double.MaxValue;
        long bytes = 0;
        for (var round = 0; round < 5; round++)
        {
            var allocated = GC.GetAllocatedBytesForCurrentThread();
            var clock = Stopwatch.StartNew();
            RaiseMany(leaf, args, Raises);
            clock.Stop();
            bytes = GC.GetAllocatedBytesForCurrentThread() - allocated;
            best = Math.Min(best, clock.Elapsed.TotalMilliseconds * 1_000_000 / Raises);
        }

        TestContext.WriteLine($"{label}: {best,8:F0} ns/raise  {bytes / (double)Raises,6:F0} B/raise");
    }

    private static void RaiseMany(Border leaf, PointerEventArgs args, int count)
    {
        for (var i = 0; i < count; i++)
        {
            args.Handled = false;
            args.Source = null;
            args.OriginalSource = null;
            leaf.RaiseEvent(args);
        }
    }
}
