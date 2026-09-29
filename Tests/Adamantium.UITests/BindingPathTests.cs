using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Adamantium.Mathematics;
using Adamantium.UI.Controls.Base;
using Adamantium.UI.Controls.Decorators;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Data;
using Adamantium.UI.Core.Diagnostics;
using Adamantium.UI.Extensions;
using NUnit.Framework;

namespace Adamantium.UITests;

/// <summary>Every kind of binding against every kind of link: a dotted path is live along its whole length, whether a link
/// is a plain notifying object, a component of this engine or a struct, and a TwoWay write lands in the leaf the path
/// points at NOW.</summary>
[TestFixture]
public class BindingPathTests
{
    public enum Kind
    {
        Binding,
        Ancestor,
        Self
    }

    public enum Link
    {
        Notifying,
        Component
    }

    private interface INode
    {
        object Inner { get; set; }

        double Value { get; set; }

        Vector2 Offset { get; set; }
    }

    private sealed class NotifyingNode : INode, INotifyPropertyChanged
    {
        private object _inner;
        private double _value;
        private Vector2 _offset;

        public event PropertyChangedEventHandler PropertyChanged;

        public object Inner
        {
            get => _inner;
            set
            {
                _inner = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Inner)));
            }
        }

        public double Value
        {
            get => _value;
            set
            {
                _value = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Value)));
            }
        }

        public Vector2 Offset
        {
            get => _offset;
            set
            {
                _offset = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Offset)));
            }
        }
    }

    private sealed class ComponentNode : AdamantiumComponent, INode
    {
        public static readonly AdamantiumProperty InnerProperty = AdamantiumProperty.Register(nameof(Inner),
            typeof(object), typeof(ComponentNode), new PropertyMetadata(null));

        public static readonly AdamantiumProperty ValueProperty = AdamantiumProperty.Register(nameof(Value),
            typeof(double), typeof(ComponentNode), new PropertyMetadata(0d));

        public static readonly AdamantiumProperty OffsetProperty = AdamantiumProperty.Register(nameof(Offset),
            typeof(Vector2), typeof(ComponentNode), new PropertyMetadata(Vector2.Zero));

        public object Inner
        {
            get => GetValue(InnerProperty);
            set => SetValue(InnerProperty, value);
        }

        public double Value
        {
            get => GetValue<double>(ValueProperty);
            set => SetValue(ValueProperty, value);
        }

        public Vector2 Offset
        {
            get => GetValue<Vector2>(OffsetProperty);
            set => SetValue(OffsetProperty, value);
        }
    }

    private sealed class Holder : Border
    {
        public static readonly AdamantiumProperty ItemProperty = AdamantiumProperty.Register(nameof(Item),
            typeof(object), typeof(Holder), new PropertyMetadata(null));

        public object Item
        {
            get => GetValue(ItemProperty);
            set => SetValue(ItemProperty, value);
        }
    }

    private static INode Node(Link link, double value = 0, INode inner = null)
    {
        INode node = link == Link.Notifying ? new NotifyingNode() : new ComponentNode();
        node.Value = value;
        node.Inner = inner;
        return node;
    }

    // The path always starts at the holder's Item: {Binding} names it as its source, {Ancestor} finds it above the
    // target, {Self} is it.
    private static MeasurableUIComponent Bound(Kind kind, Holder holder, string path, BindingMode mode = BindingMode.OneWay)
    {
        MeasurableUIComponent target;
        switch (kind)
        {
            case Kind.Binding:
                target = new Border();
                target.SetBinding("Width", new Binding(path) { Source = holder, Mode = mode });
                break;
            case Kind.Ancestor:
                target = new Border();
                holder.AddLogicalChild(target);
                new Ancestor { AncestorType = typeof(Holder), Path = path, Logical = true, Mode = mode }.Apply(target, "Width");
                break;
            default:
                target = holder;
                new Self { Path = path, Mode = mode }.Apply(holder, "Width");
                break;
        }

        BindingUpdateQueue.Flush();
        return target;
    }

    [Test]
    public void AChangedLeaf_Follows([Values] Kind kind, [Values] Link link)
    {
        var leaf = Node(link, 10);
        var holder = new Holder { Item = Node(link, inner: leaf) };
        var target = Bound(kind, holder, "Item.Inner.Value");
        Assert.That(target.Width, Is.EqualTo(10));

        leaf.Value = 20;
        BindingUpdateQueue.Flush();

        Assert.That(target.Width, Is.EqualTo(20));
    }

    [Test]
    public void AReplacedMiddle_MovesTheBindingToTheNewLeaf([Values] Kind kind, [Values] Link link)
    {
        var old = Node(link, 10);
        var middle = Node(link, inner: old);
        var target = Bound(kind, new Holder { Item = middle }, "Item.Inner.Value");

        middle.Inner = Node(link, 30);
        BindingUpdateQueue.Flush();
        Assert.That(target.Width, Is.EqualTo(30), "the replaced middle was not noticed");

        old.Value = 99;
        BindingUpdateQueue.Flush();
        Assert.That(target.Width, Is.EqualTo(30), "the old leaf is still listened to");
    }

    [Test]
    public void AReplacedFirstLink_MovesTheWholePath([Values] Kind kind, [Values] Link link)
    {
        var oldMiddle = Node(link, inner: Node(link, 10));
        var holder = new Holder { Item = oldMiddle };
        var target = Bound(kind, holder, "Item.Inner.Value");

        holder.Item = Node(link, inner: Node(link, 40));
        BindingUpdateQueue.Flush();
        Assert.That(target.Width, Is.EqualTo(40), "the replaced first link was not noticed");

        oldMiddle.Inner = Node(link, 77);
        BindingUpdateQueue.Flush();
        Assert.That(target.Width, Is.EqualTo(40), "the old middle is still listened to");
    }

    [Test]
    public void AMiddleThatArrivesLater_IsPickedUp([Values] Kind kind, [Values] Link link)
    {
        var middle = Node(link);
        var target = Bound(kind, new Holder { Item = middle }, "Item.Inner.Value");

        middle.Inner = Node(link, 50);
        BindingUpdateQueue.Flush();

        Assert.That(target.Width, Is.EqualTo(50));
    }

    [Test]
    public void ATwoWayWrite_LandsInTheLeafThePathPointsAtNow([Values] Kind kind, [Values] Link link)
    {
        var old = Node(link, 10);
        var middle = Node(link, inner: old);
        var target = Bound(kind, new Holder { Item = middle }, "Item.Inner.Value", BindingMode.TwoWay);
        var current = Node(link, 30);
        middle.Inner = current;
        BindingUpdateQueue.Flush();

        target.Width = 60;
        BindingUpdateQueue.Flush();

        Assert.Multiple(() =>
        {
            Assert.That(current.Value, Is.EqualTo(60), "the write went nowhere");
            Assert.That(old.Value, Is.EqualTo(10), "the write went into the old leaf");
        });
    }

    // A name the object does not have is a broken path, not an empty one: it says where it broke and in what.
    [Test]
    public void AMisspelledLink_IsReported([Values] Kind kind, [Values] Link link,
        [Values("Item.Innr.Value", "Item.Inner.Valu", "Item.Offset.Z")] string path)
    {
        var messages = new List<string>();
        BindingTrace.Sink = messages.Add;
        try
        {
            var holder = new Holder { Item = Node(link, inner: Node(link, 10)) };
            var target = Bound(kind, holder, path);
            var broken = Array.Find(path.Split('.'), segment => segment is "Innr" or "Valu" or "Z");

            Assert.Multiple(() =>
            {
                Assert.That(messages, Has.Some.Contains($"'{broken}'"), "nothing said where the path broke");
                Assert.That(Expression(target).Status, Is.EqualTo(BindingStatus.PathError));
            });
        }
        finally
        {
            BindingTrace.Sink = null;
        }
    }

    // ...while a link that is simply empty for now is no error at all.
    [Test]
    public void ANullMiddle_IsNotReported([Values] Kind kind, [Values] Link link)
    {
        var messages = new List<string>();
        BindingTrace.Sink = messages.Add;
        try
        {
            var target = Bound(kind, new Holder { Item = Node(link) }, "Item.Inner.Value");

            Assert.Multiple(() =>
            {
                Assert.That(messages, Is.Empty);
                Assert.That(Expression(target).Status, Is.Not.EqualTo(BindingStatus.PathError));
            });
        }
        finally
        {
            BindingTrace.Sink = null;
        }
    }

    private static BindingExpressionBase Expression(MeasurableUIComponent target)
    {
        return BindingEngine.GetBindingExpression(target, MeasurableUIComponent.WidthProperty);
    }

    // Every link subscribed to is unsubscribed from: a source that outlives a closed binding does not keep its target.
    [Test]
    public void AClosedBinding_LetsItsTargetGo([Values] Kind kind, [Values] Link link)
    {
        var holder = new Holder { Item = Node(link, inner: Node(link, 10)) };
        var target = BoundThenClosed(kind, holder);

        Collect();

        Assert.That(target.IsAlive, Is.False, "a link still holds the closed binding and its target");
        GC.KeepAlive(holder);
    }

    // A target taken out from under its ancestor lets the ancestor go, even while the target itself lives on.
    [Test]
    public void AnAncestorLeftBehind_IsNotKeptByTheTarget([Values] Link link)
    {
        var (ancestor, target) = MovedAway(link);

        Collect();

        Assert.That(ancestor.IsAlive, Is.False, "the target still holds the ancestor it was taken from");
        GC.KeepAlive(target);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference BoundThenClosed(Kind kind, Holder holder)
    {
        var target = Bound(kind, kind == Kind.Self ? new Holder { Item = holder.Item } : holder, "Item.Inner.Value");
        BindingEngine.ClearBindings(target);
        if (kind == Kind.Ancestor)
        {
            holder.RemoveLogicalChild(target);
        }

        return new WeakReference(target);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (WeakReference Ancestor, Border Target) MovedAway(Link link)
    {
        var ancestor = new Holder { Item = Node(link, inner: Node(link, 10)) };
        var target = (Border)Bound(Kind.Ancestor, ancestor, "Item.Inner.Value");
        ancestor.RemoveLogicalChild(target);
        return (new WeakReference(ancestor), target);
    }

    // What a frame does first: a layout pass takes in the elements invalidated outside one, and the render marks are
    // consumed. Until then both hold what changed, whatever the bindings do.
    private static void Collect()
    {
        BindingUpdateQueue.Flush();
        WindowExtension.UpdateTree(new Border());
        foreach (var scope in RenderDirtyRouter.All())
        {
            scope.Clear();
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    [Test]
    public void AStructInThePath_IsReadAndWrittenThrough([Values] Kind kind, [Values] Link link)
    {
        var first = Node(link);
        first.Offset = new Vector2(5, 0);
        var holder = new Holder { Item = first };
        var target = Bound(kind, holder, "Item.Offset.X", BindingMode.TwoWay);
        Assert.That(target.Width, Is.EqualTo(5));

        first.Offset = new Vector2(6, 0);
        BindingUpdateQueue.Flush();
        Assert.That(target.Width, Is.EqualTo(6), "a new struct on the same link was not noticed");

        var next = Node(link);
        next.Offset = new Vector2(7, 0);
        holder.Item = next;
        BindingUpdateQueue.Flush();
        Assert.That(target.Width, Is.EqualTo(7), "the replaced link before the struct was not noticed");

        target.Width = 8;
        BindingUpdateQueue.Flush();
        Assert.Multiple(() =>
        {
            Assert.That(next.Offset.X, Is.EqualTo(8), "the write did not reach the struct");
            Assert.That(first.Offset.X, Is.EqualTo(6), "the write went into the old link");
        });
    }
}
