using System;
using System.IO;
using System.Threading;
using Adamantium.UI;
using NUnit.Framework;

namespace Adamantium.UITests;

/// <summary>The named places processes take - copies of an application (<see cref="UIApplication.MaxInstances"/>), the
/// editor's solutions. A place is held by a thread of its own, so a process here is a slot taken on the test's thread;
/// a process that crashed is a thread that ended holding its mutex.</summary>
[TestFixture]
public class InstanceSlotTests
{
    private string _folder;

    [SetUp]
    public void SetUp() => _folder = Path.Combine(Path.GetTempPath(), "adam-instances-" + Guid.NewGuid().ToString("N"));

    [Test]
    public void ACopyPastTheLimit_GetsNoPlace()
    {
        using var first = InstanceSlot.TryTake(_folder, 2);
        using var second = InstanceSlot.TryTake(_folder, 2);
        using var third = InstanceSlot.TryTake(_folder, 2);

        Assert.Multiple(() =>
        {
            Assert.That(first, Is.Not.Null);
            Assert.That(second, Is.Not.Null);
            Assert.That(third, Is.Null);
        });
    }

    [Test]
    public void APlaceTaken_IsSeenTaken_UntilGivenUp()
    {
        var slot = InstanceSlot.TryTake(_folder);
        Assert.That(InstanceSlot.IsTaken(_folder), Is.True);

        slot.Dispose();

        Assert.That(InstanceSlot.IsTaken(_folder), Is.False);
    }

    [Test]
    public void ACopyThatEnds_GivesItsPlaceUp()
    {
        var first = InstanceSlot.TryTake(_folder, 1);
        Assert.That(InstanceSlot.TryTake(_folder, 1), Is.Null, "precondition: the one place is taken");

        first.Dispose();
        using var next = InstanceSlot.TryTake(_folder, 1);

        Assert.That(next, Is.Not.Null);
    }

    [Test]
    public void ACopyThatCrashed_GivesItsPlaceUp()
    {
        var crashed = new Thread(() =>
        {
            var mutex = new Mutex(false, InstanceSlot.CreateName(_folder, 0), InstanceSlot.Options, out _);
            mutex.WaitOne();
        });
        crashed.Start();
        crashed.Join();

        using var next = InstanceSlot.TryTake(_folder, 1);

        Assert.That(next, Is.Not.Null);
    }

    [Test]
    public void ApplicationsOfOtherDataFolders_AreCountedApart()
    {
        using var mine = InstanceSlot.TryTake(_folder, 1);
        using var other = InstanceSlot.TryTake(_folder + "-other", 1);

        Assert.That(other, Is.Not.Null);
    }
}
