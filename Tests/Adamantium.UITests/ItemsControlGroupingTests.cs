using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Adamantium.Core.Collections;
using Adamantium.Mathematics;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Buttons;
using Adamantium.UI.Controls.Decorators;
using Adamantium.UI.Controls.Panels;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Automation;
using Adamantium.UI.Core.Collections;
using Adamantium.UI.Core.Input;
using Adamantium.UI.Core.Templates;
using NUnit.Framework;

namespace Adamantium.UITests;

/// <summary>An items control shows its source's groups itself: the items stay one list, and each group's header is a row
/// of the panel before the group's items - a line across a wrap panel, a row of a stack.</summary>
[TestFixture]
public class ItemsControlGroupingTests
{
    public sealed record Part(string Name, string Kind);

    private sealed record Group(string Kind, int Count);

    private const double Header = 24;
    private const double Tile = 56;

    private static List<Part> Parts(params Group[] groups)
    {
        var parts = new List<Part>();
        foreach (var group in groups)
        {
            for (var i = 0; i < group.Count; i++)
            {
                parts.Add(new Part($"{group.Kind} {i}", group.Kind));
            }
        }

        return parts;
    }

    private static CollectionView Grouped(IEnumerable<Part> parts, params string[] levels)
    {
        var view = new CollectionView(parts.ToList());
        foreach (var level in levels)
        {
            view.GroupDescriptions.Add(new PropertyGroupDescription(level));
        }

        return view;
    }

    private static ItemsControl List(CollectionView view, Panel panel, double headerHeight = Header,
        bool canCollapse = false)
    {
        var list = new ItemsControl
        {
            Template = new ControlTemplate(() =>
            {
                var presenter = new ItemsPresenter();
                var result = new TemplateResult { RootComponent = presenter };
                result.RegisterName("PART_ItemsPresenter", presenter);
                return result;
            }),
            ItemsPanel = new ItemsPanelTemplate(() => new TemplateResult { RootComponent = panel }),
            ItemTemplate = new DataTemplate(() => new TemplateResult { RootComponent = new Border { Width = 52, Height = 52 } }),
            ItemsSource = view,
        };
        list.GroupStyle.Add(new GroupStyle
        {
            HeaderTemplate = new DataTemplate(() => new TemplateResult { RootComponent = new Border { Height = headerHeight } }),
            CanCollapse = canCollapse,
        });
        return list;
    }

    // The panel is a measure boundary: a list measured again at the same size does not reach it, so after the first pass
    // the panel itself is laid out again at the list's size, as the layout manager does.
    private static void Lay(ItemsControl list, double width, double height)
    {
        list.Measure(new Size<double>(width, height));
        list.Arrange(new Rect(0, 0, width, height));
        var panel = list.ItemsHostPanel;
        for (var pass = 0; pass < 3; pass++)
        {
            panel.InvalidateMeasure();
            panel.Measure(new Size<double>(width, height));
            panel.InvalidateArrange();
            panel.Arrange(new Rect(0, 0, width, height));
        }
    }

    private static Rect HeaderBounds(ItemsControl list, int place) => list.GroupHeaderGenerator.HeaderAt(place).Bounds;

    private static Rect ItemBounds(ItemsControl list, int index) => list.ItemContainerGenerator.ContainerFromIndex(index).Bounds;

    [Test]
    public void AWrapPanel_PutsEachHeaderOnALineOfItsOwn_AndTheGroupsTilesUnderIt()
    {
        var list = List(Grouped(Parts(new Group("Physics", 7), new Group("Joints", 5), new Group("Game", 2)), nameof(Part.Kind)),
            new WrapPanel { ItemWidth = Tile, ItemHeight = Tile });

        Lay(list, 300, 1000);

        Assert.Multiple(() =>
        {
            Assert.That(list.GroupHeaderPlaces.Select(place => place.BeforeItem), Is.EqualTo(new[] { 0, 7, 12 }));
            Assert.That(HeaderBounds(list, 0).Y, Is.EqualTo(0));
            Assert.That(HeaderBounds(list, 0).Width, Is.EqualTo(300), "a header is the whole line");
            Assert.That(ItemBounds(list, 0), Is.EqualTo(new Rect(0, Header, Tile, Tile)));
            Assert.That(ItemBounds(list, 5), Is.EqualTo(new Rect(0, Header + Tile, Tile, Tile)), "the sixth tile, the next line");
            Assert.That(HeaderBounds(list, 1).Y, Is.EqualTo(Header + 2 * Tile), "after the physics' two lines");
            Assert.That(ItemBounds(list, 7).Y, Is.EqualTo(2 * Header + 2 * Tile));
            Assert.That(HeaderBounds(list, 2).Y, Is.EqualTo(2 * Header + 3 * Tile), "after the joints' one full line");
            Assert.That(ItemBounds(list, 13).Y, Is.EqualTo(3 * Header + 3 * Tile));
            Assert.That(list.GroupHeaderGenerator.HeaderAt(1).DataContext, Is.InstanceOf<CollectionViewGroup>());
            Assert.That(((CollectionViewGroup)list.GroupHeaderGenerator.HeaderAt(1).DataContext).Name, Is.EqualTo("Joints"));
        });
    }

    [Test]
    public void AWrapPanel_IsAsTallAsItsHeadersAndLines()
    {
        var panel = new WrapPanel { ItemWidth = Tile, ItemHeight = Tile };
        var list = List(Grouped(Parts(new Group("Physics", 7), new Group("Joints", 5)), nameof(Part.Kind)), panel);

        list.Measure(new Size<double>(300, double.PositiveInfinity));

        Assert.That(panel.DesiredSize.Height, Is.EqualTo(2 * Header + 3 * Tile));
    }

    [Test]
    public void AStack_PutsEachHeaderAsARowBeforeItsItems()
    {
        var list = List(Grouped(Parts(new Group("A", 3), new Group("B", 2)), nameof(Part.Kind)), new StackPanel());

        Lay(list, 200, 1000);

        Assert.Multiple(() =>
        {
            Assert.That(HeaderBounds(list, 0).Y, Is.EqualTo(0));
            Assert.That(ItemBounds(list, 0).Y, Is.EqualTo(Header));
            Assert.That(ItemBounds(list, 2).Y, Is.EqualTo(Header + 2 * 52));
            Assert.That(HeaderBounds(list, 1).Y, Is.EqualTo(Header + 3 * 52));
            Assert.That(ItemBounds(list, 3).Y, Is.EqualTo(2 * Header + 3 * 52));
        });
    }

    [Test]
    public void AStackThatDoesNotVirtualize_StacksHeadersAndItemsAtTheirOwnHeights()
    {
        var list = List(Grouped(Parts(new Group("A", 2), new Group("B", 1)), nameof(Part.Kind)), new StackPanel { IsVirtualizing = false },
            headerHeight: 30);

        Lay(list, 200, 1000);

        Assert.Multiple(() =>
        {
            Assert.That(HeaderBounds(list, 0).Y, Is.EqualTo(0));
            Assert.That(ItemBounds(list, 0).Y, Is.EqualTo(30));
            Assert.That(HeaderBounds(list, 1).Y, Is.EqualTo(30 + 2 * 52));
            Assert.That(ItemBounds(list, 2).Y, Is.EqualTo(60 + 2 * 52));
        });
    }

    [Test]
    public void MoreLevels_PutAnOuterHeaderBeforeTheFirstInnerOne()
    {
        var parts = new[] { new Part("Box", "Collider"), new Part("Body", "Physics"), new Part("Ray", "Collider") };
        var view = new CollectionView(parts.ToList());
        view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(Part.Kind)));
        view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(Part.Name)));
        var list = List(view, new StackPanel());

        Lay(list, 200, 1000);

        Assert.Multiple(() =>
        {
            Assert.That(list.GroupHeaderPlaces.Select(place => $"{place.Level} before {place.BeforeItem}"),
                Is.EqualTo(new[] { "0 before 0", "1 before 0", "1 before 1", "0 before 2", "1 before 2" }));
            Assert.That(HeaderBounds(list, 1).Y, Is.EqualTo(Header), "the inner header under the outer one");
            Assert.That(ItemBounds(list, 0).Y, Is.EqualTo(2 * Header));
        });
    }

    [Test]
    public void WithoutAGroupStyle_TheItemsStandAlone()
    {
        var list = List(Grouped(Parts(new Group("A", 3), new Group("B", 2)), nameof(Part.Kind)), new StackPanel());
        list.GroupStyle.Clear();

        Lay(list, 200, 1000);

        Assert.Multiple(() =>
        {
            Assert.That(list.GroupHeaderPlaces, Is.Empty);
            Assert.That(list.GroupHeaderGenerator.RealizedPlaces, Is.Empty);
            Assert.That(ItemBounds(list, 3).Y, Is.EqualTo(3 * 52));
        });
    }

    [Test]
    public void AnItemAddedToAGroup_MovesTheGroupsAfterIt()
    {
        var parts = new TrackingCollection<Part>(Parts(new Group("Physics", 5), new Group("Joints", 1)));
        var view = new CollectionView(parts);
        view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(Part.Kind)));
        var list = List(view, new WrapPanel { ItemWidth = Tile, ItemHeight = Tile });
        Lay(list, 300, 1000);
        var before = HeaderBounds(list, 1).Y;

        parts.Add(new Part("Physics 5", "Physics"));
        Lay(list, 300, 1000);

        Assert.Multiple(() =>
        {
            Assert.That(before, Is.EqualTo(Header + Tile));
            Assert.That(HeaderBounds(list, 1).Y, Is.EqualTo(Header + 2 * Tile), "the sixth physics tile opened a line");
            Assert.That(list.GroupHeaderPlaces[1].BeforeItem, Is.EqualTo(6));
        });
    }

    // A folded group shows its header alone: its tiles take no line and are not made, and the next group comes up.
    [Test]
    public void AFoldedGroup_ShowsItsHeaderAlone()
    {
        var list = List(Grouped(Parts(new Group("Physics", 7), new Group("Joints", 5)), nameof(Part.Kind)),
            new WrapPanel { ItemWidth = Tile, ItemHeight = Tile });
        Lay(list, 300, 1000);

        list.GroupHeaderGenerator.HeaderAt(0).IsExpanded = false;
        Lay(list, 300, 1000);

        Assert.Multiple(() =>
        {
            Assert.That(list.GroupHeaderPlaces[0].IsExpanded, Is.False);
            Assert.That(HeaderBounds(list, 1).Y, Is.EqualTo(Header), "the joints right under the physics' header");
            Assert.That(ItemBounds(list, 7).Y, Is.EqualTo(2 * Header));
            Assert.That(list.ItemContainerGenerator.ContainerFromIndex(0), Is.Null, "a folded group's tiles are not made");
            Assert.That(list.ItemsHostPanel.DesiredSize.Height, Is.EqualTo(2 * Header + Tile).Or.EqualTo(1000));
        });
    }

    // A folded group between two open ones: the window spans it, and still none of its tiles is made - in a wrap panel
    // and in a stack. They lay under its header before.
    [TestCase(true)]
    [TestCase(false)]
    public void AFoldedGroupBetweenOpenOnes_MakesNoneOfItsItems(bool wrap)
    {
        Panel panel = wrap ? new WrapPanel { ItemWidth = Tile, ItemHeight = Tile } : new StackPanel();
        var list = List(Grouped(Parts(new Group("Stars", 1), new Group("Planets", 8), new Group("Moons", 5)),
            nameof(Part.Kind)), panel);
        Lay(list, 300, 1000);

        list.GroupHeaderGenerator.HeaderAt(1).IsExpanded = false;
        Lay(list, 300, 1000);

        Assert.Multiple(() =>
        {
            for (var index = 1; index <= 8; index++)
            {
                Assert.That(list.ItemContainerGenerator.ContainerFromIndex(index), Is.Null, $"planet {index}");
            }

            Assert.That(list.ItemContainerGenerator.ContainerFromIndex(0), Is.Not.Null);
            Assert.That(list.ItemContainerGenerator.ContainerFromIndex(9), Is.Not.Null);
            Assert.That(panel.VisualChildren.OfType<ContentPresenter>().Count(child => child.Visibility == Visibility.Visible),
                Is.EqualTo(6), "the star and the moons");
        });
    }

    // Folded is the group's, by its name: the groups made again after a change of the items keep it.
    [Test]
    public void AFoldedGroup_StaysFolded_AsTheItemsChange()
    {
        var parts = new TrackingCollection<Part>(Parts(new Group("Physics", 3), new Group("Joints", 2)));
        var view = new CollectionView(parts);
        view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(Part.Kind)));
        var list = List(view, new StackPanel());
        Lay(list, 200, 1000);
        list.GroupHeaderGenerator.HeaderAt(1).IsExpanded = false;
        Lay(list, 200, 1000);

        parts.Add(new Part("Physics 3", "Physics"));
        Lay(list, 200, 1000);

        Assert.Multiple(() =>
        {
            Assert.That(list.GroupHeaderPlaces[1].IsExpanded, Is.False);
            Assert.That(list.GroupHeaderGenerator.HeaderAt(1).IsExpanded, Is.False, "the header shows it");
            Assert.That(list.GroupHeaderPlaces[0].IsExpanded, Is.True);
        });
    }

    // A level that folds says so on its headers, and automation folds and opens them; one that does not stays open.
    [Test]
    public void AHeaderFolds_OnlyWhereItsGroupStyleLetsIt()
    {
        var fixedList = List(Grouped(Parts(new Group("Physics", 3), new Group("Joints", 2)), nameof(Part.Kind)),
            new StackPanel());
        Lay(fixedList, 200, 1000);
        var fixedHeader = fixedList.GroupHeaderGenerator.HeaderAt(0);
        var toggledFixed = fixedHeader.Toggle();
        var fixedPattern = fixedHeader.GetAutomationPeer().GetPattern(PatternId.ExpandCollapse);

        var list = List(Grouped(Parts(new Group("Physics", 3), new Group("Joints", 2)), nameof(Part.Kind)),
            new StackPanel(), canCollapse: true);
        Lay(list, 200, 1000);
        var header = list.GroupHeaderGenerator.HeaderAt(1);
        var folding = (IExpandCollapseProvider)header.GetAutomationPeer().GetPattern(PatternId.ExpandCollapse);
        folding.Collapse();
        Lay(list, 200, 1000);

        Assert.Multiple(() =>
        {
            Assert.That(toggledFixed, Is.False);
            Assert.That(fixedHeader.IsExpanded, Is.True);
            Assert.That(fixedPattern, Is.Null, "nothing to fold");
            Assert.That(header.CanCollapse, Is.True);
            Assert.That(folding.ExpandCollapseState, Is.EqualTo(ExpandCollapseState.Collapsed));
            Assert.That(list.GroupHeaderPlaces[1].IsExpanded, Is.False);
            Assert.That(list.ItemContainerGenerator.ContainerFromIndex(3), Is.Null, "the joints are folded away");
        });
    }

    // ---- Drops: a slot is an index AND a group; the gap opens in that group alone ----------------------------------

    private static DropSlot SlotAt(ItemsControl list, double x, double y)
    {
        Assert.That(((IDropGapPanel)list.ItemsHostPanel).TryGetDropSlot(new Vector2<double>(x, y), null, out var slot),
            Is.True);
        return slot;
    }

    private static void OpenGap(ItemsControl list, DropSlot slot, double width = 300)
    {
        ((IDropGapPanel)list.ItemsHostPanel).ShowDropGap(slot, null);
        Lay(list, width, 1000);
    }

    private static object NameOf(DropSlot slot) => slot.Group?.Name;

    // Five tiles a line: the physics' lines at 24 and 80, the joints' header at 136, their line at 160.
    private static ItemsControl Tiles(params Group[] groups)
    {
        var list = List(Grouped(Parts(groups), nameof(Part.Kind)), new WrapPanel { ItemWidth = Tile, ItemHeight = Tile });
        list.DropPlaceholderTemplate = new DataTemplate(() => new TemplateResult { RootComponent = new Border { Tag = "gap" } });
        Lay(list, 300, 1000);
        return list;
    }

    private static Rect GapBounds(ItemsControl list) =>
        list.ItemsHostPanel.VisualChildren.OfType<Border>().Single(border => Equals(border.Tag, "gap")).Bounds;

    // The physics' end and the joints' start are both index 7: past the physics' last tile is the one, the joints' header
    // the other.
    [Test]
    public void AGroupsEnd_AndTheNextGroupsStart_AreTwoSlots()
    {
        var list = Tiles(new Group("Physics", 7), new Group("Joints", 5));

        var end = SlotAt(list, 4 * Tile + 10, Header + Tile + 10);
        var start = SlotAt(list, 10, Header + 2 * Tile + 10);
        var onATile = SlotAt(list, Tile + 10, Header + 10);

        Assert.Multiple(() =>
        {
            Assert.That((end.Index, NameOf(end), end.GroupStart, end.GroupEnd), Is.EqualTo((7, "Physics", 0, 7)));
            Assert.That((start.Index, NameOf(start), start.GroupStart, start.GroupEnd), Is.EqualTo((7, "Joints", 7, 12)));
            Assert.That((onATile.Index, NameOf(onATile)), Is.EqualTo((1, "Physics")));
        });
    }

    // A gap at the physics' end is a cell after their last tile; at the joints' start it moves the joints' tiles along -
    // and the physics' not at all.
    [Test]
    public void TheGap_OpensInItsOwnGroup()
    {
        var list = Tiles(new Group("Physics", 7), new Group("Joints", 5));
        var end = SlotAt(list, 4 * Tile + 10, Header + Tile + 10);
        var start = SlotAt(list, 10, Header + 2 * Tile + 10);

        OpenGap(list, end);
        var gapAtEnd = GapBounds(list);
        var jointAtEnd = ItemBounds(list, 7);
        OpenGap(list, start);

        Assert.Multiple(() =>
        {
            Assert.That(gapAtEnd, Is.EqualTo(new Rect(2 * Tile, Header + Tile, Tile, Tile)), "after the physics' 7th");
            Assert.That(jointAtEnd, Is.EqualTo(new Rect(0, 2 * Header + 2 * Tile, Tile, Tile)), "the joints stay");
            Assert.That(GapBounds(list), Is.EqualTo(new Rect(0, 2 * Header + 2 * Tile, Tile, Tile)), "the joints' first cell");
            Assert.That(ItemBounds(list, 7).X, Is.EqualTo(Tile), "the first joint moves along");
            Assert.That(ItemBounds(list, 6), Is.EqualTo(new Rect(Tile, Header + Tile, Tile, Tile)), "the physics stay");
        });
    }

    // A gap in a full group gives it one more line, and the groups after it move down by that line.
    [Test]
    public void AGapInAFullGroup_MovesTheGroupsAfterItDown()
    {
        var list = Tiles(new Group("Physics", 10), new Group("Joints", 5));
        var before = HeaderBounds(list, 1).Y;

        OpenGap(list, SlotAt(list, 10, Header + 10));

        Assert.Multiple(() =>
        {
            Assert.That(before, Is.EqualTo(Header + 2 * Tile));
            Assert.That(ItemBounds(list, 9), Is.EqualTo(new Rect(0, Header + 2 * Tile, Tile, Tile)), "the tenth, wrapped");
            Assert.That(HeaderBounds(list, 1).Y, Is.EqualTo(Header + 3 * Tile));
            Assert.That(ItemBounds(list, 10).Y, Is.EqualTo(2 * Header + 3 * Tile), "the joints' tiles with them");
        });
    }

    // The point the gap opened under answers the same slot with the gap open: the gap does not chase itself.
    [Test]
    public void TheGapsOwnCell_AnswersTheSameSlot()
    {
        var list = Tiles(new Group("Physics", 7), new Group("Joints", 5));
        var points = new[]
        {
            new Vector2<double>(10, Header + 10), new Vector2<double>(2 * Tile + 10, Header + Tile + 10),
            new Vector2<double>(Tile + 10, 2 * Header + 2 * Tile + 10),
        };
        foreach (var point in points)
        {
            var slot = SlotAt(list, point.X, point.Y);
            OpenGap(list, slot);
            var again = SlotAt(list, point.X, point.Y);

            Assert.That((again.Index, NameOf(again)), Is.EqualTo((slot.Index, NameOf(slot))), $"at {point}");
        }
    }

    // A folded group takes a drop on its header, at its end; an outer header passes it to its first inner group.
    [Test]
    public void OnAHeader_AFoldedGroupTakesTheDropAtItsEnd_AnOuterOneAtItsFirstInnerGroup()
    {
        var folded = Tiles(new Group("Physics", 7), new Group("Joints", 5));
        folded.GroupHeaderGenerator.HeaderAt(0).IsExpanded = false;
        Lay(folded, 300, 1000);

        var parts = new[] { new Part("Box", "Collider"), new Part("Ray", "Collider"), new Part("Body", "Physics") };
        var view = new CollectionView(parts.ToList());
        view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(Part.Kind)));
        view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(Part.Name)));
        var nested = List(view, new StackPanel());
        Lay(nested, 200, 1000);

        var onFolded = SlotAt(folded, 10, 10);
        var onOuter = SlotAt(nested, 10, 10);

        // The last group folded: below its header, past all the lines, is still its end.
        var lastFolded = Tiles(new Group("Physics", 7), new Group("Joints", 5));
        lastFolded.GroupHeaderGenerator.HeaderAt(1).IsExpanded = false;
        Lay(lastFolded, 300, 1000);
        var belowTheLast = SlotAt(lastFolded, 10, 2 * Header + 2 * Tile + 10);

        Assert.Multiple(() =>
        {
            Assert.That((belowTheLast.Index, NameOf(belowTheLast)), Is.EqualTo((12, "Joints")));
            Assert.That((onFolded.Index, NameOf(onFolded)), Is.EqualTo((7, "Physics")));
            Assert.That((onOuter.Index, NameOf(onOuter)), Is.EqualTo((0, "Box")));
        });
    }

    // In a stack, a gap is a row of its group: the rows after it in that group move down by one, the next header too.
    [Test]
    public void AStacksGap_IsARowOfItsGroup()
    {
        var list = List(Grouped(Parts(new Group("Physics", 3), new Group("Joints", 2)), nameof(Part.Kind)),
            new StackPanel());
        Lay(list, 200, 1000);
        var row = ItemBounds(list, 1).Height;

        var slot = SlotAt(list, 10, Header + row + 10);
        OpenGap(list, slot, 200);

        Assert.Multiple(() =>
        {
            Assert.That((slot.Index, NameOf(slot)), Is.EqualTo((1, "Physics")));
            Assert.That(ItemBounds(list, 0).Y, Is.EqualTo(Header));
            Assert.That(ItemBounds(list, 1).Y, Is.EqualTo(Header + 2 * row));
            Assert.That(HeaderBounds(list, 1).Y, Is.EqualTo(Header + 4 * row));
            Assert.That(ItemBounds(list, 3).Y, Is.EqualTo(2 * Header + 4 * row), "the joints follow");
        });
    }

    // Automation reads a list as it shows: each header before its group's items, a folded group by its header alone -
    // in a list of items, and in a plain items control alike.
    [Test]
    public void Automation_ReadsEachHeaderBeforeItsGroup_AFoldedOneAlone()
    {
        string Read(ItemsControl list) => string.Join(" ", list.GetAutomationPeer().GetChildren()
            .Select(peer => peer.ControlType == AutomationControlType.Group ? $"[{peer.Name}]" : "item"));

        var parts = Parts(new Group("Physics", 2), new Group("Joints", 1));
        var plain = List(Grouped(parts, nameof(Part.Kind)), new StackPanel(), canCollapse: true);
        AutomationProperties.SetName(plain, "Parts");
        var listBox = new ListBox
        {
            Template = plain.Template,
            ItemsPanel = new ItemsPanelTemplate(() => new TemplateResult { RootComponent = new StackPanel() }),
            ItemsSource = Grouped(parts, nameof(Part.Kind)),
        };
        listBox.GroupStyle.Add(plain.GroupStyle[0]);
        plain.ItemTemplate = new DataTemplate(() =>
        {
            var tile = new Border { Width = 52, Height = 52 };
            AutomationProperties.SetName(tile, "tile");
            return new TemplateResult { RootComponent = tile };
        });
        Lay(plain, 200, 1000);
        Lay(listBox, 200, 1000);
        var plainOpen = Read(plain);
        var listOpen = Read(listBox);

        listBox.GroupHeaderGenerator.HeaderAt(0).Toggle();
        Lay(listBox, 200, 1000);

        Assert.Multiple(() =>
        {
            Assert.That(plainOpen, Is.EqualTo("[Physics] [tile] [tile] [Joints] [tile]"));
            Assert.That(listOpen, Is.EqualTo("[Physics] item item [Joints] item"));
            Assert.That(Read(listBox), Is.EqualTo("[Physics] [Joints] item"));
        });
    }

    // A stack that does not virtualize opens no gap, but tells the group: the upper half of a row is before it, the lower
    // after it; the next group's header is its start.
    [Test]
    public void AStackThatDoesNotVirtualize_TellsTheGroupOfADrop()
    {
        var list = List(Grouped(Parts(new Group("Physics", 2), new Group("Joints", 1)), nameof(Part.Kind)),
            new StackPanel { IsVirtualizing = false });
        Lay(list, 200, 1000);
        var row = ItemBounds(list, 0).Height;

        var upper = SlotAt(list, 10, Header + 5);
        var lower = SlotAt(list, 10, Header + 2 * row - 5);
        var nextHeader = SlotAt(list, 10, Header + 2 * row + 5);

        Assert.Multiple(() =>
        {
            Assert.That(((IDropGapPanel)list.ItemsHostPanel).SupportsDropGap, Is.False);
            Assert.That((upper.Index, NameOf(upper)), Is.EqualTo((0, "Physics")));
            Assert.That((lower.Index, NameOf(lower), lower.GroupEnd), Is.EqualTo((2, "Physics", 2)));
            Assert.That((nextHeader.Index, NameOf(nextHeader)), Is.EqualTo((2, "Joints")));
        });
    }

    // The arrows walk the tiles across the groups, past the headers: from a group's last tile on to the next group's
    // first, and down a column.
    [Test]
    public void TheArrows_WalkTheTilesAcrossTheGroups()
    {
        var list = List(Grouped(Parts(new Group("Physics", 7), new Group("Joints", 5)), nameof(Part.Kind)),
            new WrapPanel { ItemWidth = Tile, ItemHeight = Tile }, canCollapse: true);
        list.ItemTemplate = new DataTemplate(() => new TemplateResult { RootComponent = new Button { Width = 52, Height = 52 } });
        var root = new Border { Width = 300, Height = 1000, Child = list };
        for (var pass = 0; pass < 5; pass++)
        {
            Adamantium.UI.Extensions.WindowExtension.UpdateTree(root);
        }

        Button TileAt(int index) => (Button)list.ItemContainerGenerator.ContainerFromIndex(index).VisualChildren.First();
        FocusManager.Focus(TileAt(6));

        Assert.Multiple(() =>
        {
            Assert.That(KeyboardNavigation.Move(FocusNavigationDirection.Right), Is.True);
            Assert.That(FocusManager.Focused, Is.SameAs(TileAt(7)), "past the joints' header");
            Assert.That(KeyboardNavigation.Move(FocusNavigationDirection.Up), Is.True);
            Assert.That(FocusManager.Focused, Is.SameAs(TileAt(5)), "up the column, into the physics");
        });
    }

    // Two headers of one level that differ - one name wrapping - settle at the longer: the panel does not ask for another
    // measure on every pass.
    [Test]
    public void HeadersOfOneLevelThatDiffer_SettleAtTheLonger()
    {
        var list = List(Grouped(Parts(new Group("Physics", 3), new Group("Joints", 2)), nameof(Part.Kind)), new StackPanel());
        list.GroupStyle[0].HeaderTemplate = new DataTemplate(() =>
        {
            var header = new Border();
            header.SetBinding(Border.HeightProperty, new Adamantium.UI.Core.Data.Binding(nameof(CollectionViewGroup.Name))
            {
                Converter = new NameToHeight(),
            });
            return new TemplateResult { RootComponent = header };
        });
        var root = new Border { Width = 200, Height = 1000, Child = list };
        for (var pass = 0; pass < 6; pass++)
        {
            Adamantium.UI.Extensions.WindowExtension.UpdateTree(root);
        }

        Assert.Multiple(() =>
        {
            Assert.That(LayoutManager.For(list).IsSettled, Is.True, "no measure asked for again");
            Assert.That(HeaderBounds(list, 1).Y - HeaderBounds(list, 0).Y, Is.EqualTo(40 + 3 * ItemBounds(list, 0).Height));
        });
    }

    private sealed class NameToHeight : Adamantium.UI.Core.Data.ValueConverter
    {
        public override object Convert(object value, System.Type targetType, object parameter,
            System.Globalization.CultureInfo culture) => Equals(value, "Joints") ? 40.0 : 20.0;

        public override object ConvertBack(object value, System.Type targetType, object parameter,
            System.Globalization.CultureInfo culture) => throw new System.NotSupportedException();
    }

    // A folded group of a hundred thousand between two small open ones, all in view: a pass costs what the shown rows do,
    // not what the folded ones would.
    [TestCase(true)]
    [TestCase(false)]
    public void AHugeFoldedGroupInView_CostsLittle(bool wrap)
    {
        Panel MakePanel() => wrap ? new WrapPanel { ItemWidth = Tile, ItemHeight = Tile } : new StackPanel();
        var folded = List(Grouped(Parts(new Group("A", 10), new Group("B", 100_000), new Group("C", 10)), nameof(Part.Kind)),
            MakePanel());
        Lay(folded, 300, 2000);
        folded.GroupHeaderGenerator.HeaderAt(1).IsExpanded = false;
        Lay(folded, 300, 2000);
        var small = List(Grouped(Parts(new Group("A", 10), new Group("C", 10)), nameof(Part.Kind)), MakePanel());
        Lay(small, 300, 2000);

        var withFolded = PerPass(folded);
        var without = PerPass(small);
        TestContext.Out.WriteLine($"{(wrap ? "wrap" : "stack")}: {withFolded:F3} ms a pass with the folded group, {without:F3} without");

        Assert.That(withFolded, Is.LessThan(without * 3 + 0.5), "the folded rows cost next to nothing");
    }

    private static double PerPass(ItemsControl list)
    {
        const int passes = 50;
        var panel = list.ItemsHostPanel;
        var watch = Stopwatch.StartNew();
        for (var pass = 0; pass < passes; pass++)
        {
            panel.InvalidateMeasure();
            panel.Measure(new Size<double>(300, 2000));
        }

        return watch.Elapsed.TotalMilliseconds / passes;
    }

    // A folded header the drag rests on opens.
    [Test]
    public void ADragRestingOnAFoldedHeader_OpensIt()
    {
        var list = List(Grouped(Parts(new Group("Physics", 3)), nameof(Part.Kind)), new StackPanel(), canCollapse: true);
        Lay(list, 200, 1000);
        var header = list.GroupHeaderGenerator.HeaderAt(0);
        header.Toggle();

        ((ISpringLoadable)header).SpringLoad();

        Assert.That(list.GroupHeaderPlaces[0].IsExpanded, Is.True);
    }

    // Folding an outer group folds its inner ones with it: their headers take no place either.
    [Test]
    public void AFoldedOuterGroup_HidesItsInnerHeaders()
    {
        var parts = new[] { new Part("Box", "Collider"), new Part("Ray", "Collider"), new Part("Body", "Physics") };
        var view = new CollectionView(parts.ToList());
        view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(Part.Kind)));
        view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(Part.Name)));
        var list = List(view, new StackPanel());
        Lay(list, 200, 1000);

        list.GroupHeaderGenerator.HeaderAt(0).IsExpanded = false;
        Lay(list, 200, 1000);

        Assert.That(list.GroupHeaderPlaces.Select(place => $"{place.Level} {place.Group.Name}"),
            Is.EqualTo(new[] { "0 Collider", "0 Physics", "1 Body" }));
    }

    // Each level's header is as tall as its own template makes it.
    [Test]
    public void EachLevel_HasItsOwnHeaderHeight()
    {
        var parts = new[] { new Part("Box", "Collider"), new Part("Body", "Physics") };
        var view = new CollectionView(parts.ToList());
        view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(Part.Kind)));
        view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(Part.Name)));
        var list = List(view, new StackPanel(), headerHeight: 40);
        list.GroupStyle.Add(new GroupStyle
        {
            HeaderTemplate = new DataTemplate(() => new TemplateResult { RootComponent = new Border { Height = 20 } }),
        });
        Lay(list, 200, 1000);

        Assert.Multiple(() =>
        {
            Assert.That(HeaderBounds(list, 1).Y, Is.EqualTo(40), "the inner header under the 40 outer one");
            Assert.That(HeaderBounds(list, 1).Height, Is.EqualTo(20));
            Assert.That(ItemBounds(list, 0).Y, Is.EqualTo(60));
            Assert.That(HeaderBounds(list, 2).Y, Is.EqualTo(60 + 52), "the next outer header after the item");
        });
    }

    // An item joining another group is one Add: the containers of the rest stay as they were.
    [Test]
    public void AnItemJoiningAGroup_KeepsTheOtherContainers()
    {
        var parts = new TrackingCollection<Part>(Parts(new Group("Physics", 3), new Group("Joints", 2)));
        var view = new CollectionView(parts);
        view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(Part.Kind)));
        var list = List(view, new StackPanel());
        Lay(list, 200, 1000);
        var first = list.ItemContainerGenerator.ContainerFromIndex(0);

        parts.Add(new Part("Joints 2", "Joints"));
        Lay(list, 200, 1000);

        Assert.That(list.ItemContainerGenerator.ContainerFromIndex(0), Is.SameAs(first));
    }

    // The headers are the list's while they are shown: a list changed again and again holds no more than it shows.
    [Test]
    public void TheHeaders_AreLetGo_AsTheGroupsAreMadeAgain()
    {
        var parts = new TrackingCollection<Part>(Parts(new Group("A", 2), new Group("B", 2)));
        var view = new CollectionView(parts);
        view.SortDescriptions.Add(new SortDescription(nameof(Part.Name), SortDirection.Ascending));
        view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(Part.Kind)));
        var list = List(view, new StackPanel());
        Lay(list, 200, 1000);
        var held = list.LogicalChildren.OfType<GroupHeader>().Count();

        for (var change = 0; change < 10; change++)
        {
            // "0 ..." sorts before every name: each new group comes first, and the groups are made again.
            parts.Add(new Part($"0 {change}", $"New {change}"));
            parts.RemoveAt(parts.Count - 1);
            Lay(list, 200, 1000);
        }

        Assert.That(list.LogicalChildren.OfType<GroupHeader>().Count(), Is.LessThanOrEqualTo(held));
    }

    // A stack's rows of another height - a record's details - stand among the groups at their own height.
    [Test]
    public void AStacksTallerRows_StandAtTheirOwnHeight_AmongTheGroups()
    {
        var list = List(Grouped(Parts(new Group("A", 3), new Group("B", 2)), nameof(Part.Kind)), new TallerRows());

        Lay(list, 200, 1000);

        Assert.Multiple(() =>
        {
            Assert.That(ItemBounds(list, 1).Height, Is.EqualTo(52 + 30), "the second row has 30 more");
            Assert.That(ItemBounds(list, 2).Y, Is.EqualTo(Header + 2 * 52 + 30));
            Assert.That(HeaderBounds(list, 1).Y, Is.EqualTo(Header + 3 * 52 + 30));
        });
    }

    private sealed class TallerRows : StackPanel
    {
        protected override IReadOnlyList<(int Index, double Extra)> ItemExtentExceptions => [(1, 30.0)];
    }

    // Not virtualized, where an item stands is still known to whoever scrolls to it.
    [Test]
    public void AStackThatDoesNotVirtualize_TellsWhereAnItemStands()
    {
        var list = List(Grouped(Parts(new Group("A", 2), new Group("B", 2)), nameof(Part.Kind)),
            new StackPanel { IsVirtualizing = false });
        Lay(list, 200, 1000);

        var known = ((VirtualizingPanel)list.ItemsHostPanel).TryGetItemRect(2, out var rect);

        Assert.Multiple(() =>
        {
            Assert.That(known, Is.True);
            Assert.That(rect.Y, Is.EqualTo(2 * Header + 2 * 52));
        });
    }

    // Only the headers of the lines in view are made, as only those tiles are.
    [Test]
    public void AScrolledGroupedList_MakesOnlyTheHeadersInView()
    {
        var groups = Enumerable.Range(0, 50).Select(group => new Group($"G{group:00}", 20)).ToArray();
        var list = List(Grouped(Parts(groups), nameof(Part.Kind)), new WrapPanel { ItemWidth = Tile, ItemHeight = Tile });

        Lay(list, 300, 400);

        Assert.Multiple(() =>
        {
            Assert.That(list.GroupHeaderGenerator.RealizedPlaces.Count, Is.LessThan(5));
            Assert.That(list.ItemContainerGenerator.RealizedCount, Is.LessThan(60));
        });
    }

    // The groups cost the panel arithmetic over the groups, not a walk over the items: laying out 10 000 tiles in 50 groups
    // takes about what 10 000 tiles alone take.
    [Test]
    public void LayingOutGroups_CostsAboutWhatTheTilesAloneCost()
    {
        var groups = Enumerable.Range(0, 50).Select(group => new Group($"G{group:00}", 200)).ToArray();
        var grouped = List(Grouped(Parts(groups), nameof(Part.Kind)), new WrapPanel { ItemWidth = Tile, ItemHeight = Tile });
        var alone = List(new CollectionView(Parts(groups)), new WrapPanel { ItemWidth = Tile, ItemHeight = Tile });
        alone.GroupStyle.Clear();
        Lay(grouped, 600, 800);
        Lay(alone, 600, 800);

        var groupedTime = Time(grouped);
        var aloneTime = Time(alone);
        TestContext.Out.WriteLine($"10 000 tiles: in 50 groups {groupedTime:0.00} ms a pass, alone {aloneTime:0.00} ms a pass");

        Assert.That(groupedTime, Is.LessThan(aloneTime * 3 + 1));
    }

    // A pass of the panel itself: measured and arranged again at its size, as a scroll or a change of the items does.
    private static double Time(ItemsControl list)
    {
        const int passes = 50;
        var panel = list.ItemsHostPanel;
        var size = panel.RenderSize;
        var watch = Stopwatch.StartNew();
        for (var pass = 0; pass < passes; pass++)
        {
            panel.InvalidateMeasure();
            panel.Measure(size);
            panel.InvalidateArrange();
            panel.Arrange(new Rect(0, 0, size.Width, size.Height));
        }

        return watch.Elapsed.TotalMilliseconds / passes;
    }
}
