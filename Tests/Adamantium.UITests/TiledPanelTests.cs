using Adamantium.Mathematics;
using Adamantium.UI.Controls.Decorators;
using Adamantium.UI.Controls.Panels;
using Adamantium.UI.Core;
using NUnit.Framework;

namespace Adamantium.UITests;

/// <summary>The tiles a <see cref="TiledPanel"/> lays its children in: in their order, as its scheme says, one alone
/// when maximized, the gaps where the splits put them.</summary>
[TestFixture]
public class TiledPanelTests
{
    [Test]
    public void FourTilesTwoByTwo_InTheChildrensOrder_TheGapsBetweenThem()
    {
        var (panel, children) = Laid(TileLayout.Four, 4);

        Assert.Multiple(() =>
        {
            Assert.That(children[0].Bounds, Is.EqualTo(new Rect(0, 0, 98, 48)));
            Assert.That(children[1].Bounds, Is.EqualTo(new Rect(102, 0, 98, 48)));
            Assert.That(children[2].Bounds, Is.EqualTo(new Rect(0, 52, 98, 48)));
            Assert.That(children[3].Bounds, Is.EqualTo(new Rect(102, 52, 98, 48)));
        });
    }

    [Test]
    public void AChildTheSchemeHasNoTileFor_IsCollapsed()
    {
        var (_, children) = Laid(TileLayout.TwoColumns, 3);

        Assert.Multiple(() =>
        {
            Assert.That(children[1].Bounds, Is.EqualTo(new Rect(102, 0, 98, 100)));
            Assert.That(children[2].Visibility, Is.EqualTo(Visibility.Collapsed));
        });
    }

    [Test]
    public void TheMaximizedItem_FillsThePanelAlone_ByItsData()
    {
        var (panel, children) = Laid(TileLayout.Four, 4);
        children[2].DataContext = "front";

        panel.MaximizedItem = "front";
        Lay(panel);

        Assert.Multiple(() =>
        {
            Assert.That(children[2].Bounds, Is.EqualTo(new Rect(0, 0, 200, 100)));
            Assert.That(children[0].Visibility, Is.EqualTo(Visibility.Collapsed));
            Assert.That(children[3].Visibility, Is.EqualTo(Visibility.Collapsed));
        });

        panel.MaximizedItem = null;
        Lay(panel);

        Assert.That(children[0].Visibility, Is.EqualTo(Visibility.Visible), "and back to the four");
    }

    [Test]
    public void TheSplitsMoveTheGaps()
    {
        var (panel, children) = Laid(TileLayout.ThreeLeft, 3);

        panel.ColumnSplit = 0.25;
        panel.RowSplit = 0.75;
        Lay(panel);

        Assert.Multiple(() =>
        {
            Assert.That(children[0].Bounds, Is.EqualTo(new Rect(0, 0, 49, 100)));
            Assert.That(children[1].Bounds, Is.EqualTo(new Rect(53, 0, 147, 72)));
            Assert.That(children[2].Bounds, Is.EqualTo(new Rect(53, 76, 147, 24)));
        });
    }

    [Test]
    public void SharedEvenly_TheTilesAreAlikeAgain()
    {
        var (panel, children) = Laid(TileLayout.Four, 4);
        panel.ColumnSplit = 0.2;
        panel.RowSplit = 0.8;
        Lay(panel);

        panel.ShareEvenly();
        Lay(panel);

        Assert.That(children[3].Bounds, Is.EqualTo(new Rect(102, 52, 98, 48)));
    }

    [Test]
    public void OneLargeAndThree_TheThreeShareTheirColumnEvenly()
    {
        var (_, children) = Laid(TileLayout.FourLeft, 4);

        Assert.Multiple(() =>
        {
            Assert.That(children[0].Bounds, Is.EqualTo(new Rect(0, 0, 98, 100)));
            Assert.That(children[1].Bounds.Height, Is.EqualTo(children[3].Bounds.Height).Within(1e-9));
            Assert.That(children[3].Bounds.Bottom, Is.EqualTo(100).Within(1e-9));
        });
    }

    private static (TiledPanel Panel, Border[] Children) Laid(TileLayout layout, int count)
    {
        var panel = new TiledPanel { Layout = layout };
        var children = new Border[count];
        for (var i = 0; i < count; i++)
        {
            children[i] = new Border();
            panel.Children.Add(children[i]);
        }

        Lay(panel);
        return (panel, children);
    }

    private static void Lay(TiledPanel panel)
    {
        panel.Measure(new Size(200, 100), force: true);
        panel.Arrange(new Rect(0, 0, 200, 100));
    }
}
