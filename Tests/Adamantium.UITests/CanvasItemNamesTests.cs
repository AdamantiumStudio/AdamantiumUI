using System.Linq;
using Adamantium.Mathematics;
using Adamantium.UI.Controls.Buttons;
using Adamantium.UI.Controls.Canvases;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Media;
using NUnit.Framework;

namespace Adamantium.UITests;

/// <summary>A name and a comment on anything drawn: the name is what the plane's list calls it and what a search finds,
/// the comment a note kept apart - both carried by a copy and by SVG, and the inspector's list narrowed by the plane's
/// search.</summary>
[TestFixture]
public class CanvasItemNamesTests
{
    private static ShapeItem Box(string name) =>
        new(CanvasShape.Rectangle, new Rect(0, 0, 20, 10), Brushes.White, 1) { Name = name };

    private static StrokeItem Ink(string name)
    {
        var stroke = new StrokeItem(new Vector2<double>(0, 0), Brushes.White, 2) { Name = name };
        stroke.Add(new Vector2<double>(0, 0));
        stroke.Add(new Vector2<double>(40, 0));
        return stroke;
    }

    [Test]
    public void AName_IsWhatTheListCallsIt()
    {
        var unnamed = new TextItem(new Vector2<double>(0, 0), "a label", Brushes.White, 12);
        var named = new TextItem(new Vector2<double>(0, 0), "a label", Brushes.White, 12) { Name = "Bolt" };

        Assert.Multiple(() =>
        {
            Assert.That(Box("Bolts").Title, Is.EqualTo("Bolts"));
            Assert.That(Box(null).Title, Is.Empty);
            Assert.That(unnamed.Title, Is.EqualTo("a label"), "without a name, what a text says");
            Assert.That(named.Title, Is.EqualTo("Bolt"));
        });
    }

    [Test]
    public void ACopy_KeepsTheNameAndTheComment()
    {
        var shape = Box("Bolts");
        shape.Comment = "check the torque";
        var group = new GroupItem([Ink("trail")]) { Name = "set", Comment = "two of them" };

        var copied = shape.Copy();
        var copiedGroup = group.Copy();

        Assert.Multiple(() =>
        {
            Assert.That(copied.Name, Is.EqualTo("Bolts"));
            Assert.That(copied.Comment, Is.EqualTo("check the torque"));
            Assert.That(copiedGroup.Name, Is.EqualTo("set"));
            Assert.That(copiedGroup.Comment, Is.EqualTo("two of them"));
        });
    }

    [Test]
    public void SvgCarriesNamesAndComments_DownIntoGroups()
    {
        var shape = Box("Bolts");
        shape.Comment = "check\nthe torque";
        var text = new TextItem(new Vector2<double>(0, 30), "M10", Brushes.White, 12) { Name = "size", Comment = "anchor" };
        var group = new GroupItem([Ink("trail")]) { Name = "set" };

        var read = CanvasSvg.Load(CanvasSvg.Save([shape, text, group]), out var skipped);

        Assert.Multiple(() =>
        {
            Assert.That(skipped, Is.Zero, "a title or a note is not a shape that could not be read");
            Assert.That(read[0].Name, Is.EqualTo("Bolts"));
            Assert.That(read[0].Comment, Is.EqualTo("check\nthe torque"));
            Assert.That(((TextItem)read[1]).Text, Is.EqualTo("M10"), "the words are not mixed with the name");
            Assert.That(read[1].Name, Is.EqualTo("size"));
            Assert.That(read[1].Comment, Is.EqualTo("anchor"));
            Assert.That(read[2].Name, Is.EqualTo("set"));
            Assert.That(((GroupItem)read[2]).Children.Single().Name, Is.EqualTo("trail"));
        });
    }

    [Test]
    public void SomethingSvgLeavesOut_LeavesItsNameOut_TooRatherThanOnItsNeighbour()
    {
        var shape = Box("Bolts");
        shape.Order = 0;
        var control = new ElementItem(new Button(), new Rect(0, 0, 40, 20)) { Name = "Confirm", Comment = "asks first", Order = 1 };

        var svg = System.Xml.Linq.XDocument.Parse(CanvasSvg.Save([shape, control]));
        var drawn = svg.Root!.Elements().Single(element => element.Name.LocalName == "rect");

        Assert.Multiple(() =>
        {
            Assert.That((string)drawn.Attribute("data-adm-name"), Is.EqualTo("Bolts"));
            Assert.That(drawn.Attribute("data-adm-comment"), Is.Null);
            Assert.That(drawn.Elements().Count(element => element.Name.LocalName == "title"), Is.EqualTo(1));
        });
    }

    [Test]
    public void AStrangersTextTitle_IsItsName_NotPartOfWhatItSays()
    {
        const string svg = "<svg xmlns=\"http://www.w3.org/2000/svg\"><text x=\"0\" y=\"10\"><title>Label</title>Hello</text></svg>";

        var read = (TextItem)CanvasSvg.Load(svg).Single();

        Assert.Multiple(() =>
        {
            Assert.That(read.Text, Is.EqualTo("Hello"));
            Assert.That(read.Name, Is.EqualTo("Label"));
        });
    }

    [Test]
    public void AFramesName_IsWhatItSaysAlongTheTop_InOneLine()
    {
        var frame = new CanvasFrameItem(new Rect(0, 0, 100, 60), "Stage", null, null);

        frame.Name = "Bay";
        Assert.That(frame.Title, Is.EqualTo("Bay"));

        frame.Name = null;
        Assert.Multiple(() =>
        {
            Assert.That(frame.Title, Is.Empty, "cleared, not lost");
            Assert.That(((ICanvasItem)frame).TakesName, Is.True);
            Assert.That(((ICanvasItem)new ElementItem(new Button(), new Rect(0, 0, 40, 20))).TakesName, Is.True,
                "a control of its own takes a name; a node standing for the application's object does not");
        });
    }

    [Test]
    public void ASearch_FindsByName_IgnoringCase_AndAGroupByWhatIsInIt()
    {
        var board = new DrawingBoard { SearchText = "BOLT" };

        Assert.Multiple(() =>
        {
            Assert.That(board.Matches(Box("Bolts")), Is.True);
            Assert.That(board.Matches(Box("Nuts")), Is.False);
            Assert.That(board.Matches(new GroupItem([Box("bolt 2")])), Is.True, "found inside");
            board.SearchText = "  ";
            Assert.That(board.Matches(Box("Nuts")), Is.True, "nothing asked keeps everything");
        });
    }

    [Test]
    public void TheInspectorsList_KeepsWhatTheSearchFinds()
    {
        var scene = new CanvasScene();
        scene.Add(Box("Bolts"));
        scene.Add(Box("Nuts"));
        scene.Add(Box("Bolt cover"));
        var board = new DrawingBoard { Scene = scene };
        var inspector = new CanvasInspector { Canvas = board };

        board.SearchText = "bolt";

        Assert.Multiple(() =>
        {
            Assert.That(inspector.Structure.Cast<ICanvasItem>().Select(item => item.Name),
                Is.EqualTo(new[] { "Bolt cover", "Bolts" }), "topmost first");
            Assert.That(inspector.FoundCount, Is.EqualTo(2));
            Assert.That(inspector.ItemCount, Is.EqualTo(3), "what is on the plane, all of it");
        });

        board.SearchText = string.Empty;

        Assert.That(inspector.FoundCount, Is.EqualTo(3));
    }

    private sealed class SheetsBoard : DrawingBoard
    {
        public override string PlaceOf(ICanvasItem item) => "sheet 3";
    }

    [Test]
    public void OneItemSelected_IsHeadedByItsNameAndItsPlace_WhereThePlaneSaysOne()
    {
        var scene = new CanvasScene();
        var named = Box("Bolts");
        var unnamed = Box(null);
        scene.Add(named);
        scene.Add(unnamed);
        var sheets = new SheetsBoard { Scene = scene };
        var inspector = new CanvasInspector { Canvas = sheets };

        sheets.SelectMany([named], false);
        var withName = inspector.Header;
        sheets.SelectMany([unnamed], false);
        var withoutName = inspector.Header;
        var plain = new CanvasInspector { Canvas = new DrawingBoard { Scene = new CanvasScene() } };

        Assert.Multiple(() =>
        {
            Assert.That(withName, Is.EqualTo("Bolts · sheet 3"));
            Assert.That(withoutName, Is.EqualTo("sheet 3"));
            Assert.That(plain.Header, Is.Null, "a plane with nothing to say leaves one item unheaded, as before");
        });
    }

    [Test]
    public void ADrawingsOwnActions_CanBeLeftOut()
    {
        var inspector = new CanvasInspector { Canvas = new DrawingBoard() };
        Assert.Multiple(() =>
        {
            Assert.That(inspector.DrawingActions, Is.EqualTo(Visibility.Visible));
            Assert.That(inspector.ClearAction, Is.EqualTo(Visibility.Visible));
        });

        inspector.ShowsDrawingActions = false;

        Assert.Multiple(() =>
        {
            Assert.That(inspector.DrawingActions, Is.EqualTo(Visibility.Collapsed));
            Assert.That(inspector.ClearAction, Is.EqualTo(Visibility.Collapsed), "a viewer's marks are not the viewer's to clear away");
        });
    }
}
