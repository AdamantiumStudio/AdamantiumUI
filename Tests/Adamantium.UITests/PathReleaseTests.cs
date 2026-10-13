using System;
using System.Runtime.CompilerServices;
using Adamantium.Mathematics;
using Adamantium.UI.Controls.Shapes;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Media;
using NUnit.Framework;

namespace Adamantium.UITests;

/// <summary>A geometry often outlives the paths drawing it - an icon a view model hands out to every menu that shows the
/// command. A path followed its geometry's changes until it was given another one, which a discarded path never is, so
/// the icon kept every path that had ever drawn it, and each of their pages.</summary>
[TestFixture]
public class PathReleaseTests
{
    [Test]
    public void ADiscardedPath_IsNotKeptByTheGeometryItDrew()
    {
        var icon = new RectangleGeometry { Rect = new Rect(0, 0, 10, 10) };

        var path = DrawnAndThrownAway(icon);
        RenderDirty.Clear();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        Assert.That(path.IsAlive, Is.False);
        GC.KeepAlive(icon);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference DrawnAndThrownAway(Geometry icon)
    {
        var path = new Path { Data = icon };
        DiscardedVisuals.PublishSubtree(path);
        while (DiscardedVisuals.Drain(64) > 0) { }
        return new WeakReference(path);
    }
}
