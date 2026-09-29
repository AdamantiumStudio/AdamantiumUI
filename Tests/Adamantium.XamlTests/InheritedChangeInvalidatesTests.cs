using Adamantium.Core.DependencyInjection;
using Adamantium.Mathematics;
using Adamantium.UI.Controls.Decorators;
using Adamantium.UI.Controls.Text;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Media;
using NUnit.Framework;

namespace Adamantium.XamlTests;

// An element stepped over by an inherited change is still invalidated (AffectsRender and family), not only staled.
[TestFixture]
public class InheritedChangeInvalidatesTests
{
    [OneTimeSetUp]
    public void EnsureAppContext() =>
        UIAppContext.Initialize(new FakeApp(new AdamantiumDependencyContainer()), null);

    [Test]
    public void AnInheritedForegroundChange_AsksTheTextToRepaint()
    {
        var text = new TextBlock { Text = "Shapes" };
        var parent = new Border { Child = text };
        parent.Foreground = new SolidColorBrush(Color.FromRgba(220, 40, 40, 255));

        // The signal the render side actually listens to, rather than a flag only a completed Render can clear.
        var toldToRedraw = false;
        void OnInvalidated(IUIComponent c) { if (ReferenceEquals(c, text)) toldToRedraw = true; }
        VisualTreeNotifications.ContentInvalidated += OnInvalidated;
        try
        {
            parent.Foreground = new SolidColorBrush(Color.FromRgba(40, 200, 90, 255));
        }
        finally
        {
            VisualTreeNotifications.ContentInvalidated -= OnInvalidated;
        }

        Assert.That(toldToRedraw, Is.True,
            "the text took the new color but was never asked to redraw - it keeps painting the old one until " +
            "something unrelated dirties it, which is why this looked intermittent");
    }
}
