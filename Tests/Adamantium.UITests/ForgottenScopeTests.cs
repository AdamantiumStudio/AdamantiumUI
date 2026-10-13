using System.Collections.Generic;
using Adamantium.UI.Controls.Decorators;
using Adamantium.UI.Core;
using NUnit.Framework;

namespace Adamantium.UITests;

/// <summary>A scope whose surface is gone keeps no marks. An element that still names it - a context menu a style shares
/// among windows, last shown in one that closed - kept everything marked in it alive, and through that the closed window.
/// </summary>
[TestFixture]
public class ForgottenScopeTests
{
    [Test]
    public void AForgottenScope_KeepsNoMarks()
    {
        var scope = RenderDirtyRouter.NewScope();
        scope.MarkGeometry(new Border());
        scope.MarkPaint(new Border());

        RenderDirtyRouter.Forget(scope);
        scope.MarkGeometry(new Border());   // ...nor takes new ones: marks keep arriving while its window is torn down
        scope.MarkPaint(new Border());

        var geometry = new List<IUIComponent>();
        var paint = new List<IUIComponent>();
        scope.SnapshotGeometryInto(geometry);
        scope.SnapshotPaintInto(paint);
        Assert.Multiple(() =>
        {
            Assert.That(geometry, Is.Empty, "geometry marks");
            Assert.That(paint, Is.Empty, "paint marks");
        });
    }
}
