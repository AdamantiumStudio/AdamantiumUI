using Adamantium.UI.Controls.Base;
using Adamantium.UI.Controls.Decorators;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Templates;
using NUnit.Framework;

namespace Adamantium.UITests;

/// <summary>A COLLAPSED CONTROL BUILDS NOTHING until it is shown: a scroll bar with nothing to scroll, a page nobody has
/// turned to. Its template is built the first time it stops being collapsed, and kept when it is collapsed again.</summary>
[TestFixture]
public class CollapsedTemplateTests
{
    private int _built;

    private ControlTemplate Counted() => new(() =>
    {
        _built++;
        var root = new Border();
        var result = new TemplateResult();
        result.RegisterName("PART_Root", root);
        result.RootComponent = root;
        return result;
    });

    [SetUp]
    public void Reset() => _built = 0;

    [Test]
    public void ACollapsedControl_BuildsItsTemplateOnlyOnceShown()
    {
        var control = new Control { Visibility = Visibility.Collapsed, Template = Counted() };

        Assert.That(_built, Is.Zero, "built while collapsed");
        Assert.That(control.GetTemplateChild("PART_Root"), Is.Null);

        control.Visibility = Visibility.Visible;

        Assert.That(_built, Is.EqualTo(1));
        Assert.That(control.GetTemplateChild("PART_Root"), Is.Not.Null);
    }

    [Test]
    public void AHiddenControl_StillBuildsItsTemplate()
    {
        var control = new Control { Visibility = Visibility.Hidden, Template = Counted() };

        Assert.That(_built, Is.EqualTo(1), "a hidden control keeps its place, so it is laid out and needs its parts");
    }

    [Test]
    public void CollapsedAgain_TheTemplateIsKept()
    {
        var control = new Control { Visibility = Visibility.Collapsed, Template = Counted() };
        control.Visibility = Visibility.Visible;
        var root = control.GetTemplateChild("PART_Root");

        control.Visibility = Visibility.Collapsed;
        control.Visibility = Visibility.Visible;

        Assert.That(_built, Is.EqualTo(1));
        Assert.That(control.GetTemplateChild("PART_Root"), Is.SameAs(root));
    }

    [Test]
    public void ATemplateChangedWhileCollapsed_IsTheOneBuiltWhenShown()
    {
        var control = new Control { Visibility = Visibility.Collapsed, Template = Counted() };
        var other = new Border();
        control.Template = new ControlTemplate(() =>
        {
            var result = new TemplateResult();
            result.RegisterName("PART_Root", other);
            result.RootComponent = other;
            return result;
        });

        control.Visibility = Visibility.Visible;

        Assert.That(_built, Is.Zero, "the first template was never shown");
        Assert.That(control.GetTemplateChild("PART_Root"), Is.SameAs(other));
    }
}
