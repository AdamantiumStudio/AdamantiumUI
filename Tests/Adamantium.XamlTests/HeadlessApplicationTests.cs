using Adamantium.UI.Automation;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Decorators;
using Adamantium.UI.Core;
using Adamantium.UI.Themes.FluentTheme;
using NUnit.Framework;

namespace Adamantium.XamlTests;

/// <summary>A window closed in a headless application is discarded as in a running one. The discard asks the
/// application for its region manager, and without one it threw, so a closed window stayed in its brushes' trees for
/// the rest of the test run.</summary>
[TestFixture]
public class HeadlessApplicationTests
{
    [Test]
    public void AClosedWindow_IsDiscardedWithItsContent()
    {
        HeadlessApplication.Start<Fluent>();
        var content = new Border();
        var window = new Window { Width = 200, Height = 100, Content = content };
        Adamantium.UI.Extensions.WindowExtension.UpdateTree(window);

        window.Close();
        LoopSignal.Drain();
        while (DiscardedVisuals.Drain(64) > 0) { }

        Assert.Multiple(() =>
        {
            Assert.That(window.IsDiscarded, Is.True, "the window");
            Assert.That(content.IsDiscarded, Is.True, "its content");
        });
    }
}
