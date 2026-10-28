using System.Collections.Generic;
using System.Linq;
using Adamantium.Mathematics;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Decorators;
using Adamantium.UI.Controls.Panels;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Templates;
using NUnit.Framework;

namespace Adamantium.UITests;

/// <summary>Which variant each group is drawn at as the tab gets narrower. The answer must come from the ARRANGE slot,
/// must degrade in steps rather than clipping, and must not change its mind twice about the same width.</summary>
[TestFixture]
public class RibbonAdaptiveLayoutTests
{
    // A command whose width is decided by the size the group gave it - what a real command's Ribbon.Size triggers do.
    private sealed class SizedCommand : Border
    {
        /// <summary>Medium is the icon and the label on ONE line, so a long label makes a command wider drawn medium
        /// than drawn large. Measured on the live ribbon: "Select none" costs 84 medium against 66 large.</summary>
        public bool LongLabel { get; init; }

        private double _extra;

        /// <summary>Added at every size: a label rewritten longer or shorter, as another language does.</summary>
        public double Extra
        {
            get => _extra;
            set
            {
                _extra = value;
                InvalidateMeasure();
            }
        }

        protected override Size<double> MeasureOverride(Size<double> availableSize)
        {
            Width = _extra + Ribbon.GetSize(this) switch
            {
                RibbonSize.Large => 60,
                RibbonSize.Medium => LongLabel ? 90 : 40,
                _ => 20
            };
            Height = 60;
            return base.MeasureOverride(availableSize);
        }
    }

    /// <summary>What the theme wraps a group's commands in: padding, and the rule down its right edge. A group laid out
    /// at what its COMMANDS cost has nowhere to put this, and the rule walks back into the commands.</summary>
    private const double Chrome = 15;   // 6 + 6 padding, 1 rule, 1 + 1 rule margin

    private static RibbonGroup Group(int commands, int shrinkPriority = 0, bool fixedSize = false, double caption = 0)
    {
        var group = new RibbonGroup
        {
            ShrinkPriority = shrinkPriority,
            ItemsPanel = new ItemsPanelTemplate(() => new TemplateResult { RootComponent = new RibbonGroupPanel() }),
            Template = new ControlTemplate(() =>
            {
                var outer = new Grid();
                outer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                outer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var presenter = new ItemsPresenter { Margin = new Thickness(6, 6, 6, 3) };
                var rule = new Adamantium.UI.Controls.Shapes.Rectangle
                {
                    Width = 1,
                    Margin = new Thickness(1, 8, 1, 8),
                    VerticalAlignment = VerticalAlignment.Stretch
                };
                Grid.SetColumn(rule, 1);

                outer.Children.Add(presenter);
                outer.Children.Add(rule);

                // The group's caption, under the commands - wider than they are once they shrink.
                if (caption > 0)
                {
                    outer.Children.Add(new Border { Width = caption, Height = 1, Margin = new Thickness(6, 0, 6, 0) });
                }

                var result = new TemplateResult { RootComponent = outer };
                result.RegisterName("PART_ItemsPresenter", presenter);
                return result;
            })
        };

        for (var i = 0; i < commands; i++)
        {
            var command = new SizedCommand();
            if (fixedSize)
            {
                Ribbon.SetCollapseToMedium(command, RibbonCollapseThreshold.Never);
                Ribbon.SetCollapseToSmall(command, RibbonCollapseThreshold.Never);
            }

            group.Items.Add(command);
        }

        return group;
    }

    /// <summary>The shape whose ladder does NOT narrow step by step: one large command beside long-labeled mediums.
    /// Drop the large one and the run gains a column instead of losing width - measured on the live ribbon's "Scene"
    /// group, whose rungs cost 165, 183, 183.</summary>
    private static RibbonGroup NonMonotonicGroup()
    {
        var group = Group(0);
        group.Items.Add(new SizedCommand());

        for (var i = 0; i < 3; i++)
        {
            var command = new SizedCommand { LongLabel = true };
            Ribbon.SetMaxSize(command, RibbonSize.Medium);
            Ribbon.SetCollapseToSmall(command, RibbonCollapseThreshold.Never);
            group.Items.Add(command);
        }

        return group;
    }

    // The same commands, written out or given as data through ItemsSource + ItemTemplate.
    private static RibbonGroup Written(int commands, RibbonSize max)
    {
        var group = Group(0);
        for (var i = 0; i < commands; i++)
        {
            var command = new SizedCommand();
            Ribbon.SetMaxSize(command, max);
            group.Items.Add(command);
        }

        return group;
    }

    private static RibbonGroup FromData(int commands, RibbonSize max)
    {
        var group = Group(0);
        group.ItemTemplate = new DataTemplate(() =>
        {
            var command = new SizedCommand();
            Ribbon.SetMaxSize(command, max);
            return new TemplateResult { RootComponent = command };
        });
        group.ItemsSource = Enumerable.Range(0, commands).Select(i => $"command {i}").ToList();
        return group;
    }

    private static (RibbonGroupsPanel Panel, RibbonGroup[] Groups) Row(params RibbonGroup[] groups)
    {
        var panel = new RibbonGroupsPanel();
        foreach (var group in groups) panel.Children.Add(group);
        return (panel, groups);
    }

    private static void LayoutAt(RibbonGroupsPanel panel, double width)
    {
        panel.Measure(new Size<double>(double.PositiveInfinity, 106));
        panel.Arrange(new Rect(0, 0, width, 106));
    }

    private static IEnumerable<int> Variants(RibbonGroup[] groups) => groups.Select(g => g.CurrentVariant);

    private static double Occupied(RibbonGroup[] groups) => groups.Sum(g => g.WidthAt(g.CurrentVariant));

    // A command from data is the control its template builds, bound to its item - not a presenter around it, which took
    // the size the group chose and the quick-access menu and key, and kept them from the command.
    [Test]
    public void ACommandFromData_IsTheControlItsTemplateBuilds()
    {
        var (panel, groups) = Row(FromData(2, RibbonSize.Medium));
        LayoutAt(panel, 2000);
        var group = groups[0];

        var containers = Enumerable.Range(0, 2).Select(i => group.ItemContainerGenerator.ContainerFromIndex(i)).ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(containers, Is.All.InstanceOf<SizedCommand>());
            Assert.That(containers.Select(c => ((SizedCommand)c).DataContext), Is.EqualTo(group.ItemsSource));
            Assert.That(containers.Select(c => Ribbon.GetSize((SizedCommand)c)), Is.All.EqualTo(RibbonSize.Medium),
                "the roomiest variant draws a command no larger than its own MaxSize");
        });
    }

    // ...and the group costs and shrinks exactly as the same commands written out. Before, a tighter variant looked as
    // wide as the roomiest, and the group went from Medium straight to collapsed.
    [Test]
    public void AGroupFromData_ShrinksAsTheSameCommandsWrittenOut()
    {
        var (writtenPanel, written) = Row(Written(3, RibbonSize.Medium));
        var (dataPanel, data) = Row(FromData(3, RibbonSize.Medium));
        LayoutAt(writtenPanel, 2000);
        LayoutAt(dataPanel, 2000);

        var writtenWidths = Enumerable.Range(0, written[0].Variants.Count).Select(written[0].WidthAt).ToArray();
        var dataWidths = Enumerable.Range(0, data[0].Variants.Count).Select(data[0].WidthAt).ToArray();
        Assert.That(dataWidths, Is.EqualTo(writtenWidths), "every variant costs the same");

        foreach (var width in new double[] { 80, 60, 40, 30 })
        {
            LayoutAt(writtenPanel, width);
            LayoutAt(dataPanel, width);
            Assert.That(data[0].CurrentVariant, Is.EqualTo(written[0].CurrentVariant), $"the same variant at {width}");
        }
    }

    private sealed class OtherCommand : Border
    {
    }

    // Words become SizedCommands, numbers OtherCommands: two templates in one group, as a DataTemplateSet picks them.
    private sealed class ByKind : DataTemplateSelector
    {
        public readonly DataTemplate Words = new(() => new TemplateResult { RootComponent = new SizedCommand() });
        public readonly DataTemplate Numbers = new(() => new TemplateResult { RootComponent = new OtherCommand() });

        public override DataTemplate SelectTemplate(object item, AdamantiumComponent container) =>
            item is string ? Words : Numbers;
    }

    private static object ContainerOf(RibbonGroup group, int index) => group.ItemContainerGenerator.ContainerFromIndex(index);

    // A command given back by a removed item goes only to an item its template builds: handed to an item another
    // template draws, it showed that item as the wrong control.
    [Test]
    public void ARecycledCommand_GoesOnlyToAnItemItsTemplateBuilds()
    {
        var items = new System.Collections.ObjectModel.ObservableCollection<object> { "first", "second" };
        var group = Group(0);
        group.ItemTemplateSelector = new ByKind();
        group.ItemsSource = items;
        var (panel, _) = Row(group);
        LayoutAt(panel, 2000);

        items.RemoveAt(0);
        items.Add(3);
        LayoutAt(panel, 2000);

        Assert.That(ContainerOf(group, 1), Is.InstanceOf<OtherCommand>(), "the number is drawn by its own template");
    }

    // ...and it comes back without the item it served: neither its data nor the quick-access identity made for it.
    [Test]
    public void ARecycledCommand_ForgetsTheItemItServed()
    {
        var items = new System.Collections.ObjectModel.ObservableCollection<object> { "first" };
        var group = Group(0);
        group.ItemTemplate = new ByKind().Words;
        group.ItemsSource = items;
        var (panel, _) = Row(group);
        LayoutAt(panel, 2000);
        var firstKey = Ribbon.GetQuickAccessKey((SizedCommand)ContainerOf(group, 0));

        items.RemoveAt(0);
        items.Add("second");
        LayoutAt(panel, 2000);
        var command = (SizedCommand)ContainerOf(group, 0);

        Assert.Multiple(() =>
        {
            Assert.That(command.DataContext, Is.EqualTo("second"));
            Assert.That(Ribbon.GetQuickAccessKey(command), Is.Not.Null.And.Not.SameAs(firstKey),
                "a new item is a new command to the quick-access bar");
        });
    }

    // A slot switching between two templates takes back the command it showed before: a misfit parked on top of the
    // pool hid it, and every switch made one more command that was never let go.
    [Test]
    public void ASlotSwitchingTemplates_TakesBackItsCommand()
    {
        var items = new System.Collections.ObjectModel.ObservableCollection<object> { "word" };
        var group = Group(0);
        group.ItemTemplateSelector = new ByKind();
        group.ItemsSource = items;
        var (panel, _) = Row(group);
        LayoutAt(panel, 2000);
        var word = ContainerOf(group, 0);

        items[0] = 1;
        LayoutAt(panel, 2000);
        items[0] = "again";
        LayoutAt(panel, 2000);

        Assert.That(ContainerOf(group, 0), Is.SameAs(word));
    }

    // The identity the quick-access bar knows a command by stays with its item: rebuilt on a move, the command is still
    // the one in the bar, not a stranger the menu offers to add again.
    [Test]
    public void ACommandFromData_KeepsItsQuickAccessIdentityWhenRebuilt()
    {
        var items = new System.Collections.ObjectModel.ObservableCollection<object> { "first", "second" };
        var group = Group(0);
        group.ItemTemplate = new ByKind().Words;
        group.ItemsSource = items;
        var (panel, _) = Row(group);
        LayoutAt(panel, 2000);
        var key = Ribbon.GetQuickAccessKey((SizedCommand)ContainerOf(group, 0));

        items.Move(0, 1);
        LayoutAt(panel, 2000);

        Assert.That(Ribbon.GetQuickAccessKey((SizedCommand)ContainerOf(group, 1)), Is.Not.Null.And.SameAs(key));
    }

    // Written out, a command has no templated parent; built from data it must not get the group as one either, or the
    // focus ring of a one-command group climbed to the whole group.
    [Test]
    public void ACommandFromData_HasNoTemplatedParent()
    {
        var (panel, groups) = Row(FromData(1, RibbonSize.Large));
        LayoutAt(panel, 2000);

        Assert.That(((SizedCommand)ContainerOf(groups[0], 0)).TemplatedParent, Is.Null);
    }

    // Tools given as data are one of a set as written ones are: a tool looked for the others among the group's items,
    // which are data, found none, and a press left two tools checked.
    [Test]
    public void ToolsFromData_AreOneOfASet()
    {
        var group = Group(0);
        group.ItemTemplate = new DataTemplate(() =>
            new TemplateResult { RootComponent = new RibbonRadioButton { GroupName = "Tool" } });
        group.ItemsSource = new List<string> { "select", "move" };
        var (panel, _) = Row(group);
        LayoutAt(panel, 2000);
        var select = (RibbonRadioButton)ContainerOf(group, 0);
        var move = (RibbonRadioButton)ContainerOf(group, 1);
        select.IsChecked = true;

        move.PerformClick();

        Assert.Multiple(() =>
        {
            Assert.That(move.IsChecked, Is.True);
            Assert.That(select.IsChecked, Is.False, "the tool picked before stayed checked");
        });
    }

    // A tool given back by a removed item comes back unchecked, as one built for the new item would: it kept the press
    // of the item it served, and the new tool showed checked beside the one picked since.
    [Test]
    public void ARecycledTool_ForgetsThePressOfTheItemItServed()
    {
        var items = new System.Collections.ObjectModel.ObservableCollection<object> { "select", "move" };
        var group = Group(0);
        group.ItemTemplate = new DataTemplate(() =>
            new TemplateResult { RootComponent = new RibbonRadioButton { GroupName = "Tool" } });
        group.ItemsSource = items;
        var (panel, _) = Row(group);
        LayoutAt(panel, 2000);
        var select = (RibbonRadioButton)ContainerOf(group, 0);
        select.PerformClick();

        items.RemoveAt(0);
        ((RibbonRadioButton)ContainerOf(group, 0)).PerformClick();
        items.Add("rotate");
        LayoutAt(panel, 2000);

        Assert.Multiple(() =>
        {
            Assert.That(ContainerOf(group, 1), Is.SameAs(select), "the removed item's tool is the one given back");
            Assert.That(select.IsChecked, Is.False, "the new tool came back checked");
            Assert.That(((RibbonRadioButton)ContainerOf(group, 0)).IsChecked, Is.True);
        });
    }

    // The width a group is laid out at has to be the GROUP's, not its commands'. Missing the chrome, every group is
    // arranged narrower than it needs and the rule down its edge is pushed back over the commands - which is what the
    // separators "sliding" looked like on screen.
    [Test]
    public void AGroupIsArrangedAtItsOwnWidth_ChromeIncluded()
    {
        var (panel, groups) = Row(Group(2), Group(2));
        LayoutAt(panel, 2000);

        Assert.Multiple(() =>
        {
            Assert.That(groups[0].WidthAt(0), Is.EqualTo(groups[0].DesiredSize.Width),
                "the roomiest variant costs exactly what the group asked to be measured at");
            Assert.That(groups[0].Bounds.Width, Is.EqualTo(groups[0].DesiredSize.Width), "and that is what it got");
            Assert.That(groups[1].Bounds.X, Is.EqualTo(groups[0].Bounds.Width), "so the next group starts after it");
        });
    }

    // ...at every variant, not just the roomiest: the chrome does not shrink with the commands.
    [Test]
    public void TheChromeIsCountedAtEveryVariant()
    {
        var (panel, groups) = Row(Group(2));
        LayoutAt(panel, 2000);
        var group = groups[0];

        for (var i = 0; i < group.Variants.Count - 1; i++)
        {
            Assert.That(group.WidthAt(i), Is.GreaterThanOrEqualTo(Chrome),
                $"variant {i} has to leave room for the padding and the rule");
        }
    }

    // A group must be DRAWN at the variant it was LAID OUT at. Two owners for that number is what let a group keep
    // drawing its commands large while the row gave it the narrow slot - so the next group started inside it.
    [Test]
    public void AGroupIsDrawnAtTheVariantItWasLaidOutAt()
    {
        var (panel, groups) = Row(Group(3), Group(3));
        LayoutAt(panel, 2000);
        LayoutAt(panel, 260);

        foreach (var group in groups)
        {
            var expected = group.Variants[group.CurrentVariant].Sizes;
            var drawn = group.Items.Cast<IMeasurableComponent>().Select(Ribbon.GetSize).ToArray();

            Assert.That(drawn, Is.EqualTo(expected),
                $"group at variant {group.CurrentVariant} is drawing {string.Join(",", drawn)}");
        }
    }

    // ...and the groups therefore do not overlap: each starts where the one before it ended.
    [Test]
    public void GroupsDoNotOverlap_HoweverNarrowTheTab()
    {
        var (panel, groups) = Row(Group(3), Group(3), Group(2));

        foreach (var width in new[] { 2000.0, 600, 400, 260, 160, 400, 2000 })
        {
            LayoutAt(panel, width);

            for (var i = 1; i < groups.Length; i++)
            {
                Assert.That(groups[i].Bounds.X, Is.GreaterThanOrEqualTo(groups[i - 1].Bounds.X + groups[i - 1].Bounds.Width),
                    $"at width {width}, group {i} starts inside group {i - 1}");
            }
        }
    }

    // Nothing stays put away while there is room for it. The order groups give way in decides WHICH gives way; it never
    // leaves band standing empty beside a group shrunk or collapsed that would fit in it - a collapse frees more than
    // was needed, and a group further along may take that back. Swept both ways, as a window is dragged.
    [Test]
    public void NoGroupStaysShrunkWhileThereIsRoomForIt()
    {
        var (panel, groups) = Row(Group(9), NonMonotonicGroup(), Group(3, fixedSize: true), Group(9));
        LayoutAt(panel, 2000);
        var whole = (int)Occupied(groups);

        var widths = Enumerable.Range(0, (whole - 120) / 5).Select(i => whole - 5.0 * i).ToList();
        widths.AddRange(Enumerable.Reverse(widths));

        foreach (var width in widths)
        {
            LayoutAt(panel, width);

            foreach (var group in groups)
            {
                var room = width - (Occupied(groups) - group.WidthAt(group.CurrentVariant));
                for (var i = 0; i < group.CurrentVariant; i++)
                {
                    Assert.That(group.WidthAt(i), Is.GreaterThan(room),
                        $"at width {width} a group sits at variant {group.CurrentVariant} with room for variant {i}");
                }
            }
        }
    }

    // Whatever the tab does to a group, the group never ends up taking MORE room than it does drawn the way its author
    // asked. Ladders are not monotonic, so stepping down them blindly can park a group on a rung that costs more - and
    // collapsing off that rung compares against it, not against where the group started.
    [Test]
    public void ShrinkingAGroup_NeverMakesItWider()
    {
        var group = NonMonotonicGroup();
        group.CollapsedWidth = 180;
        var (panel, groups) = Row(group, Group(3));
        LayoutAt(panel, 2000);

        var roomiest = group.WidthAt(0);
        Assert.That(group.WidthAt(1), Is.GreaterThan(roomiest), "precondition: the rung below rung 0 costs MORE");

        for (var width = Occupied(groups); width >= 60; width -= 5)
        {
            LayoutAt(panel, width);

            Assert.That(group.WidthAt(group.CurrentVariant), Is.LessThanOrEqualTo(roomiest),
                $"at width {width} the group sits on rung {group.CurrentVariant} and is wider than it was whole");
        }
    }

    // Dragging a window narrower, the row at each width is the one a fresh row shows there, and what each variant costs
    // stays what it is. A group's chrome read off a measure that had not reached its commands misread every width, and
    // groups grew back and shrank again as the window only narrowed.
    [Test]
    public void SweepingTheWidth_GivesAtEachWidthWhatAFreshRowGives()
    {
        var (panel, groups) = Row(Group(3), Group(3), Group(2));
        LayoutAt(panel, 2000);
        var costs = groups.Select(g => Enumerable.Range(0, g.Variants.Count - 1).Select(g.WidthAt).ToArray()).ToArray();

        for (var width = 700.0; width >= 120; width -= 1)
        {
            LayoutAt(panel, width);

            var (fresh, freshGroups) = Row(Group(3), Group(3), Group(2));
            LayoutAt(fresh, 2000);
            LayoutAt(fresh, width);

            Assert.That(Variants(groups), Is.EqualTo(Variants(freshGroups)), $"at width {width}");
            for (var g = 0; g < groups.Length; g++)
            {
                var now = Enumerable.Range(0, costs[g].Length).Select(groups[g].WidthAt).ToArray();
                Assert.That(now, Is.EqualTo(costs[g]).Within(0.5), $"group {g}'s costs at width {width}");
            }
        }
    }

    // ...a group whose caption is wider than its smaller layouts too: its width there is the caption's, which no
    // constant added to what the commands cost says - read off the layout drawn, it came out different for each.
    [Test]
    public void AGroupWiderByItsCaption_CostsTheSameWhicheverLayoutIsDrawn()
    {
        var (panel, groups) = Row(Group(3), Group(2, caption: 70));
        LayoutAt(panel, 2000);
        var costs = Enumerable.Range(0, groups[1].Variants.Count - 1).Select(groups[1].WidthAt).ToArray();
        Assert.That(costs[^1], Is.EqualTo(costs[^2]).Within(0.5), "precondition: the caption holds the small layouts wide");

        for (var width = 400.0; width >= 120; width -= 1)
        {
            LayoutAt(panel, width);
            var now = Enumerable.Range(0, costs.Length).Select(groups[1].WidthAt).ToArray();
            Assert.That(now, Is.EqualTo(costs).Within(0.5), $"at width {width}");
        }
    }

    // A layout pass repeated at an unchanged width must not keep changing its mind - that is what a band twitching while
    // a window is dragged looks like from the outside.
    [Test]
    public void RepeatedPassesAtOneWidth_Settle()
    {
        var (panel, groups) = Row(Group(3), Group(3));
        LayoutAt(panel, 320);
        var first = groups.Select(g => g.CurrentVariant).ToArray();

        for (var pass = 0; pass < 8; pass++)
        {
            LayoutAt(panel, 320);
            Assert.That(groups.Select(g => g.CurrentVariant), Is.EqualTo(first), $"pass {pass} changed its mind");
        }
    }

    // With room to spare every group is drawn the way its author asked - shrinking is something the WIDTH does to the
    // tab, never a default.
    [Test]
    public void WithRoomToSpare_EveryGroupIsAtItsRoomiest()
    {
        var (panel, groups) = Row(Group(3), Group(3));
        LayoutAt(panel, 2000);

        Assert.That(Variants(groups), Is.All.EqualTo(0));
    }

    // The tab narrows and the layout DEGRADES instead of being clipped: something gives way, and what is left still
    // fits the slot.
    [Test]
    public void ANarrowerTab_ShrinksAGroupInsteadOfOverflowing()
    {
        var (panel, groups) = Row(Group(3), Group(3));
        LayoutAt(panel, 2000);
        var roomy = Occupied(groups);

        LayoutAt(panel, roomy - 60);

        Assert.Multiple(() =>
        {
            Assert.That(Variants(groups), Is.Not.All.EqualTo(0), "something had to give");
            Assert.That(Occupied(groups), Is.LessThanOrEqualTo(roomy - 60));
        });
    }

    // The END of a tab yields first: the group an author put first keeps its full layout longest.
    [Test]
    public void TheRightmostGroupGivesWayFirst()
    {
        var (panel, groups) = Row(Group(3), Group(3));
        LayoutAt(panel, 2000);

        LayoutAt(panel, Occupied(groups) - 20);

        Assert.Multiple(() =>
        {
            Assert.That(groups[0].CurrentVariant, Is.EqualTo(0), "the leading group is untouched");
            Assert.That(groups[1].CurrentVariant, Is.GreaterThan(0));
        });
    }

    // ShrinkPriority overrides that: an author names the group they are willing to lose room in.
    [Test]
    public void ShrinkPriority_DecidesWhichGroupGivesWay()
    {
        var (panel, groups) = Row(Group(3, shrinkPriority: -1), Group(3));
        LayoutAt(panel, 2000);

        LayoutAt(panel, Occupied(groups) - 20);

        Assert.Multiple(() =>
        {
            Assert.That(groups[0].CurrentVariant, Is.GreaterThan(0), "the one offered up goes first");
            Assert.That(groups[1].CurrentVariant, Is.EqualTo(0));
        });
    }

    // Room given back is taken back - the degradation is not one-way.
    [Test]
    public void WideningTheTab_RestoresWhatWasGivenUp()
    {
        var (panel, groups) = Row(Group(3), Group(3));
        LayoutAt(panel, 2000);
        var roomy = Occupied(groups);

        LayoutAt(panel, roomy - 60);
        Assert.That(Variants(groups), Is.Not.All.EqualTo(0), "precondition: something shrank");

        LayoutAt(panel, 2000);

        Assert.That(Variants(groups), Is.All.EqualTo(0));
    }

    // Narrow, widen A LITTLE, narrow again. The middle step grows a group back, and the third has to be able to take it
    // away again - a group that shrank once and then never shrinks again is stuck at a size the tab cannot hold.
    [Test]
    public void AGroupGrownBackByAWidening_ShrinksAgainWhenTheTabNarrows()
    {
        var (panel, groups) = Row(Group(3), Group(3));
        LayoutAt(panel, 2000);
        var roomy = Occupied(groups);

        LayoutAt(panel, roomy - 120);
        var tight = groups.Select(g => g.CurrentVariant).ToArray();
        Assert.That(tight, Is.Not.All.EqualTo(0), "precondition: something shrank");

        LayoutAt(panel, roomy - 60);   // a LITTLE wider, enough to grow something back
        LayoutAt(panel, roomy - 120);  // and back to where it was

        Assert.That(Variants(groups), Is.EqualTo(tight), "the same width has to give the same answer");
    }

    // ...and the width it settles at must be one the tab can actually hold.
    [Test]
    public void AfterAWidenAndNarrow_TheGroupsStillFit()
    {
        var (panel, groups) = Row(Group(3), Group(3));
        LayoutAt(panel, 2000);
        var roomy = Occupied(groups);

        LayoutAt(panel, roomy - 120);
        LayoutAt(panel, roomy - 60);
        LayoutAt(panel, roomy - 120);

        Assert.That(Occupied(groups), Is.LessThanOrEqualTo(roomy - 120));
    }

    // The same width twice has to give the same answer, whichever side it was approached from - within the deadband the
    // choice is allowed to differ, outside it, it is not.
    [Test]
    public void ASettledWidth_DoesNotChangeUnderRepeatedPasses()
    {
        var (panel, groups) = Row(Group(3), Group(3));
        LayoutAt(panel, 300);
        var settled = groups.Select(g => g.CurrentVariant).ToArray();

        for (var pass = 0; pass < 5; pass++) LayoutAt(panel, 300);

        Assert.That(Variants(groups), Is.EqualTo(settled));
    }

    // Out of size steps and still too wide: a group gives itself up entirely, and its commands go to the flyout its
    // button opens rather than off the edge of the tab. Commands that cannot shrink at all, so collapsing is the only
    // lever there is.
    [Test]
    public void OutOfSizeSteps_AGroupCollapsesRatherThanOverflowing()
    {
        var (panel, groups) = Row(Group(3, fixedSize: true), Group(3, fixedSize: true));
        LayoutAt(panel, 2000);

        LayoutAt(panel, 200);

        Assert.That(groups.Select(g => g.IsCollapsed), Has.Some.True);
    }

    // ...and the rightmost is the one to go, same rule as shrinking.
    [Test]
    public void TheRightmostGroupCollapsesFirst()
    {
        var (panel, groups) = Row(Group(3, fixedSize: true), Group(3, fixedSize: true));
        LayoutAt(panel, 2000);

        // Room for one group whole beside one collapsed button, and not for two whole ones.
        LayoutAt(panel, groups[0].WidthAt(0) + groups[1].CollapsedWidth + 6);

        Assert.Multiple(() =>
        {
            Assert.That(groups[1].IsCollapsed, Is.True);
            Assert.That(groups[0].IsCollapsed, Is.False, "the leading group keeps its commands");
        });
    }

    // Room given back un-collapses too - a group does not stay a button once the tab can hold it again.
    [Test]
    public void WideningTheTab_UncollapsesAGroup()
    {
        var (panel, groups) = Row(Group(3, fixedSize: true), Group(3, fixedSize: true));
        LayoutAt(panel, 2000);
        LayoutAt(panel, 200);
        Assert.That(groups.Select(g => g.IsCollapsed), Has.Some.True, "precondition: something collapsed");

        LayoutAt(panel, 2000);

        Assert.Multiple(() =>
        {
            Assert.That(groups.Select(g => g.IsCollapsed), Is.All.False);
            Assert.That(Variants(groups), Is.All.EqualTo(0));
        });
    }

    // A group hidden gives its room back at the SAME tab width: what shrank for want of it is drawn whole again. Waiting
    // for the tab to widen left the row shrunk beside empty space.
    [Test]
    public void AGroupHidden_GivesItsRoomBack()
    {
        var (panel, groups) = Row(Group(3), Group(3), Group(3));
        LayoutAt(panel, 2000);
        var width = groups[0].WidthAt(0) + groups[1].WidthAt(0) + 30;
        LayoutAt(panel, width);
        Assert.That(groups[1].CurrentVariant, Is.GreaterThan(0), "precondition: the middle group shrank too");

        groups[2].Visibility = Visibility.Collapsed;
        LayoutAt(panel, width);

        Assert.That(groups.Take(2).Select(g => g.CurrentVariant), Is.All.EqualTo(0));
    }

    // Commands drawn narrower - another language, a shorter label - give the row room the same way.
    [Test]
    public void CommandsDrawnNarrower_GiveTheRowRoomBack()
    {
        var (panel, groups) = Row(Group(3), Group(3));
        var commands = groups.SelectMany(g => g.Items.Cast<SizedCommand>()).ToArray();
        foreach (var command in commands) command.Extra = 20;
        LayoutAt(panel, 2000);
        var width = Occupied(groups) - 40;
        LayoutAt(panel, width);
        Assert.That(Variants(groups), Is.Not.All.EqualTo(0), "precondition: something shrank");

        foreach (var command in commands) command.Extra = 0;
        LayoutManager.For(panel).ExecuteLayoutPass();     // one frame, as the running app re-measures the labels
        LayoutAt(panel, width);

        Assert.That(Variants(groups), Is.All.EqualTo(0));
    }

    // ...a collapsed group's commands too, rewritten with everyone's (another language). Its widths were about the old
    // labels, it cannot be measured inside its flyout, and it stayed a button for good - the band switched to Russian
    // and back sat half empty.
    [Test]
    public void ACollapsedGroupWhoseCommandsGrewNarrower_IsDrawnWholeWhenItFits()
    {
        var (panel, groups) = Row(Group(3, fixedSize: true), Group(3, fixedSize: true));
        LayoutAt(panel, 2000);
        var width = groups[0].WidthAt(0) + groups[1].WidthAt(0) + 6;
        var commands = groups.SelectMany(g => g.Items.Cast<SizedCommand>()).ToArray();

        foreach (var command in commands) command.Extra = 40;
        LayoutManager.For(panel).ExecuteLayoutPass();
        LayoutAt(panel, width);
        Assert.That(groups[1].IsCollapsed, Is.True, "precondition: too wide, so it collapsed");

        foreach (var command in commands) command.Extra = 0;
        LayoutManager.For(panel).ExecuteLayoutPass();
        LayoutAt(panel, width);

        Assert.That(groups[1].IsCollapsed, Is.False);
    }

    // The choice is the width's and the content's alone, not the path there: a label misread wide for one pass (fonts
    // still arriving) collapsed a group, its room went to a neighbor, and the row never came back to what a fresh one
    // shows at that width.
    [Test]
    public void AfterCommandsWereWideForAMoment_TheRowIsAsAFreshOne()
    {
        var width = 0.0;
        int[] Fresh()
        {
            var (fresh, freshGroups) = Row(Group(9), NonMonotonicGroup(), Group(3, fixedSize: true), Group(9));
            LayoutAt(fresh, 2000);
            width = Occupied(freshGroups) - 150;
            LayoutAt(fresh, width);
            return freshGroups.Select(g => g.CurrentVariant).ToArray();
        }

        var expected = Fresh();
        var (panel, groups) = Row(Group(9), NonMonotonicGroup(), Group(3, fixedSize: true), Group(9));
        LayoutAt(panel, 2000);
        LayoutAt(panel, width);
        var commands = groups[1].Items.Cast<SizedCommand>().ToArray();

        foreach (var command in commands) command.Extra = 120;
        LayoutManager.For(panel).ExecuteLayoutPass();
        LayoutAt(panel, width);
        foreach (var command in commands) command.Extra = 0;
        LayoutManager.For(panel).ExecuteLayoutPass();
        LayoutAt(panel, width);

        Assert.That(Variants(groups), Is.EqualTo(expected));
    }

    // A collapsed group whose flyout is in use is left as it is while the row around it changes: opened, it closed the
    // flyout under the pointer, and with its widths forgotten it was counted as costing nothing.
    [Test]
    public void ACollapsedGroupWithItsFlyoutOpen_StaysCollapsed()
    {
        var (panel, groups) = Row(Group(3, fixedSize: true), Group(3, fixedSize: true));
        LayoutAt(panel, 2000);
        var width = groups[0].WidthAt(0) + groups[1].CollapsedWidth + 6;
        LayoutAt(panel, width);
        Assert.That(groups[1].IsCollapsed, Is.True, "precondition");
        groups[1].IsDropDownOpen = true;

        foreach (var command in groups[0].Items.Cast<SizedCommand>()) command.Extra = 1;
        LayoutManager.For(panel).ExecuteLayoutPass();
        LayoutAt(panel, width);

        Assert.Multiple(() =>
        {
            Assert.That(groups[1].IsCollapsed, Is.True);
            Assert.That(groups[1].IsDropDownOpen, Is.True);
        });
    }

    // A collapsed group's button names it on two lines when the name has several words, broken nearest the middle.
    [TestCase("Styles", "Styles")]
    [TestCase("Stylistic sets", "Stylistic\nsets")]
    [TestCase("Update style to match", "Update style\nto match")]
    [TestCase("Буфер обмена", "Буфер\nобмена")]
    [TestCase(" Font  size ", "Font  size")]
    public void ACollapsedHeader_IsTheNameOnTwoLines(string header, string shown)
    {
        Assert.That(new RibbonGroup { Header = header }.CollapsedHeader, Is.EqualTo(shown));
    }

    // ...but a header a template draws is handed to it as written: it may key or convert on it.
    [Test]
    public void AHeaderDrawnByATemplate_IsGivenAsWritten()
    {
        var group = new RibbonGroup { Header = "Font size" };
        group.HeaderTemplate = new DataTemplate(() => new TemplateResult { RootComponent = new Border() });

        Assert.That(group.CollapsedHeader, Is.EqualTo("Font size"));
    }

    // A group held collapsed while its flyout was in use is the row's to decide again once the flyout closes - room
    // freed meanwhile (a command in the flyout hid a group) is not left standing empty.
    [Test]
    public void AGroupHeldForItsFlyout_IsDecidedAgainWhenItCloses()
    {
        var a = Group(3, fixedSize: true);
        var c = Group(3, fixedSize: true);
        var b = Group(3, fixedSize: true);
        b.ShrinkSteps = RibbonGroupShrinkSteps.Collapse;
        var (panel, _) = Row(a, c, b);
        LayoutAt(panel, 2000);
        var width = a.WidthAt(0) + c.WidthAt(0) + b.CollapsedWidth + 6;
        LayoutAt(panel, width);
        Assert.That(b.IsCollapsed, Is.True, "precondition");

        b.IsDropDownOpen = true;
        c.Visibility = Visibility.Collapsed;
        LayoutManager.For(panel).ExecuteLayoutPass();
        Assert.That(b.IsCollapsed, Is.True, "held while its flyout is open");

        b.IsDropDownOpen = false;
        LayoutManager.For(panel).ExecuteLayoutPass();

        Assert.That(b.IsCollapsed, Is.False);
    }

    // A group shown again is laid out for the row as it is now, not left at the step it had when it was hidden.
    [Test]
    public void AGroupShownAgain_IsDrawnWholeWhenThereIsRoom()
    {
        var (panel, groups) = Row(Group(3, fixedSize: true), Group(3, fixedSize: true));
        LayoutAt(panel, 2000);
        LayoutAt(panel, groups[0].WidthAt(0) + groups[1].CollapsedWidth + 6);
        Assert.That(groups[1].IsCollapsed, Is.True, "precondition: the second group collapsed");

        groups[1].Visibility = Visibility.Collapsed;
        LayoutAt(panel, 2000);
        groups[1].Visibility = Visibility.Visible;
        LayoutAt(panel, 2000);

        Assert.Multiple(() =>
        {
            Assert.That(groups[1].IsCollapsed, Is.False);
            Assert.That(Variants(groups), Is.All.EqualTo(0));
        });
    }

    // ...and the choice settles: un-collapsing a group moves its commands, which must not set off another choice.
    [Test]
    public void TheRoomGivenBack_Settles()
    {
        var (panel, groups) = Row(Group(3, fixedSize: true), Group(3, fixedSize: true), Group(3, fixedSize: true));
        LayoutAt(panel, 2000);
        var width = groups[0].WidthAt(0) + groups[1].CollapsedWidth + groups[2].CollapsedWidth + 6;
        LayoutAt(panel, width);
        groups[2].Visibility = Visibility.Collapsed;
        LayoutAt(panel, width);
        var settled = groups.Take(2).Select(g => g.CurrentVariant).ToArray();

        for (var pass = 0; pass < 5; pass++)
        {
            LayoutAt(panel, width);
            Assert.That(groups.Take(2).Select(g => g.CurrentVariant), Is.EqualTo(settled), $"pass {pass} changed its mind");
        }
    }

    // A tab narrower than even the collapsed buttons still lays out and still answers - it runs out of variants, not out
    // of answers, and then the band scrolls.
    [Test]
    public void ATabTooNarrowForEvenTheButtons_StopsAtCollapsed()
    {
        var (panel, groups) = Row(Group(3, fixedSize: true), Group(3, fixedSize: true));

        Assert.DoesNotThrow(() => LayoutAt(panel, 10));
        Assert.That(groups.Select(g => g.IsCollapsed), Is.All.True);
    }

    // --- Which steps a group is ALLOWED to take (RibbonGroup.ShrinkSteps) --------------------------------------------

    // Pinned: the row gives way elsewhere, and once there is nowhere else it scrolls instead.
    [Test]
    public void AGroupWithNoStepsIsDrawnWholeHoweverNarrowTheTab()
    {
        var pinned = Group(3);
        pinned.ShrinkSteps = RibbonGroupShrinkSteps.None;
        var (panel, _) = Row(pinned, Group(3));

        LayoutAt(panel, 2000);
        var whole = pinned.WidthAt(0);
        LayoutAt(panel, 120);

        Assert.Multiple(() =>
        {
            Assert.That(pinned.CurrentVariant, Is.Zero);
            Assert.That(pinned.IsCollapsed, Is.False);
            Assert.That(pinned.Bounds.Width, Is.EqualTo(whole));
            Assert.That(panel.CanScrollForward, Is.True, "nowhere left to give way, so the row moves");
        });
    }

    // Sizes but no collapse: it narrows as far as its commands allow and never becomes a button.
    [Test]
    public void AGroupThatMayResizeButNotCollapse_NeverBecomesAButton()
    {
        var limited = Group(3);
        limited.ShrinkSteps = RibbonGroupShrinkSteps.Sizes;
        var (panel, _) = Row(limited, Group(3));

        LayoutAt(panel, 2000);
        LayoutAt(panel, 60);

        Assert.Multiple(() =>
        {
            Assert.That(limited.IsCollapsed, Is.False);
            Assert.That(limited.CurrentVariant, Is.GreaterThan(0), "it did give what it could");
        });
    }

    // Collapse but no sizes: whole, or a button, and nothing between.
    [Test]
    public void AGroupThatMayOnlyCollapse_SkipsStraightToTheButton()
    {
        var either = Group(3, fixedSize: true);
        either.ShrinkSteps = RibbonGroupShrinkSteps.Collapse;
        var (panel, _) = Row(either);

        LayoutAt(panel, 2000);
        LayoutAt(panel, 80);

        Assert.That(either.IsCollapsed, Is.True);
    }
}
