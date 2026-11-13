using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Adamantium.Mathematics;
using Adamantium.Navigation;
using Adamantium.UI.Controls.Docking;
using Adamantium.UI.Controls.Panels;
using Adamantium.UI.Core;
using Adamantium.UI.Extensions;
using Adamantium.UI.Rendering;
using Adamantium.UITests.Rendering;
using NUnit.Framework;

namespace Adamantium.UITests;

/// <summary>
/// A close that waited for an answer goes on on the loop thread. The loop thread has no synchronization context, so the
/// rest of the close - the panel that empties going away - ran on the thread pool, rebuilding the layout while the loop
/// laid it out and recorded it: the render cache kept the panel's chrome in the paint order, and the frame verifier
/// found a panel drawn that the tree no longer had.
/// </summary>
[TestFixture]
public class DockingCloseOnTheLoopTests
{
    private sealed class TestWindowRoot : Grid, IRootVisualComponent
    {
        public Vector2<double> PointToClient(PixelPoint point) => new((float)point.X, (float)point.Y);
        public PixelPoint PointToScreen(Vector2<double> point) => new(point.X, point.Y);
        public PixelPoint Position { get; set; }
        public void AttachContextAndInitialize(IUIContext context) { }
        public double Left { get; set; }
        public double Top { get; set; }
        public string Title { get; set; }
        public double ClientWidth { get; set; }
        public double ClientHeight { get; set; }
        public IUIContext UIContext => null;
    }

    private static void Frame(TestWindowRoot root, RenderCache cache)
    {
        WindowExtension.UpdateTree(root);
        WindowExtension.UpdateTree(root);
        cache.RecordFrame(root);
        cache.ApplyFrame();
    }

    // What the frame verifier asks: every drawing in the paint order belongs to a component the tree still reaches.
    private static List<IUIComponent> DrawnButNotInTheTree(TestWindowRoot root, RenderCache cache)
    {
        var reached = new HashSet<IUIComponent>();
        var stack = new Stack<IUIComponent>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var component = stack.Pop();
            if (component.Visibility == Visibility.Collapsed || !reached.Add(component)) continue;
            foreach (var child in component.VisualChildren) stack.Push(child);
        }

        return cache.DescribeGroups().Select(group => group.Component).Where(component => !reached.Contains(component)).ToList();
    }

    private static PaneGroup GroupOf(DockingArea area, string paneId) =>
        area.GetVisualDescendants().OfType<PaneGroup>().First(group => group.Items.OfType<Pane>().Any(pane => pane.Id == paneId));

    // The editor's case: two documents side by side and a tool beside them; the changed one is closed, the question is
    // answered, and its panel goes away with it.
    [Test]
    public void APanelEmptiedByACloseThatWaitedForTheAnswer_GoesAwayOnTheLoopThread()
    {
        var area = new DockingArea();
        var root = new TestWindowRoot { Width = 1000, Height = 700, ClientWidth = 1000, ClientHeight = 700 };
        root.Children.Add(area);
        var cache = new RenderCache(new DrawingContext(), new FakeRenderUnitFactory());
        Frame(root, cache);

        area.AddPane(new Pane { Id = "hierarchy", Header = "Hierarchy" }, DockZone.Left);
        area.AddPane(new Pane { Id = "duel", Header = "Duel" });
        area.AddPane(new Pane { Id = "siege", Header = "Siege", IsDirty = true });
        Assume.That(area.DockBeside("siege", "duel", DockZone.Right), Is.True);
        Frame(root, cache);
        Frame(root, cache);

        var leaving = GroupOf(area, "siege");
        Assume.That(GroupOf(area, "duel"), Is.Not.SameAs(leaving), "side by side, each in its own panel");

        // The user's answer arrives the way a click on the question does: on the loop, completing a task whose
        // continuations do not run inside the click.
        var answered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        area.UnsavedClosing += async (_, e) =>
        {
            await answered.Task;
            e.Answer = UnsavedAnswer.Discard;
        };

        var leftOn = new List<int>();
        leaving.DetachedFromVisualTreeEvent += (_, _) => leftOn.Add(Environment.CurrentManagedThreadId);
        var closedOn = new List<int>();
        area.PaneClosed += (_, _) => closedOn.Add(Environment.CurrentManagedThreadId);

        var loop = Environment.CurrentManagedThreadId;
        var previousContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(null);   // as on the loop thread: there is none
        LoopSignal.Claim();
        try
        {
            var close = area.ClosePaneAsync("siege");
            Frame(root, cache);
            Assume.That(close.IsCompleted, Is.False, "the close waits for the answer");

            answered.SetResult();

            // This thread is the loop: it runs what is posted to it until the close is done.
            var waited = Stopwatch.StartNew();
            while (!close.IsCompleted && waited.Elapsed < TimeSpan.FromSeconds(10))
            {
                LoopSignal.Drain();
                close.Wait(TimeSpan.FromMilliseconds(1));
            }

            Assert.That(close.IsCompleted, Is.True, "the close finishes");
            Assert.That(close.Result, Is.True, "don't save: it closes");

            LoopSignal.Drain();
            Frame(root, cache);
            Frame(root, cache);

            Assert.Multiple(() =>
            {
                Assert.That(leftOn, Is.Not.Empty.And.All.EqualTo(loop), "the emptied panel leaves the tree on the loop thread");
                Assert.That(closedOn, Is.EqualTo(new[] { loop }), "the close is announced on the loop thread");
                Assert.That(leaving.IsAttachedToVisualTree, Is.False, "the panel is gone");
                Assert.That(leaving.IsDiscarded, Is.True, "...for good");
                Assert.That(DrawnButNotInTheTree(root, cache), Is.Empty, "nothing is drawn that the tree no longer has");
            });
        }
        finally
        {
            LoopSignal.Unclaim();
            SynchronizationContext.SetSynchronizationContext(previousContext);
        }
    }
}
