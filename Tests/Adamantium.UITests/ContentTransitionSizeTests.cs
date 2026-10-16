using System.Diagnostics;
using System.Threading;
using Adamantium.Mathematics;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Decorators;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Media.Animation;
using NUnit.Framework;

namespace Adamantium.UITests;

// While a page slides out its presenter is as large as both pages; once it has gone, the presenter is as large as the
// page that stayed.
[TestFixture]
public class ContentTransitionSizeTests
{
    private static readonly Size Host = new(400, 2000);

    private static void Frame(ContentPresenter presenter, double tick)
    {
        LoopSignal.Drain();
        presenter.Measure(Host);
        presenter.Arrange(new Rect(0, 0, Host.Width, Host.Height));
        AnimationManager.Tick(tick);
        Compositor.StartPending();
    }

    [Test]
    public void AfterAPageSlidOut_ThePresenterIsTheSizeOfThePageThatStayed()
    {
        var presenter = new ContentPresenter { ContentTransition = ContentTransition.SlideLeft, TransitionDuration = 0.2 };
        var window = new Window { Width = 400, Height = 300, Content = presenter };
        ((IMeasurableComponent)window).Measure(Host);
        presenter.Content = new Border { Width = 100, Height = 900 };
        Frame(presenter, 0);

        presenter.Content = new Border { Width = 100, Height = 50 };
        Frame(presenter, 0);
        var duringSlide = presenter.DesiredSize.Height;
        var clock = Stopwatch.StartNew();
        while (presenter.VisualChildren.Count > 1 && clock.Elapsed.TotalSeconds < 5)
        {
            Thread.Sleep(10);
            Frame(presenter, 0.05);
        }

        presenter.Measure(Host);

        Assert.Multiple(() =>
        {
            Assert.That(presenter.VisualChildren.Count, Is.EqualTo(1), "the slide is over");
            Assert.That(duringSlide, Is.EqualTo(900).Within(0.5), "both pages are there while it slides");
            Assert.That(presenter.DesiredSize.Height, Is.EqualTo(50).Within(0.5), "the page that went is not measured");
        });
    }
}
