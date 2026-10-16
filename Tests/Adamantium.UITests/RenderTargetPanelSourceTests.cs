using Adamantium.Graphics.Core;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Decorators;
using Adamantium.UI.Controls.Panels;
using Adamantium.UI.Extensions;
using NUnit.Framework;

namespace Adamantium.UITests;

/// <summary>A panel that leaves the tree keeps the surface it shows. A tab switched away and back showed an empty panel:
/// the producer hands a surface over once per size, and the panel had forgotten it.</summary>
[TestFixture]
public class RenderTargetPanelSourceTests
{
    [Test]
    public void APanelBackInTheTree_ShowsTheSurfaceItShowedBefore()
    {
        var descriptor = new SharedSurfaceDescriptor { Width = 4, Height = 4 };
        var panel = new RenderTargetPanel();
        var host = new Border { Child = panel };
        var window = new Window { Width = 200, Height = 100, Content = host };
        WindowExtension.UpdateTree(window);
        panel.SetSource(descriptor);

        host.Child = null;
        host.Child = panel;

        Assert.That(panel.Source, Is.SameAs(descriptor));
    }

    // An output going away unbinds its own surface, not the one the output that replaced it has bound since.
    [Test]
    public void ClearingASurfaceNoLongerBound_KeepsTheOneBoundSince()
    {
        var gone = new SharedSurfaceDescriptor { Width = 4, Height = 4 };
        var bound = new SharedSurfaceDescriptor { Width = 8, Height = 8 };
        var panel = new RenderTargetPanel();
        panel.SetSource(gone);
        panel.SetSource(bound);

        panel.ClearSource(gone);
        Assert.That(panel.Source, Is.SameAs(bound));

        panel.ClearSource(bound);
        Assert.That(panel.Source, Is.Null);
    }
}
