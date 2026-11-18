using System;
using System.Collections.Generic;
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

        service.ShutDownAll();

        Assert.Multiple(() =>
        {
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

    private static List<string> Calls(object universe) =>
        ((RecordingUniverse)universe).Calls;
}
