using System;
using Adamantium.UI.Core;
using NUnit.Framework;

namespace Adamantium.UITests;

/// <summary>A property's write check is asked before a value is written: a value it refuses leaves the property as it
/// was and nothing of itself behind; an override that states none keeps the base's.</summary>
[TestFixture]
public class WriteCheckTests
{
    private class Lockable : AdamantiumComponent
    {
        public static readonly AdamantiumProperty ValueProperty = AdamantiumProperty.Register(nameof(Value),
            typeof(object), typeof(Lockable),
            new PropertyMetadata(null) { WriteCheckCallback = RefusedWhileLocked });

        public bool Locked { get; set; }

        public object Value
        {
            get => GetValue<object>(ValueProperty);
            set => SetValue(ValueProperty, value);
        }

        private static void RefusedWhileLocked(AdamantiumComponent a, object value)
        {
            if (a is Lockable { Locked: true })
            {
                throw new InvalidOperationException("locked");
            }
        }
    }

    private class MovesTheDefault : Lockable
    {
        static MovesTheDefault() => ValueProperty.OverrideMetadata(typeof(MovesTheDefault), new PropertyMetadata("x"));
    }

    [Test]
    public void ARefusedValue_LeavesThePropertyAsItWas()
    {
        var lockable = new Lockable { Value = "first" };
        lockable.Locked = true;

        Assert.Throws<InvalidOperationException>(() => lockable.Value = "second");
        Assert.Throws<InvalidOperationException>(() => lockable.SetCurrentValue(Lockable.ValueProperty, "third"));
        Assert.Throws<InvalidOperationException>(() => lockable.ClearValue(Lockable.ValueProperty));
        Assert.That(lockable.Value, Is.EqualTo("first"));
    }

    [Test]
    public void ARefusedValue_IsNotKept()
    {
        var lockable = new Lockable { Locked = true };

        var refused = Refuse(lockable);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        Assert.That(refused.IsAlive, Is.False);
        GC.KeepAlive(lockable);

        static WeakReference Refuse(Lockable locked)
        {
            var value = new object();
            Assert.Throws<InvalidOperationException>(() => locked.Value = value);
            return new WeakReference(value);
        }
    }

    [Test]
    public void AnOverrideStatingNoCheck_KeepsTheBases()
    {
        var moved = new MovesTheDefault { Locked = true };

        Assert.Multiple(() =>
        {
            Assert.That(moved.Value, Is.EqualTo("x"));
            Assert.Throws<InvalidOperationException>(() => moved.Value = "y");
        });
    }
}
