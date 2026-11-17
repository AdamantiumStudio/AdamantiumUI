using System;
using System.Linq;
using System.Threading.Tasks;
using Adamantium.UI.Universes;
using NUnit.Framework;

namespace Adamantium.UITests;

/// <summary>The universes an application keeps: every one is shut down on the application's exit.</summary>
public class UniverseServiceTests
{
    [Test]
    public async Task OnExit_EveryUniverse_LiveOrRemoved_IsShutDown()
    {
        var service = new UniverseService();
        var live = await service.CreateUniverseAsync("live", () => RecordingUniverse.Make(out _));
        var removed = await service.CreateUniverseAsync("removed", () => RecordingUniverse.Make(out _));
        service.RemoveUniverse(removed);

        var stopped = service.ShutDownAll();

        Assert.Multiple(() =>
        {
            Assert.That(stopped, Is.True);
            Assert.That(Calls(live).Count(call => call == "ShutDown"), Is.EqualTo(1));
            Assert.That(Calls(removed).Count(call => call == "ShutDown"), Is.EqualTo(1));
            Assert.That(service.Universes, Is.Empty);
        });

        service.RetireRemoved();
        service.ShutDownAll();
        Assert.That(Calls(removed).Count(call => call == "ShutDown"), Is.EqualTo(1), "shut down once");
    }

    [Test]
    public void AUniverseMadeAfterTheExit_IsShutDownAsItComes()
    {
        var service = new UniverseService();
        service.ShutDownAll();
        RecordingUniverse recorder = null;

        Assert.ThrowsAsync<OperationCanceledException>(() =>
            service.CreateUniverseAsync("late", () => RecordingUniverse.Make(out recorder)));
        Assert.That(recorder.Calls, Does.Contain("ShutDown"));
        Assert.That(service.Universes, Is.Empty);
    }

    [Test]
    public async Task AUniverseStuckWhenRemoved_IsStillHeldForTheDevices_AndOnExitSaysNotAllStopped()
    {
        var service = new UniverseService();
        var stuck = await service.CreateUniverseAsync("stuck", () => RecordingUniverse.Make(out _));
        var recorder = (RecordingUniverse)(object)stuck;
        recorder.IsStuck = true;
        service.RemoveUniverse(stuck);
        service.RetireRemoved();

        service.SuspendLoops();
        service.ResumeLoops();
        var stopped = service.ShutDownAll();

        Assert.Multiple(() =>
        {
            Assert.That(recorder.Calls, Does.Contain("SuspendHostedLoop"), "a stopping universe went unheld for the devices");
            Assert.That(recorder.Calls.Count(call => call == "ShutDown"), Is.EqualTo(2), "the exit asked it again");
            Assert.That(stopped, Is.False, "a universe still drawing was said stopped");
        });
    }

    private static System.Collections.Generic.List<string> Calls(object universe) =>
        ((RecordingUniverse)universe).Calls;
}
