using Adamantium.Mathematics;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Buttons;
using Adamantium.UI.Controls.Decorators;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Templates;
using Adamantium.UI.Extensions;
using Adamantium.UI.Rendering;
using NUnit.Framework;

namespace Adamantium.UITests;

/// <summary>A loaded element that is destroyed hears Unloaded exactly once, however it was destroyed.</summary>
[TestFixture]
public class UnloadedOnDiscardTests
{
    private static void Drain()
    {
        while (DiscardedVisuals.Drain(64) > 0)
        {
        }
    }

    [Test]
    public void LoadedContentLeavingAPresenterIsUnloadedOnce()
    {
        var presenter = new ContentPresenter();
        var body = new Border();
        var unloaded = 0;
        body.Unloaded += (_, _) => unloaded++;
        presenter.Content = body;
        presenter.Measure(new Size(100, 100));
        body.Render(new DrawingContext());

        presenter.Content = new Border();
        presenter.Measure(new Size(100, 100));
        Drain();

        Assert.That(unloaded, Is.EqualTo(1), "content released by its presenter never heard Unloaded");
    }

    [Test]
    public void ContentThatNeverLoadedIsNotUnloaded()
    {
        var presenter = new ContentPresenter();
        var body = new Border();
        var unloaded = 0;
        body.Unloaded += (_, _) => unloaded++;
        presenter.Content = body;
        presenter.Measure(new Size(100, 100));

        presenter.Content = new Border();
        presenter.Measure(new Size(100, 100));
        Drain();

        Assert.That(unloaded, Is.Zero, "an element that never loaded was unloaded");
    }

    [Test]
    public void ATemplatePartIsUnloadedOnceThoughTornDownAndDrained()
    {
        Border part = null;
        var button = new Button
        {
            Template = new ControlTemplate(() =>
            {
                part = new Border();
                return new TemplateResult { RootComponent = part };
            })
        };
        var window = new Window { Width = 200, Height = 100, Content = button };
        WindowExtension.UpdateTree(window);
        Assume.That(part, Is.Not.Null, "precondition: the template was built");

        var builtPart = part;
        var unloaded = 0;
        builtPart.Unloaded += (_, _) => unloaded++;
        builtPart.Render(new DrawingContext());

        button.Template = new ControlTemplate(() => new TemplateResult { RootComponent = new Border() });
        WindowExtension.UpdateTree(window);
        Drain();

        Assert.That(unloaded, Is.EqualTo(1), "the teardown and the release each told the part it was unloaded");
    }
}
