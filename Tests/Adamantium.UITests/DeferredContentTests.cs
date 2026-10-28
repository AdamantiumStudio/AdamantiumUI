using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using Adamantium.Mathematics;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Decorators;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Media.Animation;
using Adamantium.UI.Core.Templates;
using NUnit.Framework;

namespace Adamantium.UITests;

// ContentPresenter.DeferContent: the requested content arrives, a superseded build never replaces newer content, and a
// render with no next frame builds inline.
[TestFixture]
public class DeferredContentTests
{
    private static readonly Size<double> Host = new(100, 100);

    // A view that asks to be kept - what x:KeepAlive states in markup, which generated code-behind overrides the same way.
    private sealed class KeptBorder : Border
    {
        public override NavigationCacheMode KeepAlive => NavigationCacheMode.Required;
    }

    // Drains the loop queue (what UIApplication.Update does at the top of a frame) until the presenter has adopted its
    // content or the wait runs out. Time is a ceiling for the harness, not a target.
    private static bool PumpUntil(Func<bool> done, ContentPresenter presenter, double seconds = 5, double tick = 0)
    {
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed.TotalSeconds < seconds)
        {
            LoopSignal.Drain();
            presenter.Measure(Host);
            presenter.Arrange(new Rect(0, 0, Host.Width, Host.Height));
            if (tick > 0) AnimationManager.Tick(tick);
            Compositor.StartPending();   // the frame is over
            if (done()) return true;
            Thread.Sleep(1);
        }

        return false;
    }

    private static Size<double>? SizeOfChild(ContentPresenter presenter) =>
        presenter.VisualChildren.FirstOrDefault() is IMeasurableComponent child ? child.DesiredSize : null;

    // While a swap plays there are TWO children - the one leaving and the one arriving - so "what is on screen" is a
    // question about the whole set, not about the first of them.
    private static bool HasChild(ContentPresenter presenter, Size<double> size) =>
        presenter.VisualChildren.OfType<IMeasurableComponent>().Any(c => c.DesiredSize == size);

    [SetUp]
    public void Reset() => ParkedVisuals.Clear();

    // The content asked for asynchronously still arrives, and it is the content that was asked for - not the loading
    // stand-in it replaces. The FIRST frame is allowed to show the spinner; that is the whole point, so what is pinned is
    // that it CONVERGES.
    [Test]
    public void DeferredContent_Arrives_AndReplacesTheLoadingVisual()
    {
        var content = new object();
        var presenter = new ContentPresenter
        {
            DeferContent = true,
            LoadingTemplate = new DataTemplate(() => new TemplateResult { RootComponent = new Border { Width = 8, Height = 8 } }),
            ContentTemplate = new DataTemplate(() => new TemplateResult { RootComponent = new Border { Width = 40, Height = 30 } }),
            Content = content
        };

        presenter.Measure(Host);
        Assert.That(SizeOfChild(presenter), Is.EqualTo(new Size<double>(8, 8)),
            "the LOADING visual stands in the content's place from the first measure");

        Assert.That(PumpUntil(() => SizeOfChild(presenter) == new Size<double>(40, 30), presenter), Is.True,
            "the real content has to appear once it has been built");
    }

    // A user who leaves before the content is ready must not be shown it when it lands - the tab they LEFT would take the
    // place of the one they chose. The work is not thrown away either: a view that asked to be kept waits by reference,
    // so coming back is a return rather than a rebuild.
    [Test]
    public void LeavingBeforeItLands_ParksTheFinishedContent_InsteadOfShowingIt()
    {
        var first = new object();
        var second = new object();
        var firstBuilds = 0;

        var firstTemplate = new DataTemplate(() =>
        {
            firstBuilds++;
            return new TemplateResult { RootComponent = new KeptBorder { Width = 40, Height = 30 } };
        });

        var presenter = new ContentPresenter
        {
            DeferContent = true,
            LoadingTemplate = new DataTemplate(() => new TemplateResult { RootComponent = new Border { Width = 8, Height = 8 } }),
            ContentTemplate = firstTemplate,
            Content = first
        };

        presenter.Measure(Host);

        // Left before it landed.
        presenter.ContentTemplate = new DataTemplate(() => new TemplateResult { RootComponent = new Border { Width = 12, Height = 12 } });
        presenter.Content = second;

        Assert.That(PumpUntil(() => SizeOfChild(presenter) == new Size<double>(12, 12), presenter), Is.True,
            "the content the user actually chose has to be the one that shows");

        // Give the superseded build every chance to land on top of it.
        PumpUntil(() => false, presenter, seconds: 0.3);
        Assert.That(SizeOfChild(presenter), Is.EqualTo(new Size<double>(12, 12)),
            "a build the user walked away from must never replace the content they chose");

        // ...and it was kept, not destroyed: coming back takes it out of the park instead of building it again.
        presenter.ContentTemplate = firstTemplate;
        presenter.Content = first;
        Assert.That(PumpUntil(() => SizeOfChild(presenter) == new Size<double>(40, 30), presenter), Is.True,
            "returning to the tab has to show the content that was built while it was away");
        Assert.That(firstBuilds, Is.EqualTo(1), "the finished build was parked, so the return must not build it again");
    }

    // Flicking through tabs faster than they build: whatever the user lands on is what must end up on screen, and the
    // spinner must not be where the content should be. Every earlier build is superseded on its way - some before they
    // start, some mid-flight - and none of them may take the place of the one that was actually chosen.
    [Test]
    public void FlickingThroughContent_EndsOnTheOneTheUserStoppedAt()
    {
        var presenter = new ContentPresenter
        {
            DeferContent = true,
            LoadingTemplate = new DataTemplate(() => new TemplateResult { RootComponent = new Border { Width = 8, Height = 8 } })
        };

        for (var i = 1; i <= 6; i++)
        {
            var size = 20 + i * 10;
            presenter.ContentTemplate = new DataTemplate(() => new TemplateResult { RootComponent = new Border { Width = size, Height = size } });
            presenter.Content = new object();
            presenter.Measure(Host);   // the frame the switch lands in
        }

        Assert.That(PumpUntil(() => SizeOfChild(presenter) == new Size<double>(80, 80), presenter), Is.True,
            "the content the user stopped at has to be the one that shows - and it has to show at all");
    }

    // Content adopted after a deferred build that STRETCHES fills the presenter. The layout pass can find it dirty before
    // the presenter has arranged it, and arranges it on its own into its desired size; the presenter then arranges at the
    // size it had for the spinner, and that same size is not "nothing changed" - the child was arranged into another slot.
    [Test]
    public void AdoptedContentThatStretches_FillsThePresenter()
    {
        var presenter = new ContentPresenter
        {
            DeferContent = true,
            LoadingTemplate = new DataTemplate(() => new TemplateResult { RootComponent = new Border { Width = 8, Height = 8 } }),
            ContentTemplate = new DataTemplate(() => new TemplateResult
            {
                RootComponent = new Border { Child = new Border { Width = 40, Height = 30 } }
            }),
            Content = new object()
        };

        presenter.Measure(Host);
        presenter.Arrange(new Rect(0, 0, Host.Width, Host.Height));
        var spinner = presenter.VisualChildren.First();

        var sw = Stopwatch.StartNew();
        IMeasurableComponent adopted = null;
        while (adopted == null && sw.Elapsed.TotalSeconds < 5)
        {
            LoopSignal.Drain();
            if (presenter.VisualChildren.FirstOrDefault(c => !ReferenceEquals(c, spinner)) is IMeasurableComponent child)
            {
                adopted = child;
                break;
            }

            presenter.Measure(Host);
            presenter.Arrange(new Rect(0, 0, Host.Width, Host.Height));
            Compositor.StartPending();
            Thread.Sleep(1);
        }

        Assert.That(adopted, Is.Not.Null, "precondition: the content arrives");

        presenter.Measure(Host);
        adopted.Arrange(new Rect(adopted.DesiredSize));
        presenter.Arrange(new Rect(0, 0, Host.Width, Host.Height));

        Assert.That(adopted.RenderSize, Is.EqualTo(Host));
    }

    // A background build that fails leaves the presenter holding a spinner forever, which is worse than the pause it was
    // meant to hide. It falls back to building the ordinary way instead - the case that showed up as a tab that never
    // arrived when two views were being built at once.
    [Test]
    public void AFailedBackgroundBuild_FallsBackToBuildingItHere()
    {
        var attempts = 0;
        var presenter = new ContentPresenter
        {
            DeferContent = true,
            LoadingTemplate = new DataTemplate(() => new TemplateResult { RootComponent = new Border { Width = 8, Height = 8 } }),
            ContentTemplate = new DataTemplate(() =>
            {
                // Fails exactly once, the way a registry written for one builder at a time fails the second one.
                if (System.Threading.Interlocked.Increment(ref attempts) == 1) throw new InvalidOperationException("busy");
                return new TemplateResult { RootComponent = new Border { Width = 40, Height = 30 } };
            }),
            Content = new object()
        };

        presenter.Measure(Host);

        Assert.That(PumpUntil(() => SizeOfChild(presenter) == new Size<double>(40, 30), presenter), Is.True,
            "a failed background build must not leave the spinner standing - the content is built here instead");
    }

    // The loading visual belongs to the tab being ENTERED. While the one being left is still sliding away, the area it
    // vacates stays empty - a spinner there reads as "the tab you are leaving is loading" - and content that lands during
    // the slide waits for it, or it would have the outgoing tab sliding over the top of it.
    [Test]
    public void ContentThatArrivesMidTransition_WaitsForTheSwapToFinish()
    {
        var presenter = new ContentPresenter
        {
            DeferContent = true,
            LoadingTemplate = new DataTemplate(() => new TemplateResult { RootComponent = new Border { Width = 8, Height = 8 } }),
            ContentTemplate = new DataTemplate(() => new TemplateResult { RootComponent = new Border { Width = 40, Height = 30 } }),
            Content = new object()
        };

        // Animations run for what is on screen, so the presenter goes up like a real one - under a surface that is NOT
        // one-shot, or there would be nothing to defer.
        var window = new Window { Width = 100, Height = 100, Content = presenter };
        ((IMeasurableComponent)window).Measure(Host);
        Assert.That(PumpUntil(() => SizeOfChild(presenter) == new Size<double>(40, 30), presenter), Is.True, "first content arrives");

        presenter.ContentTransition = ContentTransition.SlideLeft;
        presenter.TransitionDuration = 0.5;
        presenter.ContentTemplate = new DataTemplate(() => new TemplateResult { RootComponent = new Border { Width = 60, Height = 50 } });
        presenter.Content = new object();

        presenter.Measure(Host);
        presenter.Arrange(new Rect(0, 0, Host.Width, Host.Height));   // the swap starts here

        // The build lands well inside the half-second the swap takes; until it is over, nothing new may appear.
        PumpUntil(() => false, presenter, seconds: 0.25);
        Assert.That(HasChild(presenter, new Size<double>(60, 50)), Is.False,
            "while the tab that is leaving is still sliding, the content it would slide over must not be swapped in");
        Assert.That(HasChild(presenter, new Size<double>(8, 8)), Is.False,
            "...and the spinner belongs to the tab being entered, not to the one leaving");
        Assert.That(HasChild(presenter, new Size<double>(40, 30)), Is.True, "what is on screen is the tab on its way out");

        // ...and once the swap is over, it takes its place.
        Assert.That(PumpUntil(() => SizeOfChild(presenter) == new Size<double>(60, 50), presenter, seconds: 5, tick: 0.1), Is.True,
            "the content takes its place as soon as the swap it belongs to is done");
    }

    // Content built during the swap still has to be put up, and on a large view that holds the loop thread for a second
    // (the DataGrid page: 1.8 s). The spinner goes up first, for a frame of its own, so it turns through that second.
    [Test]
    public void ContentThatArrivesMidTransition_PutsTheSpinnerUpBeforeItself()
    {
        var presenter = new ContentPresenter
        {
            DeferContent = true,
            LoadingTemplate = new DataTemplate(() => new TemplateResult { RootComponent = new Border { Width = 8, Height = 8 } }),
            ContentTemplate = new DataTemplate(() => new TemplateResult { RootComponent = new Border { Width = 40, Height = 30 } }),
            Content = new object()
        };

        var window = new Window { Width = 100, Height = 100, Content = presenter };
        ((IMeasurableComponent)window).Measure(Host);
        Assert.That(PumpUntil(() => SizeOfChild(presenter) == new Size<double>(40, 30), presenter), Is.True, "first content arrives");

        presenter.ContentTransition = ContentTransition.SlideLeft;
        presenter.TransitionDuration = 0.5;
        presenter.ContentTemplate = new DataTemplate(() => new TemplateResult { RootComponent = new Border { Width = 60, Height = 50 } });
        presenter.Content = new object();
        presenter.Measure(Host);
        presenter.Arrange(new Rect(0, 0, Host.Width, Host.Height));
        PumpUntil(() => false, presenter, seconds: 0.25);

        var spinnerFirst = false;
        Assert.That(PumpUntil(() =>
        {
            if (HasChild(presenter, new Size<double>(60, 50))) return true;
            spinnerFirst |= HasChild(presenter, new Size<double>(8, 8));
            return false;
        }, presenter, seconds: 5, tick: 0.1), Is.True, "the content arrives");
        Assert.That(spinnerFirst, Is.True, "the spinner is up for a frame before the content is put up");
    }

    // A one-shot render has no "next frame", so it must not hand back a picture with a spinner where the content belongs:
    // the synchronous switch is what every bake path (RenderTargetBitmap, the designer preview, the test harness) turns on.
    [Test]
    public void AOneShotRender_BuildsItsContentInline()
    {
        var presenter = new ContentPresenter
        {
            DeferContent = true,
            LoadingTemplate = new DataTemplate(() => new TemplateResult { RootComponent = new Border { Width = 8, Height = 8 } }),
            ContentTemplate = new DataTemplate(() => new TemplateResult { RootComponent = new Border { Width = 40, Height = 30 } }),
            Content = new object()
        };

        // A surface that is drawn once - the harness, a bitmap bake, the designer preview. It says so itself; nothing is
        // switched on around the render.
        new Rendering.TestRoot(100, 100).Add(presenter);
        presenter.Measure(Host);

        Assert.That(SizeOfChild(presenter), Is.EqualTo(new Size<double>(40, 30)),
            "a bake has only the frame it was asked for - its content cannot be left for later");
    }
}
