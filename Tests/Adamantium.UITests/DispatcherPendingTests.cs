using System;
using Adamantium.UI.Core.Dispatcher;
using Adamantium.UI.Threading;
using NUnit.Framework;

namespace Adamantium.UITests;

/// <summary>Work invoked on the dispatcher is pending until it has run - also when it throws - so waiting for the
/// application to go idle waits for it too. A window closed that way, or a drop finished that way, was still to come when
/// automation was told the application had settled.</summary>
[TestFixture]
public class DispatcherPendingTests
{
    [Test]
    public void AnInvokedOperation_IsPendingUntilItHasRun_EvenWhenItThrows()
    {
        var executor = new DispatcherOperationExecutor(null);
        var before = executor.HasPending;

        _ = executor.InvokeAsync(() => { }, DispatcherPriority.Normal);
        _ = executor.InvokeAsync(() => throw new InvalidOperationException("fails"), DispatcherPriority.Normal);
        var queued = executor.HasPending;
        executor.Execute();

        Assert.Multiple(() =>
        {
            Assert.That(before, Is.False);
            Assert.That(queued, Is.True);
            Assert.That(executor.HasPending, Is.False);
        });
    }
}
