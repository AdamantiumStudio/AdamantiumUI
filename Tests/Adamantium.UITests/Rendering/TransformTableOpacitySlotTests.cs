using System;
using Adamantium.UI.Rendering;
using NUnit.Framework;

namespace Adamantium.UITests.Rendering;

// A frame brings the table's GPU copy up to date before it is drawn; a slot handed out after that is drawn this frame
// too, so it has to reach that copy then and there.
[TestFixture]
[Category("Gpu")]
public class TransformTableOpacitySlotTests
{
    [Test]
    public void AnOpacitySlotHandedOutMidFrame_IsOpaqueInTheCopyThisFrameDraws()
    {
        var device = GpuTestDevice.Device;
        var table = new TransformTable();
        table.EnsureResources(device);

        var slot = table.AcquireOpacitySlot(Guid.NewGuid());

        Assert.That(table.DrawnAlphaAt(slot), Is.EqualTo(1f));
    }
}
