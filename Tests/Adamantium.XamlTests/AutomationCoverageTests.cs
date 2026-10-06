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
using Adamantium.UI.Controls.Primitives;
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
    public async Task ACanvasNodeOffScreen_IsFoundByAStandIn_AndBroughtIntoViewIsTheNodeItself()
    {
        var far = new CanvasNode { Title = "Far" };
        AutomationProperties.SetAutomationId(far, "FarNode");
        var item = new ElementItem(far, new Rect(40000, 30000, 190, 110));
        var canvas = new InfiniteCanvas { Mode = CanvasMode.Nodes, Scene = new CanvasScene() };
        canvas.Scene.Add(item);
        canvas.Scene.Add(new ElementItem(new CanvasNode { Title = "Near" }, new Rect(100, 100, 190, 110)));
        var session = await Driving(canvas);
        await using var _ = session;
        var scale = canvas.Scale;

        var standIn = await session.Find(By.Id("FarNode")).GetAsync();
        await session.Find(By.Id("FarNode")).ScrollIntoViewAsync();
        var shown = await session.Find(By.Id("FarNode")).GetAsync();

        Assert.Multiple(() =>
        {
            Assert.That(standIn.Name, Is.EqualTo("Far"));
            Assert.That(standIn.ClassName, Is.EqualTo(nameof(CanvasNode)));
            Assert.That(standIn.IsOffscreen, Is.True);
            Assert.That(standIn.Patterns, Does.Contain("ScrollItem"));
            Assert.That(shown.IsOffscreen, Is.False, "in view, it is the node on the plane");
            Assert.That(shown.Patterns, Does.Contain("Transform"));
            Assert.That(canvas.Scale, Is.EqualTo(scale), "the camera moved, it did not zoom");
        });
    }

    private sealed class Node : ICanvasNode
    {
        public event System.ComponentModel.PropertyChangedEventHandler PropertyChanged;

        public double Left { get; set; }

        public double Top { get; set; }

        public double Width { get; set; }

        public string Kind { get; set; }

        public string Title { get; set; }

        public Color? Accent { get; set; }

        public bool IsCollapsed { get; set; }

        public Adamantium.Core.Collections.TrackingCollection<ICanvasSocket> Inputs { get; } = new();

        public Adamantium.Core.Collections.TrackingCollection<ICanvasSocket> Outputs { get; } = new();

        public ICanvasNodeSpecialization Specialization { get; set; }

        public void Touch() => PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(null));
    }

    [Test]
    public async Task ANodeTheCameraLeaves_LeavesItsLayer_AndIsFoundByAStandIn()
    {
        var node = new CanvasNode { Title = "Left behind" };
        var canvas = new InfiniteCanvas { Mode = CanvasMode.Nodes, Scene = new CanvasScene() };
        canvas.Scene.Add(new ElementItem(node, new Rect(-50, -50, 190, 110)));
        var session = await Driving(canvas);
        await using var _ = session;
        var shown = await session.Find(By.Name("Left behind")).GetAsync();

        canvas.PanBy(new Vector2(50000, 50000));
        await session.WaitForIdleAsync();
        var left = await session.Find(By.Name("Left behind")).GetAsync();

        Assert.Multiple(() =>
        {
            Assert.That(shown.IsOffscreen, Is.False);
            Assert.That(node.VisualParent, Is.Null, "a layer taken off the plane lets go of what it held");
            Assert.That(left.IsOffscreen, Is.True);
            Assert.That(left.Patterns, Does.Contain("ScrollItem"));
        });
    }

    [Test]
    public async Task AnApplicationsNodeNeverShown_IsFoundByItsModelsTitle()
    {
        var canvas = new InfiniteCanvas
        {
            Mode = CanvasMode.Nodes,
            Scene = new CanvasScene(),
            Nodes = new List<ICanvasNode>
            {
                new Node { Title = "Far", Kind = "Blur", Left = 40000, Top = 30000, Width = 190 },
                new Node { Kind = "Sharpen", Left = 41000, Top = 30000, Width = 190 }
            }
        };
        var session = await Driving(canvas);
        await using var _ = session;

        var far = await session.Find(By.Name("Far")).GetAsync();
        var untitled = await session.Find(By.Name("Sharpen")).GetAsync();
        await session.Find(By.Name("Far")).ScrollIntoViewAsync();
        var shown = await session.Find(By.Name("Far")).GetAsync();

        Assert.Multiple(() =>
        {
            Assert.That(far.ControlType, Is.EqualTo("DataItem"));
            Assert.That(far.ClassName, Is.EqualTo(nameof(CanvasNode)));
            Assert.That(untitled.IsOffscreen, Is.True, "a node with no title goes by its kind");
            Assert.That(shown.IsOffscreen, Is.False);
            Assert.That(shown.Patterns, Does.Contain("Transform"));
        });
    }

    [Test]
    public async Task TwoSockets_AreJoinedAndPartedWithoutTheMouse_AsStepsOfUndo_ByTheGraphsRules()
    {
        var source = new CanvasNode { Title = "Source", Inputs = 0, Outputs = 1 };
        var sink = new CanvasNode { Title = "Sink", Inputs = 1, Outputs = 1 };
        var canvas = new InfiniteCanvas { Mode = CanvasMode.Nodes, Scene = new CanvasScene(), History = new CanvasHistory() };
        canvas.Scene.Add(new ElementItem(source, new Rect(-350, -150, 190, 110)));
        canvas.Scene.Add(new ElementItem(sink, new Rect(-50, -150, 190, 110)));
        var session = await Driving(canvas);
        await using var _ = session;
        var output = session.Find(By.Name("Source")).Find(By.Type(AutomationControlType.Thumb));
        var input = session.Find(By.Name("Sink")).Find(By.Type(AutomationControlType.Thumb)).At(0);
        var sinkOutput = session.Find(By.Name("Sink")).Find(By.Type(AutomationControlType.Thumb)).At(1);
        var outputName = (await output.GetAsync()).Name;

        await output.ConnectAsync(input);
        var joined = await input.GetAsync();
        var wires = canvas.ItemsHere().OfType<ConnectionItem>().Count();
        var refused = Assert.ThrowsAsync<AutomationException>(() => output.ConnectAsync(sinkOutput));
        canvas.Undo();
        var undone = canvas.ItemsHere().OfType<ConnectionItem>().Count();
        await output.ConnectAsync(input);
        await input.DisconnectAsync();
        var parted = await input.GetAsync();

        Assert.Multiple(() =>
        {
            Assert.That(outputName, Is.Not.Empty, "a socket is called by its pin's name");
            Assert.That(wires, Is.EqualTo(1));
            Assert.That(joined.Value, Is.EqualTo($"Source: {outputName}"));
            Assert.That(joined.Patterns, Does.Contain("Connection"));
            Assert.That(refused.Message, Does.Contain("cannot be joined"), "an output does not join an output");
            Assert.That(undone, Is.EqualTo(0), "the join is one step of undo");
            Assert.That(parted.Value, Is.Empty);
            Assert.That(sink.InputPins[0].IsConnected, Is.False);
        });
    }

    [Test]
    public async Task ACanvasNode_FoldsAndUnfolds()
    {
        var node = new CanvasNode { Title = "Blur" };
        var canvas = new InfiniteCanvas { Mode = CanvasMode.Nodes, Scene = new CanvasScene() };
        canvas.Scene.Add(new ElementItem(node, new Rect(100, 100, 190, 110)));
        var session = await Driving(canvas);
        await using var _ = session;

        await session.Find(By.Name("Blur")).CollapseAsync();
        var folded = (await session.Find(By.Name("Blur")).GetAsync()).ExpandCollapseState;
        await session.Find(By.Name("Blur")).ExpandAsync();

        Assert.Multiple(() =>
        {
            Assert.That(folded, Is.EqualTo("Collapsed"));
            Assert.That(node.IsCollapsed, Is.False);
        });
    }

    [Test]
    public async Task AWindow_IsMovedAndResizedThroughAutomation()
    {
        var window = new Window
        {
            Width = 800, Height = 600, ClientWidth = 800, ClientHeight = 600, ResizeMode = WindowResizeMode.CanResize,
            Left = 100, Top = 100
        };
        AutomationProperties.SetAutomationId(window, "Main");
        var session = AutomationSession.InProcess(window);
        await using var _ = session;
        await session.WaitForIdleAsync();

        await session.Find(By.Id("Main")).MoveByAsync(40, 30);
        var moved = (window.Left, window.Top);
        await session.Find(By.Id("Main")).ResizeAsync(640, 480);
        var info = await session.Find(By.Id("Main")).GetAsync();

        Assert.Multiple(() =>
        {
            Assert.That(info.Patterns, Does.Contain("Transform"));
            Assert.That(moved.Left, Is.EqualTo(140).Within(1), "a window moves in pixels of the desktop");
            Assert.That(moved.Top, Is.EqualTo(130).Within(1));
            Assert.That(window.ClientWidth, Is.EqualTo(640).Within(1));
            Assert.That(window.ClientHeight, Is.EqualTo(480).Within(1));
        });
    }

    [Test]
    public async Task ACanvasPanel_IsMovedAndWidenedAsItsGripAndEdgeWould()
    {
        var canvas = new InfiniteCanvas();
        var session = await Driving(canvas);
        await using var _ = session;
        var inspector = session.Find(By.Id("PART_Inspector"));
        var was = await inspector.GetAsync();

        await inspector.MoveByAsync(-60, 25);
        var moved = await inspector.GetAsync();
        await inspector.ResizeAsync(was.Bounds[2] + 40, was.Bounds[3]);
        var widened = await inspector.GetAsync();

        Assert.Multiple(() =>
        {
            Assert.That(was.Patterns, Does.Contain("Transform"));
            Assert.That(moved.Bounds[0], Is.EqualTo(was.Bounds[0] - 60).Within(2));
            Assert.That(moved.Bounds[1], Is.EqualTo(was.Bounds[1] + 25).Within(2));
            Assert.That(widened.Bounds[2], Is.EqualTo(was.Bounds[2] + 40).Within(2));
        });
    }

    [Test]
    public async Task ARibbon_IsMinimizedAndShownAgain_AsItsBandCollapsesAndExpands()
    {
        var ribbon = new Ribbon { Items = { new RibbonTab { Header = "Home" } } };
        AutomationProperties.SetAutomationId(ribbon, "Ribbon");
        var session = await Driving(ribbon);
        await using var _ = session;

        await session.Find(By.Id("Ribbon")).CollapseAsync();
        var minimized = (ribbon.IsMinimized, (await session.Find(By.Id("Ribbon")).GetAsync()).ExpandCollapseState);
        await session.Find(By.Id("Ribbon")).ExpandAsync();

        Assert.Multiple(() =>
        {
            Assert.That(minimized, Is.EqualTo((true, "Collapsed")));
            Assert.That(ribbon.IsMinimized, Is.False);
        });
    }

    [Test]
    public async Task AFlipTile_IsTurnedOverAsAClickTurnsIt()
    {
        var tile = new FlipTile { Width = 40, Height = 40 };
        AutomationProperties.SetAutomationId(tile, "Tile");
        var session = await Driving(new StackPanel { Children = { tile } });
        await using var _ = session;

        await session.Find(By.Id("Tile")).ToggleAsync();
        var info = await session.Find(By.Id("Tile")).GetAsync();

        Assert.Multiple(() =>
        {
            Assert.That(info.ControlType, Is.EqualTo("Button"));
            Assert.That(info.ToggleState, Is.EqualTo("On"));
            Assert.That(tile.IsFlipped, Is.True);
        });
    }

    [Test]
    public async Task AFractalView_IsZoomedInPercent_WithinItsReach()
    {
        var view = new FractalView { Width = 200, Height = 200 };
        AutomationProperties.SetAutomationId(view, "Fractal");
        var session = await Driving(new StackPanel { Children = { view } });
        await using var _ = session;

        await session.Find(By.Id("Fractal")).ZoomAsync(1000);
        var info = await session.Find(By.Id("Fractal")).GetAsync();
        var beyond = Assert.ThrowsAsync<AutomationException>(() => session.Find(By.Id("Fractal")).ZoomAsync(1e20));

        Assert.Multiple(() =>
        {
            Assert.That(view.ZoomExp, Is.EqualTo(1).Within(1e-9), "1000% is ten times");
            Assert.That(info.Zoom, Is.EqualTo(1000));
            Assert.That(beyond.Message, Does.Contain("zooms from"));
        });
    }

    [Test]
    public async Task AFractalView_IsPannedAsADragPansIt()
    {
        var view = new FractalView { Width = 200, Height = 200 };
        AutomationProperties.SetAutomationId(view, "Fractal");
        var session = await Driving(new StackPanel { Children = { view } });
        await using var _ = session;
        var x = view.CenterX + view.CenterXFine;
        var y = view.CenterY + view.CenterYFine;

        // 1.5 across the smaller half of 100 pixels: 0.015 of the plane a pixel.
        await session.Find(By.Id("Fractal")).PanAsync(100, -40);

        Assert.Multiple(() =>
        {
            Assert.That(view.CenterX + view.CenterXFine, Is.EqualTo(x - 1.5).Within(1e-9), "the content follows the drag");
            Assert.That(view.CenterY + view.CenterYFine, Is.EqualTo(y + 0.6).Within(1e-9));
        });
    }

    [TestCase("Apps")]
    [TestCase("Shift+F10")]
    public async Task TheMenuKey_OpensTheNearestContextMenu_UnderTheFocusedElement_WithTheKeyboardInside(string keys)
    {
        var menu = new ContextMenu { Name = "Menu" };
        menu.Items.Add(new MenuItem { Header = "Copy" });
        menu.Items.Add(new MenuItem { Header = "Paste" });
        var inside = new Button { Name = "Inside", Content = "Inside" };
        var panel = new StackPanel { ContextMenu = menu, Children = { new Button { Content = "Above" }, inside } };
        await using var session = await Driving(panel);

        await session.Find(By.Id("Inside")).PressKeysAsync(keys);
        await session.WaitForIdleAsync();
        var copy = await session.Find(By.Id("Menu")).Find(By.Name("Copy")).GetAsync();
        var below = inside.TranslatePoint(new Vector2(0, inside.RenderSize.Height), panel);

        Assert.Multiple(() =>
        {
            Assert.That(menu.IsOpen, Is.True);
            Assert.That(copy.HasKeyboardFocus, Is.True, "the keyboard is on the first row");
            Assert.That(menu.HorizontalOffset, Is.EqualTo(below.X).Within(0.5), "under the focused element");
            Assert.That(menu.VerticalOffset, Is.EqualTo(below.Y).Within(0.5));
        });
    }

    [Test]
    public async Task AContextMenu_IsOpenedThroughAutomation_AndOneThatHasNoneSaysSo()
    {
        var menu = new ContextMenu { Name = "Menu" };
        menu.Items.Add(new MenuItem { Header = "Copy" });
        var host = new Border { Child = new Button { Name = "Inside", Content = "Inside" } };
        var panel = new StackPanel
        {
            Children =
            {
                new StackPanel { ContextMenu = menu, Children = { host } },
                new Button { Name = "Bare", Content = "Bare" }
            }
        };
        await using var session = await Driving(panel);

        await session.Find(By.Id("Inside")).ShowContextMenuAsync();
        var opened = menu.IsOpen;
        var none = Assert.ThrowsAsync<AutomationException>(() => session.Find(By.Id("Bare")).ShowContextMenuAsync());

        Assert.Multiple(() =>
        {
            Assert.That(opened, Is.True, "the nearest menu above the element");
            Assert.That(none.Message, Does.Contain("has no context menu"));
        });
    }

    [Test]
    public async Task ACanvasMap_IsAPictureOfThePlane_NamedByTheTheme()
    {
        var canvas = new InfiniteCanvas { Mode = CanvasMode.Nodes, Scene = new CanvasScene() };
        var session = await Driving(canvas);
        await using var _ = session;

        var map = await session.Find(By.Id("PART_MiniMap")).Find(By.Type(AutomationControlType.Image)).GetAsync();

        Assert.Multiple(() =>
        {
            Assert.That(map.ClassName, Is.EqualTo(nameof(CanvasMiniMap)));
            Assert.That(map.Name, Is.EqualTo("The whole plane, the part in view framed"));
        });
    }

    [Test]
    public async Task ACanvas_IsPannedAsADragPansIt()
    {
        var canvas = new InfiniteCanvas { Mode = CanvasMode.Nodes, Scene = new CanvasScene() };
        AutomationProperties.SetAutomationId(canvas, "Canvas");
        var session = await Driving(canvas);
        await using var _ = session;
        var was = canvas.Offset;

        await session.Find(By.Id("Canvas")).PanAsync(120, -30);
        var notAView = Assert.ThrowsAsync<AutomationException>(() => session.Find(By.Id("Canvas")).MoveByAsync(10, 10));

        Assert.Multiple(() =>
        {
            Assert.That(canvas.Offset.X - was.X, Is.EqualTo(120).Within(1e-9));
            Assert.That(canvas.Offset.Y - was.Y, Is.EqualTo(-30).Within(1e-9));
            Assert.That(notAView.Message, Does.Contain("cannot be moved"));
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
