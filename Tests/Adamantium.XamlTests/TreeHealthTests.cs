using System.Threading.Tasks;
using Adamantium.Core.DependencyInjection;
using Adamantium.UI.Automation;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Decorators;
using Adamantium.UI.Controls.Panels;
using Adamantium.UI.Controls.Text;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Resources;
using Adamantium.UI.Themes.FluentTheme;
using NUnit.Framework;

namespace Adamantium.XamlTests;

/// <summary>
/// A driven application is checked after every step for what a settled one must never show - an element in a window that
/// was thrown away, content a presenter holds but does not show - so an optimization that lets go too early fails the run
/// that walks over it, not the demo.
/// </summary>
[TestFixture]
public class TreeHealthTests
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

    private static async Task<(AutomationSession Session, ContentControl Host, TextBlock Body, Border Elsewhere, TextBlock Label)> Window()
    {
        var body = new TextBlock { Text = "Body" };
        var host = new ContentControl { Content = body };
        var elsewhere = new Border();
        var label = new TextBlock { Text = "Label" };
        var window = new Window
        {
            Width = 600, Height = 400, ClientWidth = 600, ClientHeight = 400,
            Content = new StackPanel { Children = { host, elsewhere, label } }
        };
        var session = AutomationSession.InProcess(window);
        await session.WaitForIdleAsync();
        return (session, host, body, elsewhere, label);
    }

    [Test]
    public async Task AnElementThrownAwayWhileInAWindow_FailsTheWait()
    {
        var (session, _, _, _, label) = await Window();
        await using var _ = session;

        DiscardedVisuals.Publish(new IFundamentalUIComponent[] { label });
        var failure = Assert.ThrowsAsync<AutomationException>(() => session.WaitForIdleAsync());

        Assert.That(failure.Message, Does.Contain("StackPanel > TextBlock: in a window, but Discarded and released"));
    }

    // Thrown away while a presenter shows it: the presenter lets the dead content go and is left holding nothing.
    [Test]
    public async Task ContentThrownAwayWhileShown_FailsTheWait()
    {
        var (session, _, body, _, _) = await Window();
        await using var _ = session;

        DiscardedVisuals.Publish(new IFundamentalUIComponent[] { body });
        var failure = Assert.ThrowsAsync<AutomationException>(() => session.WaitForIdleAsync());

        Assert.That(failure.Message, Does.Contain("holds TextBlock as its content, but does not show it (it is under nothing)"));
    }

    [Test]
    public async Task ContentTakenFromItsPresenter_FailsTheWait()
    {
        var (session, _, body, elsewhere, _) = await Window();
        await using var _ = session;

        elsewhere.Child = body;
        var failure = Assert.ThrowsAsync<AutomationException>(() => session.WaitForIdleAsync());

        Assert.That(failure.Message, Does.Contain("holds TextBlock as its content, but does not show it (it is under Border)"));
    }

    [Test]
    public async Task AWaitThatAllowsErrors_ReportsNothing()
    {
        var (session, _, _, _, label) = await Window();
        await using var _ = session;

        session.AllowErrors = true;
        DiscardedVisuals.Publish(new IFundamentalUIComponent[] { label });

        Assert.DoesNotThrowAsync(() => session.WaitForIdleAsync());
    }

    [Test]
    public async Task AHealthyWindow_Waits()
    {
        var (session, host, _, _, _) = await Window();
        await using var _ = session;

        host.Content = new TextBlock { Text = "Another" };

        Assert.DoesNotThrowAsync(() => session.WaitForIdleAsync());
    }
}
