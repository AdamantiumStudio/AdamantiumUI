using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Decorators;
using Adamantium.UI.Core.Templates;
using NUnit.Framework;

namespace Adamantium.UITests;

/// <summary>
/// A template owns what it builds, and only that. A control built inside it may have applied its own template already
/// - code made from markup applies it as the control is made - and those parts are that control's: a tab strip whose
/// parts were taken over by the row-details template no longer knew its own tab control and refused every tab.
/// </summary>
[TestFixture]
public class TemplateOwnershipTests
{
    private static (ContentControl Inner, Border Part) InnerWithItsTemplate()
    {
        var part = new Border();
        var inner = new ContentControl
        {
            Template = new ControlTemplate(() => new TemplateResult { RootComponent = part })
        };
        return (inner, part);
    }

    [Test]
    public void ADataTemplate_LeavesTheParts_OfATemplateBuiltInsideIt()
    {
        var (inner, part) = InnerWithItsTemplate();
        Assume.That(part.TemplatedParent, Is.SameAs(inner), "the inner template must be applied before, or this proves nothing");
        var host = new ContentPresenter();

        new DataTemplate(() => new TemplateResult { RootComponent = new Border { Child = inner } }).Build(host);

        Assert.Multiple(() =>
        {
            Assert.That(inner.TemplatedParent, Is.SameAs(host), "what the data template built is its own");
            Assert.That(part.TemplatedParent, Is.SameAs(inner), "the inner control's part stays the inner control's");
        });
    }

    [Test]
    public void AControlTemplate_LeavesTheParts_OfATemplateBuiltInsideIt()
    {
        var (inner, part) = InnerWithItsTemplate();
        var outer = new ContentControl();

        new ControlTemplate(() => new TemplateResult { RootComponent = new Border { Child = inner } }).Build(outer);

        Assert.Multiple(() =>
        {
            Assert.That(inner.TemplatedParent, Is.SameAs(outer));
            Assert.That(part.TemplatedParent, Is.SameAs(inner));
        });
    }
}
