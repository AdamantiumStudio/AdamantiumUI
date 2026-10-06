using System.Collections.Generic;
using Adamantium.Graphics;
using Adamantium.Graphics.Core;
using Adamantium.Mathematics;
using Adamantium.UI.Rendering;
using Adamantium.Vulkan.Core;
using NUnit.Framework;

namespace Adamantium.UITests.Rendering;

/// <summary>State a collector keeps per segment - a text's font sheet, a texture - follows every segment it has: one
/// recorded by a walk, one made by a patch for a control that starts drawing, and both halves of a split. A segment
/// made by a patch had none: the next frame that drew it threw, and every frame after.</summary>
[TestFixture]
[Category("Gpu")]
public class SegmentStateTests
{
    private static readonly Rect2D Scissor = new()
    {
        Offset = new Offset2D { X = 0, Y = 0 },
        Extent = new Extent2D { Width = 64, Height = 64 }
    };

    private sealed class Tagged : BatchCollector<Vector4F>
    {
        private readonly List<int> _tags = [];

        public Tagged() : base(64)
        {
        }

        public int Tag { get; set; }

        public int Bound { get; private set; } = -1;

        public void Put(int count)
        {
            EnsureCpuCapacity(Count + count);
            for (var i = 0; i < count; i++)
            {
                Items[Count++] = default;
            }
        }

        protected override void OnSegmentRecorded(int index)
        {
            while (_tags.Count <= index)
            {
                _tags.Add(0);
            }

            _tags[index] = Tag;
        }

        protected override void OnSegmentInserted(int index) => _tags.Insert(index, _tags[index - 1]);

        protected override void BindSegment(int index) => Bound = _tags[index];

        protected override void DrawSegment(IGraphicsDevice device, Buffer<Vector4F> buffer, uint count, uint firstInstance,
            Matrix4x4F projection)
        {
        }
    }

    [Test]
    public void ASegmentMadeByAPatch_CarriesTheStateItWasMadeWith()
    {
        var device = GpuTestDevice.Device;
        var collector = new Tagged();
        try
        {
            collector.BeginFrame(device);
            collector.Tag = 1;
            collector.Put(4);
            var walked = collector.Flush(device, Scissor, Matrix4x4F.Identity);

            collector.Tag = 2;
            var made = collector.AllocateSegment(device, new Vector4F[3], Scissor);
            collector.DrawRecordedSegment(device, made, Scissor, Matrix4x4F.Identity);
            var boundForMade = collector.Bound;
            collector.DrawRecordedSegment(device, walked, Scissor, Matrix4x4F.Identity);

            Assert.Multiple(() =>
            {
                Assert.That(boundForMade, Is.EqualTo(2));
                Assert.That(collector.Bound, Is.EqualTo(1), "the walked segment keeps its own");
            });
        }
        finally
        {
            collector.DisposeGpuResources(device);
        }
    }
}
