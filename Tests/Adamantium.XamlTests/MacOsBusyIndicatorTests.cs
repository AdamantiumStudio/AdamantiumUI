using System.Linq;
using System.Threading.Tasks;
using Adamantium.UI.Automation;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Base;
using Adamantium.UI.Core;
using Adamantium.UI.Themes.MacOsTheme;
using NUnit.Framework;

namespace Adamantium.XamlTests;

/// <summary>The macOS spinner's wheel is drawn on a field of its own and scaled to the size the indicator is given.</summary>
[TestFixture]
public class MacOsBusyIndicatorTests
{
    [SetUp]
    public void Fresh() => HeadlessApplication.Start<MacOs>();

    // A tab gives it 16: every spoke stays inside, the spokes point every way, and the wheel is in the middle - of a
    // slot that is not square too.
    [TestCase(16, 16)]
    [TestCase(48, 48)]
    [TestCase(16, 40)]
    public async Task TheWheel_FitsTheSizeGiven(double width, double height)
    {
        var indicator = new BusyIndicator { Width = width, Height = height };
        await using var session = AutomationSession.InProcess(
            new Window { Width = 200, Height = 200, ClientWidth = 200, ClientHeight = 200, Content = indicator });
        await session.WaitForIdleAsync();

        var spokes = Enumerable.Range(1, 8)
            .Select(spoke => ((IUIComponent)indicator.GetTemplateChild($"Spoke{spoke}")).TransformBoundsToVisual(indicator))
            .ToArray();
        var middleX = spokes.Average(bounds => bounds.X + bounds.Width / 2);
        var middleY = spokes.Average(bounds => bounds.Y + bounds.Height / 2);

        Assert.Multiple(() =>
        {
            foreach (var bounds in spokes)
            {
                Assert.That(bounds.X, Is.GreaterThanOrEqualTo(-0.01), "left");
                Assert.That(bounds.Y, Is.GreaterThanOrEqualTo(-0.01), "top");
                Assert.That(bounds.X + bounds.Width, Is.LessThanOrEqualTo(width + 0.01), "right");
                Assert.That(bounds.Y + bounds.Height, Is.LessThanOrEqualTo(height + 0.01), "bottom");
            }

            Assert.That(middleX, Is.EqualTo(width / 2).Within(0.05), "the wheel is in the middle across");
            Assert.That(middleY, Is.EqualTo(height / 2).Within(0.05), "and down");
            Assert.That(spokes[0].Y + spokes[0].Height / 2, Is.LessThan(height / 2), "the first spoke points up");
            Assert.That(spokes[4].Y + spokes[4].Height / 2, Is.GreaterThan(height / 2), "the fifth down");
        });
    }

    // Inside the box that scales it, the fade still runs round the wheel.
    [Test]
    public async Task TheFade_StillRunsInsideTheScaledWheel()
    {
        var indicator = new BusyIndicator { Width = 16, Height = 16 };
        await using var session = AutomationSession.InProcess(
            new Window { Width = 200, Height = 200, ClientWidth = 200, ClientHeight = 200, Content = indicator });
        await session.WaitForIdleAsync();

        indicator.IsActive = true;
        await session.WaitForIdleAsync();
        AnimationTime.Pass(0.35);

        var opacities = Enumerable.Range(1, 8)
            .Select(spoke => ((UIComponent)indicator.GetTemplateChild($"Spoke{spoke}")).Opacity)
            .ToArray();
        Assert.That(opacities.Distinct().Count(), Is.GreaterThan(1), string.Join(", ", opacities));
    }
}
