using System.Collections.Generic;
using System.Linq;
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
