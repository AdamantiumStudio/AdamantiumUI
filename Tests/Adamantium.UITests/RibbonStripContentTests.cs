using Adamantium.UI.Controls;
using NUnit.Framework;

namespace Adamantium.UITests;

/// <summary>The application's own commands in the strip, next to "File" - a drop-down of what the document is made
/// of. Held like the application menu: a logical child, or the window's DataContext never reaches its bindings.</summary>
[TestFixture]
public class RibbonStripContentTests
{
    [Test]
    public void TheStripContent_IsALogicalChildOfTheRibbon()
    {
        var modules = new RibbonDropDownButton { Content = "Modules" };
        var ribbon = new Ribbon { StripContent = modules };

        Assert.That(ribbon.LogicalChildren, Does.Contain(modules));
    }

    [Test]
    public void ReplacingTheStripContent_DropsThePreviousOne()
    {
        var modules = new RibbonDropDownButton { Content = "Modules" };
        var ribbon = new Ribbon { StripContent = modules };
        var replacement = new RibbonDropDownButton { Content = "Workspace" };

        ribbon.StripContent = replacement;

        Assert.Multiple(() =>
        {
            Assert.That(ribbon.LogicalChildren, Does.Not.Contain(modules));
            Assert.That(ribbon.LogicalChildren, Does.Contain(replacement));
        });
    }

    [Test]
    public void TheStripContent_SeesTheRibbonsDataContext()
    {
        var modules = new RibbonDropDownButton { Content = "Modules" };
        var ribbon = new Ribbon { StripContent = modules };
        var document = new object();

        ribbon.DataContext = document;

        Assert.That(modules.DataContext, Is.SameAs(document));
    }
}
