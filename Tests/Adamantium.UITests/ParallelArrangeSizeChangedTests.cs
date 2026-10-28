using System;
using System.Collections.Generic;
using System.Threading;
using Adamantium.Mathematics;
using Adamantium.UI.Controls.Base;
using Adamantium.UI.Controls.Decorators;
using Adamantium.UI.Controls.Panels;
using Adamantium.UI.Core;
using NUnit.Framework;

namespace Adamantium.UITests;

// A parallel arrange sizes its items on pool threads, but their SizeChanged is raised on the thread that runs it, after
// the items, in their order - also when one of them throws.
[TestFixture]
public class ParallelArrangeSizeChangedTests
{
    private const int Count = 64;

    [Test]
    public void SizeChangedOfItemsArrangedInParallel_IsRaisedOnTheArrangingThread_InItemOrder()
    {
        var (panel, items) = Panel();
        var heard = Listen(panel, out var foreign);
        var pooled = 0;
        var arranging = Thread.CurrentThread;

        ParallelArrange.Run(panel, Count, PositionIn(items), i =>
        {
            NoteThread(arranging, ref pooled);
            items[i].Arrange(new Rect(0, i * 10, 10 + i, 10));
        });

        Assume.That(pooled, Is.GreaterThan(0), "the pool took part");
        Assert.Multiple(() =>
        {
            Assert.That(foreign(), Is.EqualTo(0));
            Assert.That(heard, Is.EqualTo(items));
        });
    }

    [Test]
    public void SizeChangedOfAWidthSetInParallel_IsRaisedOnTheArrangingThread()
    {
        var (panel, items) = Panel();
        var heard = Listen(panel, out var foreign);
        var pooled = 0;
        foreach (var item in items)
        {
            item.Width = 5;
        }

        heard.Clear();
        var arranging = Thread.CurrentThread;
        ParallelArrange.Run(panel, Count, PositionIn(items), i =>
        {
            NoteThread(arranging, ref pooled);
            items[i].Width = 20 + i;
        });

        Assume.That(pooled, Is.GreaterThan(0), "the pool took part");
        Assert.Multiple(() =>
        {
            Assert.That(foreign(), Is.EqualTo(0));
            Assert.That(heard, Is.EqualTo(items));
        });
    }

    [Test]
    public void AnItemThatThrows_DoesNotLoseTheOthersSizeChanged()
    {
        var (panel, items) = Panel();
        var heard = Listen(panel, out var foreign);
        var arranged = new bool[Count];

        Assert.That(() => ParallelArrange.Run(panel, Count, PositionIn(items), i =>
        {
            items[i].Arrange(new Rect(0, i * 10, 10 + i, 10));
            arranged[i] = true;
            if (i == Count / 2)
            {
                throw new InvalidOperationException();
            }
        }), Throws.InstanceOf<AggregateException>());

        List<object> expected = [];
        for (var i = 0; i < Count; i++)
        {
            if (arranged[i])
            {
                expected.Add(items[i]);
            }
        }

        Assert.Multiple(() =>
        {
            Assert.That(foreign(), Is.EqualTo(0));
            Assert.That(heard, Is.EqualTo(expected), "every arranged item, in order");
        });
    }

    [Test]
    public void AHandlerThatThrows_ComesOutAsItIs_AndWithAnArrangeFaultBothComeOut()
    {
        var (panel, items) = Panel();
        var thrown = new InvalidOperationException("handler");
        panel.SizeChanged += (s, e) =>
        {
            if (e.OriginalSource == items[3])
            {
                throw thrown;
            }
        };

        Assert.That(() => ParallelArrange.Run(panel, Count, PositionIn(items),
                i => items[i].Arrange(new Rect(0, i * 10, 10 + i, 10))),
            Throws.Exception.SameAs(thrown));

        var arrangeFault = new NotSupportedException("arrange");
        var both = Assert.Throws<AggregateException>(() => ParallelArrange.Run(panel, Count, PositionIn(items), i =>
        {
            items[i].Arrange(new Rect(0, i * 10, 20 + i, 10));
            if (i == Count - 1)
            {
                throw arrangeFault;
            }
        }));

        Assert.That(both.InnerExceptions, Does.Contain(arrangeFault).And.Contain(thrown));
    }

    [Test]
    public void ANestedParallelArrange_HandsItsSizeChangedToTheOuterOne()
    {
        var (panel, items) = Panel();
        var (innerPanel, innerItems) = Panel();
        panel.Children.Add(innerPanel);
        var heard = Listen(panel, out var foreign);

        const int middle = Count / 2;
        int PositionOf(IUIComponent item)
        {
            if (item == innerPanel)
            {
                return middle;
            }

            var index = items.IndexOf(item as Border);
            return index < middle ? index : index + 1;
        }

        ParallelArrange.Run(panel, Count + 1, PositionOf, i =>
        {
            if (i == middle)
            {
                ParallelArrange.Run(innerPanel, Count, PositionIn(innerItems),
                    j => innerItems[j].Arrange(new Rect(0, j * 10, 10 + j, 10)));
                return;
            }

            items[i < middle ? i : i - 1].Arrange(new Rect(0, i * 10, 10 + i, 10));
        });

        List<object> expected = [.. items.GetRange(0, middle), .. innerItems, .. items.GetRange(middle, Count - middle)];
        Assert.Multiple(() =>
        {
            Assert.That(foreign(), Is.EqualTo(0));
            Assert.That(heard, Is.EqualTo(expected), "the inner panel's in its place among the outer items");
        });
    }

    [Test]
    public void AfterAFaultedArrange_SizeChangedIsRaisedAtOnceAgain()
    {
        var (panel, items) = Panel();
        var heard = Listen(panel, out _);

        Assert.That(() => ParallelArrange.Run(panel, Count, PositionIn(items),
            i => throw new InvalidOperationException()), Throws.InstanceOf<AggregateException>());

        items[0].Arrange(new Rect(0, 0, 70, 10));

        Assert.Multiple(() =>
        {
            Assert.That(panel.ArrangingInParallel, Is.Null);
            Assert.That(heard, Is.EqualTo(new object[] { items[0] }));
        });
    }

    [Test]
    public void AnElementWithNoPlaceInTheArrange_IsRaisedAfterTheItems()
    {
        var (panel, items) = Panel();
        var stray = new Border();
        panel.Children.Add(stray);
        stray.Measure(new Size<double>(100, 10));
        var heard = Listen(panel, out _);

        ParallelArrange.Run(panel, Count, PositionIn(items), i =>
        {
            if (i == 0)
            {
                stray.Arrange(new Rect(0, 0, 33, 10));
            }

            items[i].Arrange(new Rect(0, i * 10, 10 + i, 10));
        });

        Assert.That(heard, Is.EqualTo((List<object>)[.. items, stray]));
    }

    private static Func<IUIComponent, int> PositionIn(List<Border> items) => item => items.IndexOf(item as Border);

    private static (StackPanel Panel, List<Border> Items) Panel()
    {
        var panel = new StackPanel();
        List<Border> items = [];
        for (var i = 0; i < Count; i++)
        {
            var item = new Border();
            items.Add(item);
            panel.Children.Add(item);
            item.Measure(new Size<double>(100, 10));
        }

        return (panel, items);
    }

    private static List<object> Listen(StackPanel panel, out Func<int> foreign)
    {
        var arranging = Thread.CurrentThread;
        List<object> heard = [];
        var elsewhere = 0;
        panel.SizeChanged += (s, e) =>
        {
            if (Thread.CurrentThread != arranging)
            {
                Interlocked.Increment(ref elsewhere);
            }

            heard.Add(e.OriginalSource);
        };
        foreign = () => Volatile.Read(ref elsewhere);
        return heard;
    }

    private static void NoteThread(Thread arranging, ref int pooled)
    {
        if (Thread.CurrentThread != arranging)
        {
            Interlocked.Increment(ref pooled);
        }

        Thread.Sleep(1);
    }
}
