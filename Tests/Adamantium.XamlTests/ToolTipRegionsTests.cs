using System.Reflection;
using System.Threading.Tasks;
using Adamantium.Core.DependencyInjection;
using Adamantium.Mathematics;
using Adamantium.UI.Automation;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Buttons;
using Adamantium.UI.Controls.Decorators;
using Adamantium.UI.Controls.Panels;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Automation;
using Adamantium.UI.Core.Media;
using Adamantium.UI.Core.Resources;
using Adamantium.UI.Themes.FluentTheme;
using NUnit.Framework;

namespace Adamantium.XamlTests;

/// <summary>Tooltips of an element's parts (<see cref="IToolTipRegions"/>): the card shows under the part the pointer
/// rests on and says what that part says, another part brings its own, leaving the parts takes it away, a press keeps it
/// away until another part, and a child with a tip of its own wins over the parts under it.</summary>
[TestFixture]
public class ToolTipRegionsTests
{
    private static readonly Rect First = new(10, 10, 100, 40);
    private static readonly Rect Second = new(200, 10, 100, 40);

    private FakeApp _app;
    private AutomationSession _session;

    private sealed class Regions : IToolTipRegions
    {
        public bool TryGetToolTipAt(Vector2<double> point, out object toolTip, out Rect region)
        {
            if (First.Contains(point))
            {
                toolTip = "first";
                region = First;
                return true;
            }

            if (Second.Contains(point))
            {
                toolTip = "second";
                region = Second;
                return true;
            }

            toolTip = null;
            region = Rect.Empty;
            return false;
        }
    }

    [OneTimeSetUp]
    public void EnsureAppContext()
    {
        _app = new FakeApp(new AdamantiumDependencyContainer()) { ResourceManager = new ResourceManager() };
        UIAppContext.Initialize(_app, null);
    }

    [SetUp]
    public async Task ShowParts()
    {
        _app.ResourceManager = new ResourceManager();
        typeof(UIAppContext).GetProperty(nameof(UIAppContext.Current)).SetValue(null, _app);
        var themes = new ThemeManager(new AdamantiumDependencyContainer());
        _app.ThemeManager = themes;
        ((FakeContext)_app.UIContext).ThemeEngine = themes;
        var theme = new Fluent();
        themes.AddTheme(theme.Name, theme);
        themes.SetTheme(theme);

        var own = new Button { Content = "Own", Width = 80, Height = 30 };
        AutomationProperties.SetAutomationId(own, "Own");
        ToolTipService.SetToolTip(own, "own");
        ToolTipService.SetInitialShowDelay(own, 10);
        Canvas.SetLeft(own, 10);
        Canvas.SetTop(own, 120);
        var inside = new Canvas { Children = { own } };
        var parts = new Border
        {
            Width = 400, Height = 200, Background = Brushes.White, Child = inside,
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
        };
        AutomationProperties.SetAutomationId(parts, "Parts");
        ToolTipService.SetToolTipRegions(parts, new Regions());
        ToolTipService.SetInitialShowDelay(parts, 10);

        var window = new Window { Width = 800, Height = 600, ClientWidth = 800, ClientHeight = 600, Content = parts };
        _session = AutomationSession.InProcess(window);
        await _session.WaitForIdleAsync();
    }

    [TearDown]
    public async Task PutAway()
    {
        typeof(ToolTipService).GetMethod("Hide", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, null);
        if (_session != null)
        {
            await _session.DisposeAsync();
            _session = null;
        }
    }

    private static Popup Card =>
        (Popup)typeof(ToolTipService).GetField("_popup", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);

    private AutomationElement Parts => _session.Find(By.Id("Parts"));

    private Task<ElementInfo> Tip() => _session.Find(By.Type(AutomationControlType.ToolTip)).WaitForAsync();

    private async Task<string> TipSaying(string text)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            var tip = await Tip();
            if (tip.Name == text)
            {
                return tip.Name;
            }

            await Task.Delay(10);
        }

        return (await Tip()).Name;
    }

    private async Task Quiet()
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            await Task.Delay(20);
            await _session.WaitForIdleAsync();
        }
    }

    [Test]
    public async Task APartRestedOn_ShowsItsTip_UnderThatPart()
    {
        await Parts.HoverAsync(50, 30);
        var tip = await Tip();

        Assert.Multiple(() =>
        {
            Assert.That(tip.Name, Is.EqualTo("first"));
            Assert.That(Card.PlacementRectangle, Is.EqualTo(First), "the card stands under the whole element");
        });
    }

    [Test]
    public async Task AnotherPart_BringsItsOwnTip()
    {
        await Parts.HoverAsync(50, 30);
        await Tip();

        await Parts.HoverAsync(250, 30);

        Assert.Multiple(async () =>
        {
            Assert.That(await TipSaying("second"), Is.EqualTo("second"));
            Assert.That(Card.PlacementRectangle, Is.EqualTo(Second));
        });
    }

    [Test]
    public async Task LeavingTheParts_TakesTheTipAway()
    {
        await Parts.HoverAsync(50, 30);
        await Tip();

        await Parts.HoverAsync(150, 80);

        await _session.Find(By.Type(AutomationControlType.ToolTip)).WaitForAbsentAsync();
        Assert.That(Card.IsOpen, Is.False);
    }

    [Test]
    public async Task APress_KeepsTheTipAway_UntilAnotherPart()
    {
        await Parts.HoverAsync(50, 30);
        await Tip();

        await Parts.DragAsync(50, 30, 50, 30);
        await Parts.HoverAsync(55, 32);
        await Quiet();
        var afterPress = Card.IsOpen;
        await Parts.HoverAsync(250, 30);

        Assert.Multiple(async () =>
        {
            Assert.That(afterPress, Is.False, "the tip came back over the part pressed");
            Assert.That(await TipSaying("second"), Is.EqualTo("second"));
        });
    }

    [Test]
    public async Task ADragAcrossAPart_ShowsNoTip()
    {
        await Parts.DragAsync(150, 80, 50, 30);
        await Quiet();

        Assert.That(Card?.IsOpen ?? false, Is.False, "a tip came up under a held button");
    }

    [Test]
    public async Task AChildWithATipOfItsOwn_WinsOverThePartsUnderIt()
    {
        await _session.Find(By.Id("Own")).HoverAsync();

        Assert.That(await TipSaying("own"), Is.EqualTo("own"));
    }
}
