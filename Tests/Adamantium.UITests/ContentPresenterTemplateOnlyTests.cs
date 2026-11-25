using System.Linq;
using Adamantium.Mathematics;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Decorators;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Templates;
using NUnit.Framework;

namespace Adamantium.UITests;

// A presenter given a template and no content shows the template: an icon needs no data.
[TestFixture]
public class ContentPresenterTemplateOnlyTests
{
    private static DataTemplate Named(string name) =>
        new(() => new TemplateResult { RootComponent = new Border { Name = name, Width = 10, Height = 10 } });

    private static string Shown(ContentPresenter presenter)
    {
        presenter.Measure(new Size<double>(100, 100));
        return (presenter.VisualChildren.SingleOrDefault() as Border)?.Name;
    }

    [Test]
    public void ATemplateWithNoContent_IsShown()
    {
        var presenter = new ContentPresenter { ContentTemplate = Named("Glyph") };

        Assert.That(Shown(presenter), Is.EqualTo("Glyph"));
    }

    [Test]
    public void AnotherTemplate_ReplacesIt()
    {
        var presenter = new ContentPresenter { ContentTemplate = Named("Up") };
        Shown(presenter);

        presenter.ContentTemplate = Named("Down");

        Assert.That(Shown(presenter), Is.EqualTo("Down"));
    }

    [Test]
    public void NoTemplateAndNoContent_ShowsNothing()
    {
        var presenter = new ContentPresenter { ContentTemplate = Named("Glyph") };
        Shown(presenter);

        presenter.ContentTemplate = null;
        presenter.Measure(new Size<double>(100, 100));

        Assert.That(presenter.VisualChildren.Count(), Is.EqualTo(0));
    }

    // The template is for the content: with none, it binds to nothing rather than to whatever is around it.
    [Test]
    public void WithoutContent_TheVisualHasNoData()
    {
        var presenter = new ContentPresenter { DataContext = "outer", ContentTemplate = Named("Glyph") };
        Shown(presenter);

        var built = (Border)presenter.VisualChildren.Single();

        Assert.That(built.DataContext, Is.Null);
    }
}
