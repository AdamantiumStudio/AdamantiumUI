using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Adamantium.Core.DependencyInjection;
using Adamantium.Mathematics;
using Adamantium.UI.Automation;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Base;
using Adamantium.UI.Controls.Buttons;
using Adamantium.UI.Controls.Decorators;
using Adamantium.UI.Controls.DrawingBoard;
using Adamantium.UI.Controls.Panels;
using Adamantium.UI.Controls.Shapes;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Automation;
using Adamantium.UI.Core.Resources;
using Adamantium.UI.Themes.FluentTheme;
using NUnit.Framework;

namespace Adamantium.XamlTests;

/// <summary>
/// Elements that are not controls to automation: an element given an automation id is found by it, whatever it is.
/// </summary>
[TestFixture]
public class AutomationCoverageTests
{
    private FakeApp _app;

    [OneTimeSetUp]
    public void EnsureAppContext()
    {
        _app = new FakeApp(new AdamantiumDependencyContainer()) { ResourceManager = new ResourceManager() };
        UIAppContext.Initialize(_app, null);
    }

    [SetUp]
    public void Fresh()
    {
        _app.ResourceManager = new ResourceManager();
        typeof(UIAppContext).GetProperty(nameof(UIAppContext.Current)).SetValue(null, _app);
        var themes = new ThemeManager(new AdamantiumDependencyContainer());
        _app.ThemeManager = themes;
        ((FakeContext)_app.UIContext).ThemeEngine = themes;

        var theme = new Fluent();
        themes.AddTheme(theme.Name, theme);
        themes.SetTheme(theme);
    }

    private static async Task<AutomationSession> Driving(UIComponent content)
    {
        var window = new Window { Width = 800, Height = 600, ClientWidth = 800, ClientHeight = 600, Content = content };
        var session = AutomationSession.InProcess(window);
        await session.WaitForIdleAsync();
        return session;
    }

    [Test]
    public async Task AnElementGivenOnlyAnAutomationId_IsFoundByIt_AndHoldsWhatIsInside()
    {
        var card = new Border { Child = new Button { Content = "Inside" } };
        AutomationProperties.SetAutomationId(card, "Card");
        var dot = new Ellipse { Width = 10, Height = 10 };
        AutomationProperties.SetAutomationId(dot, "Dot");
        var nameOnly = new StackPanel { Name = "NameOnly" };
        var session = await Driving(new StackPanel { Children = { card, dot, nameOnly } });
        await using var _ = session;

        var inside = await session.Find(By.Id("Card")).Child(By.Type(AutomationControlType.Button)).GetAsync();
        var found = await session.FindAllAsync(By.Id("Dot"));

        Assert.Multiple(() =>
        {
            Assert.That(inside.Name, Is.EqualTo("Inside"));
            Assert.That(found.Select(e => e.ControlType), Is.EqualTo(new[] { "Group" }));
        });
        Assert.That(await session.Find(By.Id("NameOnly")).ExistsAsync(), Is.False,
            "an x:Name alone is a name for the code-behind, not for automation");
    }

    [Test]
    public async Task APictureABusyIndicatorASplitterAndASeparator_AreWhatTheyAre()
    {
        var picture = new Image { Width = 20, Height = 20 };
        AutomationProperties.SetName(picture, "Company logo");
        var busy = new BusyIndicator { Width = 20, Height = 20 };
        AutomationProperties.SetAutomationId(busy, "Loading");
        var splitter = new GridSplitter { Width = 5, Height = 20 };
        AutomationProperties.SetAutomationId(splitter, "Splitter");
        var separator = new Separator();
        AutomationProperties.SetAutomationId(separator, "Rule");
        var session = await Driving(new StackPanel { Children = { picture, busy, new Grid { Children = { splitter } }, separator } });
        await using var _ = session;

        var image = await session.Find(By.Name("Company logo")).GetAsync();
        var loading = await session.Find(By.Id("Loading")).GetAsync();
        var thumb = await session.Find(By.Id("Splitter")).GetAsync();
        var rule = await session.Find(By.Id("Rule")).GetAsync();

        Assert.Multiple(() =>
        {
            Assert.That(image.ControlType, Is.EqualTo("Image"));
            Assert.That(loading.ControlType, Is.EqualTo("ProgressBar"));
            Assert.That(loading.Patterns, Does.Not.Contain("RangeValue"), "busy has no amount to read");
            Assert.That(thumb.ControlType, Is.EqualTo("Thumb"));
            Assert.That(rule.ControlType, Is.EqualTo("Separator"));
        });
    }

    [Test]
    public async Task AWindowsTitleBar_IsNamedByTheTitle_AndHoldsTheCaptionButtons()
    {
        var window = new Window { Width = 800, Height = 600, ClientWidth = 800, ClientHeight = 600, Title = "Orders" };
        var session = AutomationSession.InProcess(window);
        await using var _ = session;
        await session.WaitForIdleAsync();

        var bar = await session.Find(By.Type(AutomationControlType.TitleBar)).GetAsync();
        var close = await session.Find(By.Type(AutomationControlType.TitleBar)).Child(By.Id("Close")).GetAsync();

        Assert.Multiple(() =>
        {
            Assert.That(bar.Name, Is.EqualTo("Orders"));
            Assert.That(close.ControlType, Is.EqualTo("Button"));
        });
    }

    [Test]
    public async Task AGridSplitter_IsMovedAlongItsAxis_AndTheColumnsFollow()
    {
        var splitter = new GridSplitter { Width = 6 };
        AutomationProperties.SetAutomationId(splitter, "Splitter");
        Grid.SetColumn(splitter, 1);
        var left = new Border();
        var grid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                new ColumnDefinition { Width = new GridLength(6) },
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }
            },
            Children = { left, splitter }
        };
        var session = await Driving(grid);
        await using var _ = session;
        var before = left.RenderSize.Width;

        await session.Find(By.Id("Splitter")).MoveByAsync(100, 40);
        var info = await session.Find(By.Id("Splitter")).GetAsync();

        Assert.Multiple(() =>
        {
            Assert.That(left.RenderSize.Width, Is.EqualTo(before + 100).Within(1), "across only: the 40 down is not its axis");
            Assert.That(info.Patterns, Does.Contain("Transform"));
        });
    }

    [Test]
    public async Task APaneSplitter_IsMovedAlongItsAxis_AndThePanesFollow()
    {
        var left = new Border();
        var right = new Border();
        PaneHost.SetPaneLength(left, PaneLength.Stars(1));
        PaneHost.SetPaneLength(right, PaneLength.Stars(1));
        var splitter = new PaneSplitter();
        AutomationProperties.SetAutomationId(splitter, "Boundary");
        var session = await Driving(new PaneHost { Orientation = Orientation.Horizontal, Children = { left, splitter, right } });
        await using var _ = session;
        var before = left.RenderSize.Width;

        await session.Find(By.Id("Boundary")).MoveByAsync(-120, 0);

        Assert.That(left.RenderSize.Width, Is.EqualTo(before - 120).Within(1));
    }

    [Test]
    public async Task AZoomBox_IsZoomedInPercent_AndScrolledByItsOwnViewer()
    {
        var box = new ZoomBox { Content = new Border { Width = 600, Height = 400 } };
        AutomationProperties.SetAutomationId(box, "Zoom");
        var session = await Driving(box);
        await using var _ = session;

        await session.Find(By.Id("Zoom")).ZoomAsync(250);
        var info = await session.Find(By.Id("Zoom")).GetAsync();
        var outside = Assert.ThrowsAsync<AutomationException>(() => session.Find(By.Id("Zoom")).ZoomAsync(5000));

        Assert.Multiple(() =>
        {
            Assert.That(box.ScaleX, Is.EqualTo(2.5).Within(1e-9));
            Assert.That(box.ScaleY, Is.EqualTo(2.5).Within(1e-9));
            Assert.That(info.Zoom, Is.EqualTo(250));
            Assert.That(info.Patterns, Does.Contain("Scroll"));
            Assert.That(outside.Message, Does.Contain("zooms from 10% to 1000%"));
        });
    }

    [Test]
    public async Task ACanvasNode_IsMovedAndResizedAsOneUndoableStepEach_AndTheCanvasIsZoomed()
    {
        var node = new CanvasNode { Title = "Blur" };
        var item = new ElementItem(node, new Rect(100, 100, 190, 110));
        var canvas = new InfiniteCanvas { Mode = CanvasMode.Nodes, Scene = new CanvasScene(), History = new CanvasHistory() };
        AutomationProperties.SetAutomationId(canvas, "Canvas");
        canvas.Scene.Add(item);
        var session = await Driving(canvas);
        await using var _ = session;
        var blur = session.Find(By.Type(AutomationControlType.DataItem).And(By.Name("Blur")));

        await blur.MoveByAsync(30, 20);
        var moved = item.World;
        await blur.ResizeAsync(250, 110);
        var resized = item.World.Width;
        canvas.Undo();
        canvas.Undo();
        await session.Find(By.Id("Canvas")).ZoomAsync(50);

        Assert.Multiple(() =>
        {
            Assert.That(moved.X, Is.EqualTo(130).Within(0.5));
            Assert.That(moved.Y, Is.EqualTo(120).Within(0.5));
            Assert.That(resized, Is.EqualTo(250).Within(0.5));
            Assert.That(item.World.X, Is.EqualTo(100).Within(0.5), "two steps undone, the node is back");
            Assert.That(canvas.Scale, Is.EqualTo(0.5).Within(1e-9));
        });
    }

    [Test]
    public async Task ACanvasNode_IsSelectedAsAClickSelectsIt_AndTheCanvasSaysWhatIsSelected()
    {
        var blur = new CanvasNode { Title = "Blur" };
        var sharpen = new CanvasNode { Title = "Sharpen" };
        var blurItem = new ElementItem(blur, new Rect(100, 100, 190, 110));
        var sharpenItem = new ElementItem(sharpen, new Rect(100, 260, 190, 110));
        var canvas = new InfiniteCanvas { Mode = CanvasMode.Nodes, Scene = new CanvasScene() };
        AutomationProperties.SetAutomationId(canvas, "Canvas");
        canvas.Scene.Add(blurItem);
        canvas.Scene.Add(sharpenItem);
        var session = await Driving(canvas);
        await using var _ = session;
        var heard = new List<AutomationEventArgs>();
        EventHandler<AutomationEventArgs> hear = (_, e) => heard.Add(e);
        blur.GetAutomationPeer();
        sharpen.GetAutomationPeer();
        AutomationEvents.Raised += hear;
        try
        {
            await session.Find(By.Name("Blur")).SelectAsync();
            await session.Find(By.Name("Sharpen")).SelectAsync();
        }
        finally
        {
            AutomationEvents.Raised -= hear;
        }

        var sharpenInfo = await session.Find(By.Name("Sharpen")).GetAsync();
        var blurInfo = await session.Find(By.Name("Blur")).GetAsync();
        var selection = ((ISelectionProvider)canvas.GetAutomationPeer()).GetSelection();
        var selectedEvents = heard.Where(e => e.Event == AutomationEvent.ElementSelected).Select(e => e.Element).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(canvas.Selection, Is.EqualTo(new[] { sharpenItem }), "a select replaces what was selected");
            Assert.That(sharpenInfo.IsSelected, Is.True);
            Assert.That(blurInfo.IsSelected, Is.False);
            Assert.That(selection, Is.EqualTo(new[] { sharpen.GetAutomationPeer() }));
            Assert.That(selectedEvents, Is.EqualTo(new UIComponent[] { blur, sharpen }));
        });
    }

    [Test]
    public async Task AToolTip_IsFoundWhileItShows_AndIsCalledByWhatItSays()
    {
        var button = new Button { Content = "Save" };
        AutomationProperties.SetAutomationId(button, "Save");
        ToolTipService.SetToolTip(button, "Save the file");
        var session = await Driving(new StackPanel { Children = { button } });
        await using var _ = session;

        await session.Find(By.Id("Save")).HoverAsync();
        var tip = await session.Find(By.Type(AutomationControlType.ToolTip)).WaitForAsync();

        Assert.That(tip.Name, Is.EqualTo("Save the file"));
    }
}
