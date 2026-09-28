using System.Linq;
using Adamantium.Mathematics;
using Adamantium.UI.Controls.Docking;
using Adamantium.UI.Core;
using NUnit.Framework;

namespace Adamantium.UITests;

/// <summary>
/// Restoring the SAME layout more than once. Once works; the second and third press is where an arrangement that is
/// applied on top of itself shows whether anything was left behind by the first - reported from the demo as tabs that
/// come back empty, come back partly, or stop coming back at all.
/// </summary>
[TestFixture]
public class DockingRestoreTwiceTests
{
    private static DockingArea Area()
    {
        var documents = new PaneGroup { Name = "documents", Zone = DockZone.Center };
        documents.Items.Add(new Pane { Header = "Scene", Id = "scene", Kind = PaneKind.Document });
        documents.Items.Add(new Pane { Header = "Game", Id = "game", Kind = PaneKind.Document });

        var tools = new PaneGroup { Name = "tools", Zone = DockZone.Right, Size = 240 };
        tools.Items.Add(new Pane { Header = "Inspector", Id = "inspector", Kind = PaneKind.Tool });
        tools.Items.Add(new Pane { Header = "Hierarchy", Id = "hierarchy", Kind = PaneKind.Tool });

        var area = new DockingArea { DividerThickness = 0 };
        area.Children.Add(documents);
        area.Children.Add(tools);

        area.Measure(new Size(1000, 800));
        area.Arrange(new Rect(0, 0, 1000, 800));
        return area;
    }

    private static string[] PaneIdsIn(DockingArea area) =>
        area.Layout.Roots.SelectMany(root => DockingLayout.PanesIn(root.Content)).ToArray();

    [Test]
    public void RestoringTheSameLayoutTwice_LeavesEveryPaneWhereItWas()
    {
        var area = Area();
        var saved = area.SaveLayout();
        var before = PaneIdsIn(area);

        Assert.That(area.LoadLayout(saved), Is.True, "first restore");
        Assert.That(PaneIdsIn(area), Is.EquivalentTo(before), "after one restore");

        Assert.That(area.LoadLayout(saved), Is.True, "second restore");
        Assert.That(PaneIdsIn(area), Is.EquivalentTo(before), "after two restores - nothing lost, nothing doubled");

        Assert.That(area.LoadLayout(saved), Is.True, "third restore");
        Assert.That(PaneIdsIn(area), Is.EquivalentTo(before), "and after three");
    }

    // Whether the pane's control is in some panel's items; not VisualParent, since no theme means no items presenter.
    private static bool IsInSomePanel(DockingArea area, string paneId)
    {
        var pane = area.PaneById(paneId);
        return pane != null && area.Groups.Any(group => group.Items.Contains(pane));
    }

    // Panes are controls as well as ids: after a second restore each control must still be in a panel, not an empty tab.
    [Test]
    public void AfterASecondRestore_EveryPaneIsStillInAPanel()
    {
        var area = Area();
        var saved = area.SaveLayout();

        area.LoadLayout(saved);
        foreach (var id in PaneIdsIn(area))
        {
            Assert.That(IsInSomePanel(area, id), Is.True, $"'{id}' left its panel after ONE restore");
        }

        area.LoadLayout(saved);
        foreach (var id in PaneIdsIn(area))
        {
            Assert.That(IsInSomePanel(area, id), Is.True, $"'{id}' left its panel after TWO restores");
        }
    }
}
