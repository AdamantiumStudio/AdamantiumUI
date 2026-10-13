using System;
using System.Threading.Tasks;
using Adamantium.UI.Automation;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Buttons;
using Adamantium.UI.Controls.Panels;
using Adamantium.UI.Core.Automation;
using Adamantium.UI.Themes.FluentTheme;
using NUnit.Framework;

namespace Adamantium.XamlTests;

/// <summary>
/// An element is checked against keys it is expected to read as - its own (<c>name</c>, <c>enabled</c>...) or any
/// property of its control - at once, by waiting for them, or for nothing to match at all.
/// </summary>
[TestFixture]
public class ExpectationTests
{
    [SetUp]
    public void Fresh() => HeadlessApplication.Start<Fluent>();

    private static async Task<AutomationSession> FormAsync()
    {
        var save = new Button { Content = "Save", Width = 120 };
        AutomationProperties.SetAutomationId(save, "Save");
        AutomationProperties.SetName(save, "Save");
        var root = new StackPanel();
        root.Children.Add(save);
        var session = AutomationSession.InProcess(new Window { Width = 400, Height = 300, ClientWidth = 400, ClientHeight = 300, Content = root });
        await session.WaitForIdleAsync();
        return session;
    }

    [Test]
    public async Task AnElement_ReadsAsExpected_ByItsOwnKeysAndItsControlsProperties()
    {
        await using var session = await FormAsync();

        var details = await session.Find("id=Save").ExpectAsync(new Expectation("name", "Save"), new Expectation("enabled", "True"),
            new Expectation("Width", "120"));

        Assert.That(details.Element.AutomationId, Is.EqualTo("Save"));
    }

    [Test]
    public async Task AMismatch_SaysEachKeyThatMissed_WithWhatWasThere()
    {
        await using var session = await FormAsync();

        var failure = Assert.ThrowsAsync<AutomationException>(() =>
            session.Find("id=Save").ExpectAsync(new Expectation("name", "Open"), new Expectation("enabled", "True"), new Expectation("Width", "80")));

        Assert.That(failure.Message, Does.Contain("name: expected \"Open\", was \"Save\"")
            .And.Contain("Width: expected \"80\", was \"120\"")
            .And.Not.Contain("enabled:"));
    }

    [Test]
    public async Task WaitingUntil_ReturnsOnceItReadsAsExpected_AndSaysHowItWasLastSeenIfNot()
    {
        await using var session = await FormAsync();
        var save = session.Find("id=Save");

        var reached = await save.WaitUntilAsync([new Expectation("name", "Save")]);
        var never = Assert.ThrowsAsync<AutomationException>(() =>
            save.WaitUntilAsync([new Expectation("name", "Saved.")], TimeSpan.FromMilliseconds(200)));
        var missing = Assert.ThrowsAsync<AutomationException>(() =>
            session.Find("id=Load").WaitUntilAsync([new Expectation("name", "Load")], TimeSpan.FromMilliseconds(200)));

        Assert.Multiple(() =>
        {
            Assert.That(reached.Element.Name, Is.EqualTo("Save"));
            Assert.That(never.Message, Does.Contain("name: expected \"Saved.\", was \"Save\""));
            Assert.That(missing.Message, Does.Contain("never appeared"));
        });
    }

    [Test]
    public async Task Absent_PassesWhenNothingMatches_AndListsWhatDoes()
    {
        await using var session = await FormAsync();

        await session.Find("id=Load").ExpectAbsentAsync();
        var failure = Assert.ThrowsAsync<AutomationException>(() => session.Find("id=Save").ExpectAbsentAsync());

        Assert.That(failure.Message, Does.Contain("'id=Save' should match nothing"));
    }
}
