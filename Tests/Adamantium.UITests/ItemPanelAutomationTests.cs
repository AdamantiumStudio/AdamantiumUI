using System.Linq;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Panels;
using Adamantium.UI.Core.Templates;
using Adamantium.UI.Extensions;
using NUnit.Framework;

namespace Adamantium.UITests;

/// <summary>A panel put in an items control, as a grid of labelled boxes in a ribbon's group, has no peer of its own: the
/// boxes in it are the control's children to automation, not a stand-in that can do nothing.</summary>
[TestFixture]
public class ItemPanelAutomationTests
{
    [Test]
    public void ABoxInAGridInARibbonGroup_IsAChildOfTheGroup()
    {
        var width = new NumericUpDown { Name = "WidthBox" };
        var button = new RibbonButton { Name = "Reset", Content = "Reset" };
        var grid = new Grid { Children = { width } };
        var group = new RibbonGroup { Header = "Size" };
        group.Items.Add(grid);
        group.Items.Add(button);
        group.Template = new ControlTemplate(() =>
        {
            var presenter = new ItemsPresenter();
            var result = new TemplateResult { RootComponent = presenter };
            result.RegisterName("PART_ItemsPresenter", presenter);
            return result;
        });
        var window = new Window { Width = 400, Height = 200, Content = group };
        for (var i = 0; i < 5; i++)
        {
            WindowExtension.UpdateTree(window);
        }

        var children = group.GetAutomationPeer().GetChildren();

        Assert.Multiple(() =>
        {
            Assert.That(children.Select(child => child.AutomationId), Is.EqualTo(new[] { "WidthBox", "Reset" }));
            Assert.That(width.GetAutomationPeer().GetParent(), Is.SameAs(group.GetAutomationPeer()));
        });
    }
}
