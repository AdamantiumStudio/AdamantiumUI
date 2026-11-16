using System;
using System.Collections.Generic;
using Adamantium.Graphics.Core;
using Adamantium.UI.Rendering;
using NUnit.Framework;

namespace Adamantium.UITests.Rendering;

/// <summary>A ring of GPU resources is made whole or not at all: the GPU out of memory halfway, what was made is let go
/// of and the refusal reaches the caller, which keeps what it had.</summary>
[TestFixture]
public class GpuRingTests
{
    private sealed class Resource : IDisposable
    {
        public bool Disposed { get; private set; }

        public void Dispose() => Disposed = true;
    }

    [Test]
    public void EveryResourceMade_IsTheRing_InItsPlace()
    {
        var places = new List<int>();

        var ring = GpuRing.Make(3, place =>
        {
            places.Add(place);
            return new Resource();
        });

        Assert.Multiple(() =>
        {
            Assert.That(ring, Has.Length.EqualTo(3).And.All.Not.Null);
            Assert.That(places, Is.EqualTo(new[] { 0, 1, 2 }));
            Assert.That(ring, Has.All.Matches<Resource>(resource => !resource.Disposed));
        });
    }

    [Test]
    public void TheGpuOutOfMemoryHalfway_LetsGoOfWhatWasMade_AndSaysSo()
    {
        var made = new List<Resource>();

        Assert.Throws<OutOfDeviceMemoryException>(() => GpuRing.Make(3, place =>
        {
            if (place == 2)
            {
                throw new OutOfDeviceMemoryException("refused");
            }

            var resource = new Resource();
            made.Add(resource);
            return resource;
        }));

        Assert.That(made, Has.Count.EqualTo(2).And.All.Matches<Resource>(resource => resource.Disposed));
    }
}
