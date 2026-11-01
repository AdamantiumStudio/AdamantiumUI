using System;
using System.Collections.Generic;
using System.Reflection;
using Adamantium.UI.Controls.Base;
using Adamantium.UI.Controls.Decorators;
using Adamantium.UI.Controls.Text;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Media;
using NUnit.Framework;

namespace Adamantium.UITests;

/// <summary>
/// An element holds the brush it draws with, once, whatever path the brush came by and whatever it was told on the way;
/// it hears an inherited change from the value it showed; and an element below throwing from its notification does not
/// undo the write above it.
/// </summary>
[TestFixture]
public class InheritedChangeKeepsBrushHoldsTests
{
    private readonly SolidColorBrush _red = new("#FF0000");
    private readonly SolidColorBrush _blue = new("#0000FF");
    private readonly SolidColorBrush _green = new("#00FF00");

    private static int Holds(Brush brush, AdamantiumComponent owner)
    {
        var owners = (Dictionary<AdamantiumComponent, int>)typeof(Brush)
            .GetField("_owners", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(brush);
        return owners != null && owners.TryGetValue(owner, out var held) ? held : 0;
    }

    [Test]
    public void AnElementWatchedAfterAChangePassedItOver_HoldsOnlyTheBrushItDrawsWith()
    {
        var grand = new Border();
        var leaf = new Border();
        grand.Child = leaf;
        grand.Foreground = _red;
        Assert.That(leaf.Foreground, Is.SameAs(_red));
        grand.Foreground = _blue;
        var heard = new List<string>();
        leaf.PropertyChanged += (_, e) =>
        {
            if (e.Property == UIComponent.ForegroundProperty) heard.Add($"{Name(e.OldValue)}->{Name(e.NewValue)}");
        };

        grand.Foreground = _red;
        grand.Foreground = _green;

        Assert.Multiple(() =>
        {
            Assert.That(heard, Is.EqualTo(new[] { "Red->Green" }), "it heard the change from what it showed");
            Assert.That(Holds(_red, leaf), Is.EqualTo(0), "red");
            Assert.That(Holds(_green, leaf), Is.EqualTo(1), "green");
        });
    }

    [TestCase(false, TestName = "AMovedTextBlock_HoldsOnlyTheBrushItDrawsWith_WhenItsNewParentChanges")]
    [TestCase(true, TestName = "AMovedTextBlock_HoldsOnlyTheBrushItDrawsWith_WhenItsNewParentChangesThroughTheOld")]
    public void AMovedTextBlock_HoldsOnlyTheBrushItDrawsWith(bool throughTheOld)
    {
        var first = new Border { Foreground = _red };
        var second = new Border { Foreground = _blue };
        var text = new TextBlock();
        first.Child = text;
        Assert.That(text.Foreground, Is.SameAs(_red));
        first.Child = null;
        second.Child = text;

        if (throughTheOld) second.Foreground = _red;
        second.Foreground = _green;

        Assert.Multiple(() =>
        {
            Assert.That(text.Foreground, Is.SameAs(_green));
            Assert.That(Holds(_red, text), Is.EqualTo(0), "red");
            Assert.That(Holds(_blue, text), Is.EqualTo(0), "blue");
            Assert.That(Holds(_green, text), Is.EqualTo(1), "green");
        });
    }

    [Test]
    public void AMovedTextBlock_StartsItsTransitionFromWhatItShowed()
    {
        var first = new Border { Foreground = _red };
        var second = new Border { Foreground = _blue };
        var text = new RecordingTextBlock();
        first.Child = text;
        Assert.That(text.Foreground, Is.SameAs(_red));
        first.Child = null;
        second.Child = text;
        text.From.Clear();

        second.Foreground = _green;

        Assert.That(text.From, Is.EqualTo(new object[] { _red }));
    }

    [Test]
    public void AnElementBelowThrowing_DoesNotUndoTheWriteAbove()
    {
        var root = new Border();
        var child = new Border();
        root.Child = child;
        child.DataContextChanged += (_, _) => throw new InvalidOperationException("thrown below");

        Assert.Multiple(() =>
        {
            Assert.DoesNotThrow(() => root.DataContext = "context");
            Assert.That(root.DataContext, Is.EqualTo("context"));
        });
    }

    private string Name(object brush) =>
        ReferenceEquals(brush, _red) ? "Red" : ReferenceEquals(brush, _blue) ? "Blue"
        : ReferenceEquals(brush, _green) ? "Green" : brush?.ToString() ?? "null";

    private sealed class RecordingTextBlock : TextBlock
    {
        public List<object> From { get; } = [];

        protected override bool OnValueSet(AdamantiumProperty property, object oldEffectiveValue, object newValue,
            ValuePriority priority)
        {
            if (property == ForegroundProperty) From.Add(oldEffectiveValue);
            return base.OnValueSet(property, oldEffectiveValue, newValue, priority);
        }
    }
}
