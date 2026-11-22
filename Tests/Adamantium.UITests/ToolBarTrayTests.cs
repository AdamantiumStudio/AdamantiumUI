using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Adamantium.Core.Collections;
using Adamantium.Mathematics;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Base;
using Adamantium.UI.Controls.Buttons;
using Adamantium.UI.Controls.Decorators;
using Adamantium.UI.Controls.Panels;
using Adamantium.UI.Controls.Primitives;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Automation;
using Adamantium.UI.Core.Data;
using Adamantium.UI.Core.RoutedEvents;
using Adamantium.UI.Core.Input;
using Adamantium.UI.Core.Media;
using Adamantium.UI.Core.Resources;
using Adamantium.UI.Core.Templates;
using Adamantium.UI.Extensions;
using NUnit.Framework;

namespace Adamantium.UITests;

// A tray's bars in rows, shortened from the last when a row is short of room, moved by their grips to another place, row
// or tray, shown and hidden from the tray's menu - in each theme's own style.
[TestFixture]
public class ToolBarTrayTests
{
    private const double CommandWidth = 30;

    private static readonly System.Type[] StyleSets =
    [
        typeof(Adamantium.UI.Themes.FluentTheme.ToolBarStyleSet),
        typeof(Adamantium.UI.Themes.GraphiteTheme.ToolBarStyleSet),
        typeof(Adamantium.UI.Themes.MacOsTheme.MacOsToolBarStyleSet)
    ];

    private static StyleSet _styleSet;

    private static ToolBar Bar(string header, int band, int place, int commands = 3)
    {
        var bar = new ToolBar { Header = header, Band = band, BandIndex = place };
        for (var i = 0; i < commands; i++)
        {
            bar.Items.Add(new Button { Name = header + i, Width = CommandWidth, Height = 24, Content = header + i });
        }

        return bar;
    }

    private static void Style(IFundamentalUIComponent element)
    {
        foreach (var style in _styleSet.Styles.Where(style => style.Selector.Match(element)))
        {
            style.Attach(element);
        }
    }

    private static ToolBarTray Tray(params ToolBar[] bars)
    {
        var tray = new ToolBarTray();
        Style(tray);
        foreach (var bar in bars)
        {
            Style(bar);
            tray.Items.Add(bar);
        }

        return tray;
    }

    private static void Show(IUIComponent root)
    {
        for (var i = 0; i < 6; i++)
        {
            WindowExtension.UpdateTree(root);
        }
    }

    private static Border Shown(ToolBarTray tray, double width)
    {
        var root = new Border { Width = width, Height = 300, Child = new StackPanel { Children = { tray } } };
        Show(root);
        return root;
    }

    private static Vector2<double> At(IUIComponent element, IUIComponent root) =>
        element.TranslatePoint(new Vector2<double>(0, 0), root);

    private static Vector2<double> Middle(IUIComponent element, IUIComponent root) =>
        element.TranslatePoint(new Vector2<double>(element.RenderSize.Width / 2, element.RenderSize.Height / 2), root);

    private static Thumb Grip(ToolBar bar) => (Thumb)bar.GetTemplateChild("PART_Grip");

    // Dragged from the middle of its grip to a point of the window, as the grip reports a drag: from where it was
    // pressed, in the window.
    private static void Drag(ToolBar bar, IUIComponent root, Vector2<double> to)
    {
        var grip = Grip(bar);
        var from = Middle(grip, root);
        grip.RaiseEvent(new DragStartedEventArgs(from) { RoutedEvent = Thumb.DragStartedEvent });
        grip.RaiseEvent(new DragEventArgs(to - from) { RoutedEvent = Thumb.DragDeltaEvent });
        grip.RaiseEvent(new DragCompletedEventArgs(to - from, false) { RoutedEvent = Thumb.DragCompletedEvent });
        Show(root);
    }

    private static bool IsInOverflow(ToolBar bar, IUIComponent command) =>
        ReferenceEquals(command.VisualParent, bar.GetTemplateChild("PART_OverflowItems"));

    [SetUp]
    public void Reset() => _styleSet = null;

    private static void Use(System.Type styleSet)
    {
        _styleSet = (StyleSet)System.Activator.CreateInstance(styleSet);
        _styleSet.Initialize(null);
    }

    [TestCaseSource(nameof(StyleSets))]
    public void Bars_StandInRows_ByBandAndBandIndex(System.Type styleSet)
    {
        Use(styleSet);
        var second = Bar("Second", 0, 1);
        var first = Bar("First", 0, 0);
        var below = Bar("Below", 1, 0);
        var tray = Tray(second, below, first);
        var root = Shown(tray, 800);

        Assert.Multiple(() =>
        {
            Assert.That(At(first, root).X, Is.LessThan(At(second, root).X));
            Assert.That(At(first, root).Y, Is.EqualTo(At(second, root).Y));
            Assert.That(At(below, root).Y, Is.GreaterThan(At(first, root).Y + first.RenderSize.Height - 1));
            Assert.That(At(below, root).X, Is.EqualTo(At(first, root).X).Within(0.5));
            Assert.That(tray.Bars, Is.EqualTo(new[] { first, second, below }));
        });
    }

    // The first command stands clear of the grip, after it along the bar, so the grip does not read as the command's edge;
    // under it down the side.
    [TestCaseSource(nameof(StyleSets))]
    public void TheFirstCommand_StandsClearOfTheGrip_AlongTheBar(System.Type styleSet)
    {
        Use(styleSet);
        var across = Bar("Across", 0, 0);
        var root = Shown(Tray(across), 800);
        // The grip takes its theme's style, as a window's theme gives it.
        Style(Grip(across));
        Show(root);
        var gripEnd = At(Grip(across), root).X + Grip(across).RenderSize.Width;
        var acrossGap = At((IUIComponent)across.Items[0], root).X - gripEnd;

        var down = Bar("Down", 0, 0);
        var tray = Tray(down);
        tray.Orientation = Orientation.Vertical;
        var side = new Border { Width = 400, Height = 800, Child = new StackPanel { Orientation = Orientation.Horizontal, Children = { tray } } };
        Show(side);
        Style(Grip(down));
        Show(side);
        var gripFoot = At(Grip(down), side).Y + Grip(down).RenderSize.Height;
        var downGap = At((IUIComponent)down.Items[0], side).Y - gripFoot;

        Assert.Multiple(() =>
        {
            Assert.That(acrossGap, Is.EqualTo(3).Within(0.5), "along a bar across");
            Assert.That(downGap, Is.EqualTo(3).Within(0.5), "down a bar down the side");
        });
    }

    [TestCaseSource(nameof(StyleSets))]
    public void DownTheSide_TheBarsOfARow_StandOneUnderAnother_AndRunDown(System.Type styleSet)
    {
        Use(styleSet);
        var first = Bar("First", 0, 0);
        var second = Bar("Second", 0, 1);
        var beside = Bar("Beside", 1, 0);
        var tray = Tray(first, second, beside);
        tray.Orientation = Orientation.Vertical;
        var root = new Border { Width = 400, Height = 800, Child = new StackPanel { Orientation = Orientation.Horizontal, Children = { tray } } };
        Show(root);

        Assert.Multiple(() =>
        {
            Assert.That(tray.Bars.Select(bar => bar.Orientation), Is.All.EqualTo(Orientation.Vertical));
            Assert.That(At(second, root).Y, Is.GreaterThan(At(first, root).Y + first.RenderSize.Height - 1));
            Assert.That(At(second, root).X, Is.EqualTo(At(first, root).X).Within(0.5));
            Assert.That(At(beside, root).X, Is.GreaterThan(At(first, root).X + first.RenderSize.Width - 1));
            Assert.That(first.RenderSize.Height, Is.GreaterThan(first.RenderSize.Width), "a bar runs down the side");
        });
    }

    [TestCaseSource(nameof(StyleSets))]
    public void AHiddenBar_LeavesNoGap(System.Type styleSet)
    {
        Use(styleSet);
        var first = Bar("First", 0, 0);
        var second = Bar("Second", 0, 1);
        var tray = Tray(first, second);
        var root = Shown(tray, 800);
        var start = At(first, root).X;

        first.Visibility = Visibility.Collapsed;
        Show(root);

        Assert.That(At(second, root).X, Is.EqualTo(start).Within(0.5));
    }

    [TestCaseSource(nameof(StyleSets))]
    public void AShortRow_ShortensItsLastBar_ItsLastCommandsGoingToItsOverflowMenu_AndComeBackWithRoom(System.Type styleSet)
    {
        Use(styleSet);
        var first = Bar("First", 0, 0, commands: 4);
        var last = Bar("Last", 0, 1, commands: 4);
        var tray = Tray(first, last);
        var root = Shown(tray, 1000);

        // A command and a half short: the chevron takes the place of one more.
        root.Width = tray.DesiredSize.Width - CommandWidth * 1.5;
        Show(root);
        var overflowing = last.Items.Cast<IUIComponent>().Where(command => IsInOverflow(last, command)).ToList();
        var firstShortened = first.HasOverflowItems;
        var chevron = (IUIComponent)last.GetTemplateChild("PART_OverflowButton");
        var gap = At(last, root).X + last.RenderSize.Width - last.Padding.Right - last.BorderThickness.Right -
                  (At(chevron, root).X + chevron.RenderSize.Width);

        root.Width = 1000;
        Show(root);

        Assert.Multiple(() =>
        {
            Assert.That(firstShortened, Is.False, "the last bar gives way first");
            Assert.That(overflowing, Has.Count.GreaterThanOrEqualTo(2));
            Assert.That(overflowing, Does.Contain(last.Items[3]), "the last commands go first");
            Assert.That(overflowing, Does.Not.Contain(last.Items[0]));
            Assert.That(gap, Is.LessThan(1), "the chevron comes right after the commands left on the bar");
            Assert.That(last.HasOverflowItems, Is.False, "with room the commands come back");
            Assert.That(last.Items.Cast<IUIComponent>().Any(command => IsInOverflow(last, command)), Is.False);
        });
    }

    [TestCaseSource(nameof(StyleSets))]
    public void TheBarsSpacing_StandsBetweenItsCommands_AndCountsWhenRoomIsShort(System.Type styleSet)
    {
        Use(styleSet);
        var bar = Bar("Bar", 0, 0);
        bar.Spacing = 10;
        var tray = Tray(bar);
        var root = Shown(tray, 1000);
        var first = (IUIComponent)bar.Items[0];
        var second = (IUIComponent)bar.Items[1];
        var gap = At(second, root).X - (At(first, root).X + first.RenderSize.Width);

        root.Width = tray.DesiredSize.Width;
        Show(root);
        var fitsExactly = !bar.HasOverflowItems;
        root.Width -= 5;
        Show(root);

        Assert.Multiple(() =>
        {
            Assert.That(gap, Is.EqualTo(10).Within(0.5));
            Assert.That(fitsExactly, Is.True);
            Assert.That(bar.HasOverflowItems, Is.True, "five short of room with the spacing counted");
        });
    }

    [TestCaseSource(nameof(StyleSets))]
    public void ACommandThatNeverOverflows_StaysOnTheBar_AndOneThatAlwaysDoes_IsInTheMenu(System.Type styleSet)
    {
        Use(styleSet);
        var bar = Bar("Bar", 0, 0, commands: 4);
        ToolBar.SetOverflowMode((IAdamantiumComponent)bar.Items[3], ToolBarOverflowMode.Never);
        ToolBar.SetOverflowMode((IAdamantiumComponent)bar.Items[0], ToolBarOverflowMode.Always);
        var tray = Tray(bar);
        var root = Shown(tray, 1000);
        var alwaysWithRoom = IsInOverflow(bar, (IUIComponent)bar.Items[0]);

        root.Width = 60;
        Show(root);

        Assert.Multiple(() =>
        {
            Assert.That(alwaysWithRoom, Is.True);
            Assert.That(bar.HasOverflowItems, Is.True);
            Assert.That(IsInOverflow(bar, (IUIComponent)bar.Items[3]), Is.False);
            Assert.That(IsInOverflow(bar, (IUIComponent)bar.Items[2]), Is.True);
        });
    }

    [TestCaseSource(nameof(StyleSets))]
    public void ABarDraggedInItsRow_StandsWhereItIsLetGo(System.Type styleSet)
    {
        Use(styleSet);
        var first = Bar("First", 0, 0);
        var second = Bar("Second", 0, 1);
        var third = Bar("Third", 0, 2);
        var tray = Tray(first, second, third);
        var root = Shown(tray, 1000);

        Drag(third, root, At(first, root) + new Vector2<double>(2, first.RenderSize.Height / 2));

        Assert.Multiple(() =>
        {
            Assert.That(tray.Bars, Is.EqualTo(new[] { third, first, second }));
            Assert.That(new[] { third.BandIndex, first.BandIndex, second.BandIndex }, Is.EqualTo(new[] { 0, 1, 2 }));
            Assert.That(third.IsMoving, Is.False);
            Assert.That(third.RenderTransform, Is.Null);
        });
    }

    [TestCaseSource(nameof(StyleSets))]
    public void ABarDraggedToAnotherRow_OrBelowTheLast_GoesThere_AndAnEmptiedRowGoes(System.Type styleSet)
    {
        Use(styleSet);
        var first = Bar("First", 0, 0);
        var second = Bar("Second", 0, 1);
        var below = Bar("Below", 1, 0);
        var tray = Tray(first, second, below);
        var root = Shown(tray, 1000);
        var end = At(second, root) + new Vector2<double>(second.RenderSize.Width - 2, second.RenderSize.Height / 2);

        Drag(below, root, end);
        var joined = new[] { below.Band, below.BandIndex };
        var rowsAfterJoining = tray.Bars.Select(bar => bar.Band).Distinct().Count();
        var under = At(first, root) + new Vector2<double>(4, first.RenderSize.Height + 6);
        Drag(first, root, under);

        Assert.Multiple(() =>
        {
            Assert.That(joined, Is.EqualTo(new[] { 0, 2 }));
            Assert.That(rowsAfterJoining, Is.EqualTo(1), "the emptied row goes");
            Assert.That(first.Band, Is.EqualTo(1), "below the last row is a new row");
            Assert.That(new[] { second.Band, second.BandIndex, below.BandIndex }, Is.EqualTo(new[] { 0, 0, 1 }));
        });
    }

    [TestCaseSource(nameof(StyleSets))]
    public void ABarAloneInItsRow_DraggedWithinIt_StaysThere_AndDraggedOntoTheNext_JoinsIt(System.Type styleSet)
    {
        Use(styleSet);
        var top = Bar("Top", 0, 0);
        var middle = Bar("Middle", 1, 0);
        var bottom = Bar("Bottom", 2, 0);
        var tray = Tray(top, middle, bottom);
        var root = Shown(tray, 1000);

        Drag(middle, root, Middle(Grip(middle), root) + new Vector2<double>(3, 0));
        var stayed = new[] { top.Band, middle.Band, bottom.Band };
        Drag(middle, root, At(bottom, root) + new Vector2<double>(bottom.RenderSize.Width - 2, bottom.RenderSize.Height / 2));

        Assert.Multiple(() =>
        {
            Assert.That(stayed, Is.EqualTo(new[] { 0, 1, 2 }), "a small drag leaves a bar alone in its row where it was");
            Assert.That(new[] { bottom.Band, bottom.BandIndex, middle.Band, middle.BandIndex },
                Is.EqualTo(new[] { 1, 0, 1, 1 }), "dragged onto the next row, it joins it");
        });
    }

    [TestCaseSource(nameof(StyleSets))]
    public void AHiddenBar_ComesBackInItsRow_NotOnABarMovedMeanwhile(System.Type styleSet)
    {
        Use(styleSet);
        var first = Bar("First", 0, 0);
        var second = Bar("Second", 0, 1);
        var hidden = Bar("Hidden", 1, 0);
        var tray = Tray(first, second, hidden);
        var root = Shown(tray, 1000);

        hidden.Visibility = Visibility.Collapsed;
        Show(root);
        Drag(first, root, At(second, root) + new Vector2<double>(4, second.RenderSize.Height + 6));
        hidden.Visibility = Visibility.Visible;
        Show(root);

        Assert.Multiple(() =>
        {
            Assert.That(hidden.Band, Is.EqualTo(1), "the hidden bar keeps its row");
            Assert.That(first.Band, Is.EqualTo(2), "the new row is after it");
            Assert.That(tray.Bars, Is.EqualTo(new[] { second, hidden, first }));
        });
    }

    [TestCaseSource(nameof(StyleSets))]
    public void ADraggedBar_KeepsTheLayerAndTransformItsAuthorGaveIt(System.Type styleSet)
    {
        Use(styleSet);
        var first = Bar("First", 0, 0);
        var second = Bar("Second", 0, 1);
        var turn = new Adamantium.UI.Core.Media.Transform { RotationAngle = 0 };
        second.ZIndex = 5;
        second.RenderTransform = turn;
        var tray = Tray(first, second);
        var root = Shown(tray, 1000);

        Drag(second, root, At(first, root) + new Vector2<double>(2, first.RenderSize.Height / 2));

        Assert.Multiple(() =>
        {
            Assert.That(tray.Bars, Is.EqualTo(new[] { second, first }));
            Assert.That(second.ZIndex, Is.EqualTo(5));
            Assert.That(second.RenderTransform, Is.SameAs(turn));
        });
    }

    [TestCaseSource(nameof(StyleSets))]
    public void AnElementPutInTheTray_StandsOnABarOfItsOwn(System.Type styleSet)
    {
        Use(styleSet);
        var lone = new Button { Name = "Lone", Width = CommandWidth, Height = 24 };
        var tray = Tray(Bar("Bar", 0, 0));
        tray.Items.Add(lone);
        Shown(tray, 1000);

        // The bar the tray makes is styled by the theme in an application; here it only has to hold the element.
        var holder = tray.Bars.FirstOrDefault(bar => bar.Items.Contains(lone));

        Assert.Multiple(() =>
        {
            Assert.That(holder, Is.Not.Null, "the element stands on a bar");
            Assert.That(holder?.DataContext, Is.Not.SameAs(lone));
        });
    }

    [TestCaseSource(nameof(StyleSets))]
    public void AMenuSharedByTwoTrays_NamesOnlyTheBarsOfTheTrayItOpensOn(System.Type styleSet)
    {
        Use(styleSet);
        var top = Tray(Bar("Top", 0, 0));
        var bottom = Tray(Bar("Bottom", 0, 0));
        var shared = new ContextMenu();
        shared.Items.Add(new MenuItem { Header = "Lock", Command = ToolBarTray.LockCommand });
        top.ContextMenu = shared;
        bottom.ContextMenu = shared;
        var root = new Border { Width = 1000, Height = 300, Child = new StackPanel { Children = { top, bottom } } };
        Show(root);

        shared.Open(top);
        var onTop = shared.Items.OfType<MenuItem>().Select(row => row.Header).ToList();
        shared.IsOpen = false;
        shared.Open(bottom);
        var onBottom = shared.Items.OfType<MenuItem>().Select(row => row.Header).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(onTop, Is.EqualTo(new object[] { "Top", "Lock" }));
            Assert.That(onBottom, Is.EqualTo(new object[] { "Bottom", "Lock" }));
        });
    }

    [TestCaseSource(nameof(StyleSets))]
    public void TurnedDownTheSide_ABar_MeasuresItsCommandsAlongTheNewWay(System.Type styleSet)
    {
        Use(styleSet);
        var bar = new ToolBar { Header = "Wide" };
        for (var i = 0; i < 4; i++)
        {
            bar.Items.Add(new Button { Width = 80, Height = 24 });
        }

        var tray = Tray(bar);
        var root = new Border { Width = 200, Height = 200, Child = tray };
        Show(root);
        var overflowedAcross = bar.HasOverflowItems;

        root.Width = 1000;
        tray.Orientation = Orientation.Vertical;
        Show(root);

        Assert.Multiple(() =>
        {
            Assert.That(overflowedAcross, Is.True);
            Assert.That(bar.HasOverflowItems, Is.False, "four commands 24 high fit in 200");
        });
    }

    [TestCaseSource(nameof(StyleSets))]
    public void ABarLetGoOfOutsideEveryTray_GoesBack(System.Type styleSet)
    {
        Use(styleSet);
        var first = Bar("First", 0, 0);
        var second = Bar("Second", 0, 1);
        var tray = Tray(first, second);
        var root = Shown(tray, 1000);

        Drag(first, root, new Vector2<double>(500, 280));

        Assert.Multiple(() =>
        {
            Assert.That(tray.Bars, Is.EqualTo(new[] { first, second }));
            Assert.That(new[] { first.Band, first.BandIndex }, Is.EqualTo(new[] { 0, 0 }));
            Assert.That(tray.Items, Does.Contain(first));
        });
    }

    [TestCaseSource(nameof(StyleSets))]
    public void ABarDraggedOntoAnotherTray_GoesToIt(System.Type styleSet)
    {
        Use(styleSet);
        var moved = Bar("Moved", 0, 0);
        var stays = Bar("Stays", 0, 1);
        var top = Tray(moved, stays);
        var other = Bar("Other", 0, 0);
        var bottom = Tray(other);
        var root = new Border
        {
            Width = 1000, Height = 400,
            Child = new StackPanel { Children = { top, new Border { Height = 100 }, bottom } }
        };
        Show(root);

        Drag(moved, root, At(other, root) + new Vector2<double>(other.RenderSize.Width - 2, other.RenderSize.Height / 2));

        Assert.Multiple(() =>
        {
            Assert.That(top.Items, Does.Not.Contain(moved));
            Assert.That(bottom.Items, Does.Contain(moved));
            Assert.That(bottom.Bars, Is.EqualTo(new[] { other, moved }));
            Assert.That(new[] { stays.Band, stays.BandIndex }, Is.EqualTo(new[] { 0, 0 }), "the tray left numbers again");
        });
    }

    [TestCaseSource(nameof(StyleSets))]
    public void ALockedTray_HidesTheGrips_AndMovesNoBar(System.Type styleSet)
    {
        Use(styleSet);
        var first = Bar("First", 0, 0);
        var second = Bar("Second", 0, 1);
        var tray = Tray(first, second);
        var root = Shown(tray, 1000);
        var grip = Grip(second);

        ToolBarTray.LockCommand.Execute(null, tray);
        Show(root);
        var locked = tray.IsLocked;
        var gripShown = grip.Visibility;
        Drag(second, root, At(first, root) + new Vector2<double>(2, 4));

        Assert.Multiple(() =>
        {
            Assert.That(locked, Is.True);
            Assert.That(ToolBarTray.LockCommand.Query(null, tray).IsChecked, Is.True);
            Assert.That(gripShown, Is.EqualTo(Visibility.Collapsed));
            Assert.That(tray.Bars, Is.EqualTo(new[] { first, second }));
        });
    }

    [TestCaseSource(nameof(StyleSets))]
    public void TheTraysMenu_NamesItsBars_EachCheckedWhileShown_AndHidesOneUnchecked(System.Type styleSet)
    {
        Use(styleSet);
        var first = Bar("First", 0, 0);
        var second = Bar("Second", 1, 0);
        var tray = Tray(first, second);
        Shown(tray, 1000);
        var menu = tray.ContextMenu;

        second.Visibility = Visibility.Collapsed;
        menu.Open(tray);
        var rows = menu.Items.Cast<object>().ToList();
        var firstRow = (MenuItem)rows[0];
        firstRow.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent, firstRow)
        {
            RoutedEvent = MenuItem.ClickEvent
        });
        menu.IsOpen = false;
        menu.Open(tray);
        var reopened = menu.Items.Cast<object>().ToList();

        Assert.Multiple(() =>
        {
            Assert.That(rows.Take(2).Cast<MenuItem>().Select(row => row.Header), Is.EqualTo(new[] { "First", "Second" }));
            Assert.That(rows.Take(2).Cast<MenuItem>().Select(row => row.IsChecked), Is.EqualTo(new[] { true, false }));
            Assert.That(rows[2], Is.InstanceOf<Separator>());
            Assert.That(((MenuItem)rows[3]).Command, Is.SameAs(ToolBarTray.LockCommand));
            Assert.That(reopened, Has.Count.EqualTo(rows.Count), "the rows made before are taken out first");
        });
    }

    private static ToolBarGroup Group(string header, int commands)
    {
        var group = new ToolBarGroup { Header = header };
        for (var i = 0; i < commands; i++)
        {
            group.Items.Add(new Button { Name = header + i, Width = CommandWidth, Height = 24, Content = header + i });
        }

        Style(group);
        return group;
    }

    private static Separator RuleOf(ToolBarGroup group) => (Separator)group.GetTemplateChild("PART_Separator");

    // A section goes to the menu whole, never split between the bar and the menu; a rule stands before a section only
    // where a command stands before it, on the bar or in the menu.
    [TestCaseSource(nameof(StyleSets))]
    public void ASection_GoesToTheMenuWhole_AndItsRuleStandsOnlyAfterACommand(System.Type styleSet)
    {
        Use(styleSet);
        var bar = new ToolBar { Header = "Home" };
        var first = Group("Clipboard", 3);
        var second = Group("Font", 3);
        var third = Group("Paragraph", 3);
        bar.Items.Add(first);
        bar.Items.Add(second);
        bar.Items.Add(third);
        var tray = Tray(bar);
        var root = Shown(tray, 1000);
        var allOnBar = new[] { RuleOf(first).Visibility, RuleOf(second).Visibility, RuleOf(third).Visibility };
        var whole = tray.DesiredSize.Width;

        // A command short of the whole: the last section goes, whole, though one command less would have fitted.
        root.Width = whole - CommandWidth;
        Show(root);
        var thirdInMenu = IsInOverflow(bar, third);
        var secondInMenu = IsInOverflow(bar, second);
        var thirdsRuleInMenu = RuleOf(third).Visibility;

        // Two sections short: in the menu the first of them has no rule, the next one has.
        root.Width = whole - CommandWidth * 5;
        Show(root);
        var both = new[] { IsInOverflow(bar, first), IsInOverflow(bar, second), IsInOverflow(bar, third) };
        var rulesInMenu = new[] { RuleOf(second).Visibility, RuleOf(third).Visibility };

        Assert.Multiple(() =>
        {
            Assert.That(allOnBar, Is.EqualTo(new[] { Visibility.Collapsed, Visibility.Visible, Visibility.Visible }),
                "the first thing on the bar has no rule before it");
            Assert.That(thirdInMenu, Is.True);
            Assert.That(secondInMenu, Is.False);
            Assert.That(third.Items.Cast<IUIComponent>().All(command => ReferenceEquals(command.GetVisualAncestors()
                .OfType<ToolBarGroup>().FirstOrDefault(), third)), Is.True, "its commands stay together in it");
            Assert.That(thirdsRuleInMenu, Is.EqualTo(Visibility.Collapsed), "first in the menu, no rule");
            Assert.That(both, Is.EqualTo(new[] { false, true, true }));
            Assert.That(rulesInMenu, Is.EqualTo(new[] { Visibility.Collapsed, Visibility.Visible }));
        });
    }

    // Wrapping, a section that does not fit goes onto a new line of the bar, whole, not to the menu: the bar grows across,
    // and the section beginning the line hides its rule. The layout settles: passes more leave it as it is.
    [TestCaseSource(nameof(StyleSets))]
    public void WrappingSections_GoOntoANewLine_Whole_TheOneBeginningItWithNoRule(System.Type styleSet)
    {
        Use(styleSet);
        var first = Group("Clipboard", 3);
        var second = Group("Font", 3);
        var third = Group("Paragraph", 3);
        var bar = new ToolBar { Header = "Home", CommandWrapping = CommandWrapping.Wrap, Items = { first, second, third } };
        var tray = Tray(bar);
        var root = Shown(tray, 1000);
        var oneLine = bar.RenderSize.Height;
        var whole = tray.DesiredSize.Width;

        root.Width = whole - CommandWidth;
        Show(root);
        var placed = At(third, root);
        Show(root);

        Assert.Multiple(() =>
        {
            Assert.That(bar.IsMeasureValid && bar.IsArrangeValid, Is.True, "settled");
            Assert.That(At(third, root), Is.EqualTo(placed), "where the passes before put it");
            Assert.That(bar.HasOverflowItems, Is.False, "nothing in the menu");
            Assert.That(IsInOverflow(bar, third), Is.False);
            Assert.That(At(third, root).Y, Is.GreaterThan(At(first, root).Y + first.RenderSize.Height - 1), "on the next line");
            Assert.That(At((IUIComponent)third.Items[0], root).X, Is.EqualTo(At((IUIComponent)first.Items[0], root).X).Within(0.5),
                "its first command in line with the first line's");
            Assert.That(bar.RenderSize.Height, Is.GreaterThan(oneLine + first.RenderSize.Height - 1));
            Assert.That(third.Items.Cast<IUIComponent>().Select(command => At(command, root).Y).Distinct().Count(), Is.EqualTo(1),
                "its commands on one line");
            Assert.That(new[] { RuleOf(second).Visibility, RuleOf(third).Visibility },
                Is.EqualTo(new[] { Visibility.Visible, Visibility.Hidden }), "hidden, its room kept");
            Assert.That(third.BeginsLine, Is.True);
            Assert.That(bar.RenderSize.Width, Is.LessThanOrEqualTo(root.Width + 0.5), "within the room");
        });
    }

    // A command that began a line, taken off a bar no longer wrapping, is not kept alive by the bar. Unstyled commands:
    // a style attached to one holds it itself.
    [TestCaseSource(nameof(StyleSets))]
    public void ACommandThatBeganALine_TakenOff_IsLetGo(System.Type styleSet)
    {
        Use(styleSet);
        var bar = new ToolBar { Header = "Home", CommandWrapping = CommandWrapping.Wrap };
        var taken = WrapAndTakeOff(bar);
        // What the frame would let go of on its own: the queues of visuals taken off and of what to draw again.
        while (DiscardedVisuals.Drain(64) > 0) { }
        RenderDirty.Clear();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        Assert.That(taken.IsAlive, Is.False);
        GC.KeepAlive(bar);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference WrapAndTakeOff(ToolBar bar)
    {
        for (var i = 0; i < 3; i++)
        {
            bar.Items.Add(new Button { Width = CommandWidth * 3, Height = 24 });
        }

        var tray = Tray(bar);
        var root = Shown(tray, 1000);
        root.Width = tray.DesiredSize.Width - CommandWidth;
        Show(root);
        var third = bar.Items[2];
        var wrapped = ((IUIComponent)third).TranslatePoint(new Vector2<double>(0, 0), root).Y > 1;
        bar.CommandWrapping = CommandWrapping.NoWrap;
        root.Width = 1000;
        Show(root);
        bar.Items.Remove(third);
        Show(root);
        Assert.That(wrapped, Is.True, "it began the second line");
        return new WeakReference(third);
    }

    // Down from a line goes to the line below, Up back; and a section that began a line, gone to the menu when the bar
    // stops wrapping, no longer begins one.
    [TestCaseSource(nameof(StyleSets))]
    public void OnAWrappingBar_TheArrowsAcrossGoFromLineToLine_AndTheMenuHasNoLines(System.Type styleSet)
    {
        Use(styleSet);
        var first = Group("Clipboard", 3);
        var second = Group("Font", 3);
        var third = Group("Paragraph", 3);
        var bar = new ToolBar { Header = "Home", CommandWrapping = CommandWrapping.Wrap, Items = { first, second, third } };
        var tray = Tray(bar);
        var root = Shown(tray, 1000);
        root.Width = tray.DesiredSize.Width - CommandWidth;
        Show(root);

        // From the last command of the first line's first section: under it stands the third section's last command.
        FocusManager.Focus((IInputComponent)first.Items[2]);
        var down = KeyboardNavigation.Move(FocusNavigationDirection.Down);
        var below = FocusManager.Focused;
        var up = KeyboardNavigation.Move(FocusNavigationDirection.Up);
        var above = FocusManager.Focused;

        bar.CommandWrapping = CommandWrapping.NoWrap;
        Show(root);

        Assert.Multiple(() =>
        {
            Assert.That(down, Is.True);
            Assert.That(below, Is.SameAs(third.Items[2]), "the command right under it, not the section's first");
            Assert.That(up, Is.True);
            Assert.That(above, Is.SameAs(first.Items[2]), "the command right over it, not the section's last");
            Assert.That(IsInOverflow(bar, third), Is.True);
            Assert.That(third.BeginsLine, Is.False, "the menu has no lines");
        });
    }

    // A wrapping bar is as short as its longest command; one set always to the menu still goes there; switched back, the
    // commands share out between the bar and the menu again.
    [TestCaseSource(nameof(StyleSets))]
    public void AWrappingBar_IsAsShortAsItsLongestCommand_AndKeepsTheMenuForTheAlways(System.Type styleSet)
    {
        Use(styleSet);
        var groups = new[] { Group("Clipboard", 3), Group("Font", 4), Group("Paragraph", 2) };
        var always = new Button { Width = CommandWidth, Height = 24, Content = "More" };
        ToolBar.SetOverflowMode(always, ToolBarOverflowMode.Always);
        Style(always);
        var bar = new ToolBar { Header = "Home", CommandWrapping = CommandWrapping.Wrap };
        foreach (var group in groups) bar.Items.Add(group);
        bar.Items.Add(always);
        var root = Shown(Tray(bar), 1000);
        var shortest = bar.ShortestLength;
        var fontLength = groups[1].DesiredSize.Width;

        root.Width = 150;
        Show(root);
        var wrappedHasMenu = bar.HasOverflowItems;
        var alwaysInMenu = IsInOverflow(bar, always);

        bar.CommandWrapping = CommandWrapping.NoWrap;
        Show(root);

        Assert.Multiple(() =>
        {
            Assert.That(shortest, Is.LessThan(fontLength + 80), "the longest section and the bar's own chrome");
            Assert.That(shortest, Is.GreaterThanOrEqualTo(fontLength));
            Assert.That(wrappedHasMenu, Is.True);
            Assert.That(alwaysInMenu, Is.True);
            Assert.That(groups.Count(group => IsInOverflow(bar, group)), Is.GreaterThan(0), "no longer wrapping, to the menu");
        });
    }

    // A command before the first section shown or hidden puts the section's rule there or takes it away.
    [TestCaseSource(nameof(StyleSets))]
    public void ACommandShownOrHiddenBeforeASection_PutsItsRuleThereOrTakesItAway(System.Type styleSet)
    {
        Use(styleSet);
        var undo = new Button { Width = CommandWidth, Height = 24, Visibility = Visibility.Collapsed };
        var clipboard = Group("Clipboard", 2);
        var bar = new ToolBar { Header = "Home", Items = { undo, clipboard } };
        var root = Shown(Tray(bar), 1000);
        var hidden = RuleOf(clipboard).Visibility;

        undo.Visibility = Visibility.Visible;
        Show(root);
        var shown = RuleOf(clipboard).Visibility;

        Assert.Multiple(() =>
        {
            Assert.That(new[] { hidden, shown }, Is.EqualTo(new[] { Visibility.Collapsed, Visibility.Visible }));
            Assert.That(bar.FullLength, Is.GreaterThanOrEqualTo(CommandWidth * 3), "the bar measured again with it");
        });
    }

    // Each command of a section stands at its own width, not at the width of the first.
    [TestCaseSource(nameof(StyleSets))]
    public void ASectionsCommands_EachStandAtItsOwnWidth(System.Type styleSet)
    {
        Use(styleSet);
        var group = new ToolBarGroup { Header = "Align" };
        var narrow = new Button { Width = 30, Height = 24 };
        var wide = new Button { Width = 70, Height = 24 };
        group.Items.Add(narrow);
        group.Items.Add(wide);
        Style(group);
        var bar = new ToolBar { Header = "Home", Items = { group } };
        Shown(Tray(bar), 1000);

        Assert.That(new[] { narrow.RenderSize.Width, wide.RenderSize.Width }, Is.EqualTo(new[] { 30.0, 70.0 }));
    }

    // A bar made contextual is washed in its context's color behind the whole of it, its commands over the wash; an
    // ordinary bar is not, and a bar no longer contextual loses it.
    [TestCaseSource(nameof(StyleSets))]
    public void AContextualBar_IsWashedBehindTheWholeBar_AnOrdinaryOneIsNot(System.Type styleSet)
    {
        Use(styleSet);
        var home = Bar("Home", 0, 0);
        var table = Bar("Table", 1, 0);
        var root = Shown(Tray(home, table), 1000);
        var wash = (UIComponent)table.GetTemplateChild("ContextualWash");
        var before = wash.Visibility;
        var green = new SolidColorBrush(Colors.SeaGreen);
        table.IsContextual = true;
        table.Accent = green;
        Show(root);
        var plain = ((UIComponent)home.GetTemplateChild("ContextualWash")).Visibility;
        var grid = (Grid)wash.VisualParent;
        var first = grid.Children[0];
        var border = (Border)grid.VisualParent;
        var washed = wash.Visibility;
        var size = wash.RenderSize;

        table.IsContextual = false;

        Assert.Multiple(() =>
        {
            Assert.That(before, Is.EqualTo(Visibility.Collapsed), "not contextual yet");
            Assert.That(washed, Is.EqualTo(Visibility.Visible));
            Assert.That(((Border)wash).Background, Is.SameAs(green), "in its context's color");
            Assert.That(size.Width, Is.EqualTo(border.RenderSize.Width - border.BorderThickness.Left
                - border.BorderThickness.Right).Within(0.5), "the whole bar across");
            Assert.That(size.Height, Is.EqualTo(border.RenderSize.Height - border.BorderThickness.Top
                - border.BorderThickness.Bottom).Within(0.5), "the whole bar down");
            Assert.That(first, Is.SameAs(wash), "drawn first, under the commands");
            Assert.That(plain, Is.EqualTo(Visibility.Collapsed));
            Assert.That(wash.Visibility, Is.EqualTo(Visibility.Collapsed), "no longer contextual");
        });
    }

    // A contextual bar with its context's name says it at its start, on a label in its color, before its commands; a
    // bar not contextual, one with no name, or one told not to show it has none.
    [TestCaseSource(nameof(StyleSets))]
    public void AContextualBar_NamesItsContext_AtItsStart_OnALabelInItsColor(System.Type styleSet)
    {
        Use(styleSet);
        var table = Bar("Table", 0, 0);
        var green = new SolidColorBrush(Colors.SeaGreen);
        table.Accent = green;
        table.ContextualHeader = "Table Tools";
        var root = Shown(Tray(table), 1000);
        var label = (RibbonContextualLabel)table.GetTemplateChild("ContextualHeaderLabel");
        var notContextual = label.Visibility;
        var firstCommand = table.Items.OfType<Button>().First();
        var plainStart = At(firstCommand, root).X;

        table.IsContextual = true;
        Show(root);
        var named = label.Visibility;
        var labelEnd = At(label, root).X + label.RenderSize.Width;
        var namedStart = At(firstCommand, root).X;
        table.ShowsContextualHeader = false;
        Show(root);
        var hidden = label.Visibility;
        table.ShowsContextualHeader = true;
        table.ContextualHeader = null;
        Show(root);

        Assert.Multiple(() =>
        {
            Assert.That(notContextual, Is.EqualTo(Visibility.Collapsed), "not contextual");
            Assert.That(named, Is.EqualTo(Visibility.Visible));
            Assert.That(label.Content, Is.Null, "the name gone");
            Assert.That(label.Accent, Is.SameAs(green), "in the bar's color");
            Assert.That(namedStart, Is.GreaterThanOrEqualTo(labelEnd), "before the commands");
            Assert.That(namedStart, Is.GreaterThan(plainStart), "the commands make room for it");
            Assert.That(hidden, Is.EqualTo(Visibility.Collapsed), "told not to show it");
            Assert.That(label.Visibility, Is.EqualTo(Visibility.Collapsed), "no name");
        });
    }

    // Down the side a contextual bar shows no label - its color alone says it there - while automation reads its
    // context as the bar's help text either way, and the application's own help text before it.
    [TestCaseSource(nameof(StyleSets))]
    public void AContextualBarDownTheSide_ShowsNoLabel_ButAutomationReadsItsContext(System.Type styleSet)
    {
        Use(styleSet);
        var table = Bar("Table", 0, 0);
        table.IsContextual = true;
        table.ContextualHeader = "Table Tools";
        var tray = Tray(table);
        tray.Orientation = Orientation.Vertical;
        Shown(tray, 1000);
        var label = (RibbonContextualLabel)table.GetTemplateChild("ContextualHeaderLabel");
        var peer = table.GetAutomationPeer();

        var context = peer.HelpText;
        AutomationProperties.SetHelpText(table, "Rows and columns");
        var own = peer.HelpText;

        Assert.Multiple(() =>
        {
            Assert.That(label.Visibility, Is.EqualTo(Visibility.Collapsed));
            Assert.That(context, Is.EqualTo("Table Tools"));
            Assert.That(own, Is.EqualTo("Rows and columns"));
        });
    }

    // The section a shown context comes from is framed and washed in the context's color, and nothing moves for it; a
    // section in no context has neither.
    [TestCaseSource(nameof(StyleSets))]
    public void ASectionAContextComesFrom_IsFramedAndWashedInItsColor_AndNothingMoves(System.Type styleSet)
    {
        Use(styleSet);
        var green = new SolidColorBrush(Colors.SeaGreen);
        var tables = new ToolBarGroup { Header = "Tables", Accent = green };
        tables.Items.Add(new Button { Width = CommandWidth, Height = 24, Content = "Table" });
        var other = new ToolBarGroup { Header = "Align" };
        other.Items.Add(new Button { Width = CommandWidth, Height = 24, Content = "Left" });
        Style(tables);
        Style(other);
        var bar = new ToolBar { Header = "Insert", Items = { other, tables } };
        var root = Shown(Tray(bar), 1000);
        var command = tables.Items.OfType<Button>().Single();
        var before = At(command, root);
        var barWidth = bar.RenderSize.Width;

        tables.IsInContext = true;
        Show(root);
        var frame = (Border)tables.GetTemplateChild("ContextFrame");
        var wash = (Border)tables.GetTemplateChild("ContextWash");
        var after = At(command, root);

        Assert.Multiple(() =>
        {
            Assert.That(frame.Visibility, Is.EqualTo(Visibility.Visible));
            Assert.That(wash.Visibility, Is.EqualTo(Visibility.Visible));
            Assert.That(frame.BorderBrush, Is.SameAs(green));
            Assert.That(wash.Background, Is.SameAs(green));
            Assert.That(frame.RenderSize.Width, Is.GreaterThan(command.RenderSize.Width), "round the commands");
            Assert.That(after, Is.EqualTo(before), "nothing moves");
            Assert.That(bar.RenderSize.Width, Is.EqualTo(barWidth).Within(0.5));
            Assert.That(((UIComponent)other.GetTemplateChild("ContextFrame")).Visibility, Is.EqualTo(Visibility.Collapsed));
        });
    }

    // A section a shown context comes from, gone to the overflow menu: its bar's chevron is marked in the context's color
    // while it is in context, and no longer once it is not.
    [TestCaseSource(nameof(StyleSets))]
    public void ASectionAContextComesFrom_InTheMenu_MarksTheChevron(System.Type styleSet)
    {
        Use(styleSet);
        var green = new SolidColorBrush(Colors.SeaGreen);
        var bar = Bar("Insert", 0, 0, commands: 6);
        var tables = new ToolBarGroup { Header = "Tables", Accent = green, IsInContext = true };
        tables.Items.Add(new Button { Width = CommandWidth, Height = 24, Content = "Table" });
        Style(tables);
        bar.Items.Add(tables);
        var root = Shown(Tray(bar), 120);
        var marked = bar.HasContextInOverflow;
        var accent = bar.OverflowContextAccent;
        var frame = (Border)bar.GetTemplateChild("OverflowContextFrame");
        var drawn = frame.Visibility;
        var drawnIn = frame.BorderBrush;
        var chevron = (UIComponent)bar.GetTemplateChild("PART_OverflowButton");

        tables.IsInContext = false;
        Show(root);

        Assert.Multiple(() =>
        {
            Assert.That(bar.HasOverflowItems, Is.True, "short of room");
            Assert.That(marked, Is.True);
            Assert.That(accent, Is.SameAs(green));
            Assert.That(drawn, Is.EqualTo(Visibility.Visible), "the chevron framed");
            Assert.That(drawnIn, Is.SameAs(green));
            Assert.That(frame.GetVisualAncestors().Contains(chevron), Is.True, "in the chevron, over its own states");
            Assert.That(bar.HasContextInOverflow, Is.False, "out of context");
            Assert.That(bar.OverflowContextAccent, Is.Null);
            Assert.That(frame.Visibility, Is.EqualTo(Visibility.Collapsed));
        });
    }

    // Contextual bars are listed apart, under a header of their own, checked while shown and not switched by the user;
    // the user's bars get a header too. With no contextual bar the menu has no headers.
    [TestCaseSource(nameof(StyleSets))]
    public void TheTraysMenu_ListsContextualBarsApart_UnderTheirHeader_AndDoesNotSwitchThem(System.Type styleSet)
    {
        Use(styleSet);
        var home = Bar("Home", 0, 0);
        var table = Bar("Table", 1, 0);
        table.IsContextual = true;
        var tray = Tray(home, table);
        tray.BarsHeader = "Tabs";
        tray.ContextualBarsHeader = "Contextual";
        Shown(tray, 1000);
        var menu = tray.ContextMenu;

        menu.Open(tray);
        var rows = menu.Items.Cast<object>().ToList();
        menu.IsOpen = false;
        table.IsContextual = false;
        menu.Open(tray);
        var plain = menu.Items.Cast<object>().ToList();

        Assert.Multiple(() =>
        {
            Assert.That(((MenuHeader)rows[0]).Text, Is.EqualTo("Tabs"));
            Assert.That(((MenuItem)rows[1]).Header, Is.EqualTo("Home"));
            Assert.That(((MenuHeader)rows[2]).Text, Is.EqualTo("Contextual"));
            Assert.That(((MenuItem)rows[3]).Header, Is.EqualTo("Table"));
            Assert.That(((MenuItem)rows[3]).IsEnabled, Is.False, "what the user is on shows it, not the user");
            Assert.That(((MenuItem)rows[3]).IsChecked, Is.True);
            Assert.That(rows[4], Is.InstanceOf<Separator>());
            Assert.That(plain.OfType<MenuHeader>(), Is.Empty);
        });
    }

    // A tray that keeps its bars lets none go to another tray, and takes none from one.
    [TestCaseSource(nameof(StyleSets))]
    public void ATrayThatKeepsItsBars_LetsNoneGo_AndTakesNone(System.Type styleSet)
    {
        Use(styleSet);
        var mine = Bar("Mine", 0, 0);
        var top = Tray(mine, Bar("Stays", 0, 1));
        top.SharesBars = false;
        var other = Bar("Other", 0, 0);
        var bottom = Tray(other);
        var root = new Border
        {
            Width = 1000, Height = 400,
            Child = new StackPanel { Children = { top, new Border { Height = 100 }, bottom } }
        };
        Show(root);
        var mineAt = At(mine, root);

        Drag(mine, root, At(other, root) + new Vector2<double>(other.RenderSize.Width - 2, other.RenderSize.Height / 2));
        Drag(other, root, mineAt + new Vector2<double>(2, mine.RenderSize.Height / 2));

        Assert.Multiple(() =>
        {
            Assert.That(top.Items, Does.Contain(mine));
            Assert.That(bottom.Items, Does.Not.Contain(mine));
            Assert.That(bottom.Items, Does.Contain(other));
            Assert.That(top.Items, Does.Not.Contain(other));
        });
    }

    private sealed class BarModel(string name)
    {
        public string Name { get; } = name;
    }

    [TestCaseSource(nameof(StyleSets))]
    public void ViewModels_GetTheBarTheirTemplateMakes_InTheirData(System.Type styleSet)
    {
        Use(styleSet);
        var models = new TrackingCollection<object> { new BarModel("One"), new BarModel("Two") };
        var tray = new ToolBarTray
        {
            ItemsSource = models,
            ItemTemplate = new DataTemplate(() =>
            {
                var bar = new ToolBar();
                Style(bar);
                bar.SetBinding(ToolBar.HeaderProperty, new Binding(nameof(BarModel.Name)));
                bar.Items.Add(new Button { Width = CommandWidth, Height = 24 });
                bar.SetBinding(ToolBar.BandIndexProperty, new Binding("Missing") { FallbackValue = 0 });
                return new TemplateResult { RootComponent = bar };
            })
        };
        Style(tray);
        Shown(tray, 1000);

        var bars = tray.Bars.ToList();

        Assert.Multiple(() =>
        {
            Assert.That(bars.Select(bar => bar.Header), Is.EquivalentTo(new[] { "One", "Two" }));
            Assert.That(bars.Select(bar => bar.DataContext), Is.EquivalentTo(models));
        });
    }

    [TestCaseSource(nameof(StyleSets))]
    public void ACommandRunFromTheOverflowMenu_PutsTheMenuAway(System.Type styleSet)
    {
        Use(styleSet);
        var bar = Bar("Bar", 0, 0, commands: 4);
        var tray = Tray(bar);
        var root = Shown(tray, 70);
        var command = bar.Items.Cast<Button>().Last(button => IsInOverflow(bar, button));

        bar.IsOverflowOpen = true;
        var opened = bar.IsOverflowOpen;
        command.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, command)
        {
            RoutedEvent = ButtonBase.ClickEvent
        });

        Assert.Multiple(() =>
        {
            Assert.That(opened, Is.True);
            Assert.That(bar.IsOverflowOpen, Is.False);
        });
    }

    [TestCaseSource(nameof(StyleSets))]
    public void TheBars_AreNamedByTheirHeaders_ToAutomation(System.Type styleSet)
    {
        Use(styleSet);
        var bar = Bar("Formatting", 0, 0);
        var tray = Tray(bar);
        Shown(tray, 1000);

        var peer = bar.GetAutomationPeer();

        Assert.Multiple(() =>
        {
            Assert.That(peer.ControlType, Is.EqualTo(AutomationControlType.ToolBar));
            Assert.That(peer.Name, Is.EqualTo("Formatting"));
        });
    }
}
