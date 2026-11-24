using Adamantium.Mathematics;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Decorators;
using Adamantium.UI.Controls.Panels;
using Adamantium.UI.Core;
using NUnit.Framework;

namespace Adamantium.UITests;

// A status bar's fields from the left, those docked right from the right; as it narrows, the fields that do not fit go -
// the lowest priority first, of the same priority the one written later - instead of standing over one another.
[TestFixture]
public class StatusBarPanelTests
{
    [Test]
    public void AllFitting_TheyStandFromTheLeft_AndTheDockedRightFromTheRight()
    {
        var (panel, problems, zoom, caret) = Bar();

        LayOut(panel, 400);

        Assert.Multiple(() =>
        {
            Assert.That(problems.Bounds.X, Is.EqualTo(0));
            Assert.That(zoom.Bounds.X, Is.EqualTo(400 - 40), "the first docked right at the edge");
            Assert.That(caret.Bounds.X, Is.EqualTo(400 - 40 - 80), "the next inside it");
            Assert.That(panel.HasLeftOut(zoom) || panel.HasLeftOut(caret) || panel.HasLeftOut(problems), Is.False);
        });
    }

    [Test]
    public void TooNarrow_TheLowestPriorityGoes_AndTheOthersDoNotStandOverOneAnother()
    {
        var (panel, problems, zoom, caret) = Bar();
        StatusBar.SetPriority(caret, 2);
        StatusBar.SetPriority(problems, 1);

        LayOut(panel, 150);

        Assert.Multiple(() =>
        {
            Assert.That(panel.HasLeftOut(zoom), Is.True, "the lowest priority");
            Assert.That(panel.HasLeftOut(caret), Is.False);
            Assert.That(panel.HasLeftOut(problems), Is.False);
            Assert.That(caret.Bounds.X, Is.EqualTo(150 - 80), "at the edge, where the zoom stood");
            Assert.That(zoom.Bounds.X, Is.GreaterThanOrEqualTo(150), "past the bar's end, not drawn");
            Assert.That(zoom.Visibility, Is.EqualTo(Visibility.Visible), "its own visibility left as it was");
        });
    }

    [Test]
    public void OfTheSamePriority_TheOneWrittenLaterGoesFirst()
    {
        var (panel, problems, zoom, caret) = Bar();

        LayOut(panel, 150);

        Assert.Multiple(() =>
        {
            Assert.That(panel.HasLeftOut(caret), Is.True, "written last");
            Assert.That(panel.HasLeftOut(zoom), Is.False);
            Assert.That(panel.HasLeftOut(problems), Is.False);
        });
    }

    [Test]
    public void AFieldCollapsed_TakesNoRoom_AndWidenedAgain_AllComeBack()
    {
        var (panel, problems, zoom, caret) = Bar();
        problems.Visibility = Visibility.Collapsed;

        LayOut(panel, 130);
        var narrow = panel.HasLeftOut(caret) || panel.HasLeftOut(zoom);
        LayOut(panel, 100);
        var tooNarrow = panel.HasLeftOut(caret);
        LayOut(panel, 400);

        Assert.Multiple(() =>
        {
            Assert.That(narrow, Is.False, "the collapsed field leaves its room to the others");
            Assert.That(tooNarrow, Is.True);
            Assert.That(panel.HasLeftOut(caret), Is.False, "back as the bar widens");
        });
    }

    [Test]
    public void AFieldThatShrinks_NarrowsBeforeAnyGoes_AndGoesOnlyWithNoWidthLeft()
    {
        var (panel, _, zoom, caret) = Bar();
        // A message: as wide as its words, narrowed by the room it is given.
        var problems = new Border { Child = new Border { Width = 300, Height = 20 } };
        StatusBar.SetShrinks(problems, true);
        panel.Children[0] = problems;

        LayOut(panel, 250);
        var narrowed = problems.Bounds.Width;
        var kept = !panel.HasLeftOut(zoom) && !panel.HasLeftOut(caret);
        LayOut(panel, 100);

        Assert.Multiple(() =>
        {
            Assert.That(narrowed, Is.EqualTo(250 - 40 - 80), "the room the others leave");
            Assert.That(kept, Is.True, "no field gone while it can narrow");
            Assert.That(panel.HasLeftOut(problems), Is.True, "no width left: it goes too");
            Assert.That(panel.HasLeftOut(zoom), Is.False);
        });
    }

    [Test]
    public void ASeparatorLeftWithNothingToStandBetween_GoesWithTheFields()
    {
        var (panel, problems, zoom, caret) = Bar();
        var separator = new Separator { Width = 9, Height = 20 };
        DockPanel.SetDock(separator, Dock.Right);
        panel.Children.Insert(2, separator);
        StatusBar.SetPriority(zoom, 1);

        LayOut(panel, 400);
        var keptBetween = !panel.HasLeftOut(separator);
        LayOut(panel, 120);

        Assert.Multiple(() =>
        {
            Assert.That(keptBetween, Is.True, "between the zoom and the caret");
            Assert.That(panel.HasLeftOut(caret), Is.True);
            Assert.That(panel.HasLeftOut(separator), Is.True, "nothing on its inner side");
            Assert.That(panel.HasLeftOut(zoom) || panel.HasLeftOut(problems), Is.False);
        });
    }

    [Test]
    public void AFieldGone_StandsWhollyPastTheEnd_AndTheBarIsAsTallAsTheFieldsKept()
    {
        var (panel, problems, zoom, caret) = Bar();
        caret.Height = 30;

        LayOut(panel, 150);
        panel.Children.Remove(caret);

        Assert.Multiple(() =>
        {
            Assert.That(caret.Bounds.X, Is.GreaterThan(150), "wholly past the clipped end: read offscreen by automation");
            Assert.That(panel.DesiredSize.Height, Is.EqualTo(20), "the tall field gone");
            Assert.That(panel.HasLeftOut(caret), Is.False, "no longer in the bar");
        });
    }

    private static (StatusBarPanel Panel, Border Problems, Border Zoom, Border Caret) Bar()
    {
        var problems = new Border { Width = 60, Height = 20 };
        var zoom = new Border { Width = 40, Height = 20 };
        var caret = new Border { Width = 80, Height = 20 };
        DockPanel.SetDock(zoom, Dock.Right);
        DockPanel.SetDock(caret, Dock.Right);
        var panel = new StatusBarPanel();
        panel.Children.Add(problems);
        panel.Children.Add(zoom);
        panel.Children.Add(caret);
        return (panel, problems, zoom, caret);
    }

    private static void LayOut(StatusBarPanel panel, double width)
    {
        panel.Measure(new Size<double>(width, 22), force: true);
        panel.Arrange(new Rect(0, 0, width, 22), force: true);
    }
}
