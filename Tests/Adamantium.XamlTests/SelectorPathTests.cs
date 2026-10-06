using System.Linq;
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
/// A path to an element without XPath: a step looks anywhere below the one before (<c>/</c>) or among its children only
/// (<c>&gt;</c>), keeps one of its matches by index (<c>[n]</c>, from 0, negative from the last), or goes from the
/// element before to its parent or the sibling after or before it.
/// </summary>
[TestFixture]
public class SelectorPathTests
{
    [SetUp]
    public void Fresh() => HeadlessApplication.Start<Fluent>();

    private static async Task<AutomationSession> FormAsync()
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        AutomationProperties.SetAutomationId(row, "Row");
        AutomationProperties.SetName(row, "Row");
        foreach (var id in new[] { "A", "B", "C" })
        {
            var button = new Button { Content = id };
            AutomationProperties.SetAutomationId(button, id);
            row.Children.Add(button);
        }

        var outer = new StackPanel();
        AutomationProperties.SetAutomationId(outer, "Outer");
        AutomationProperties.SetName(outer, "Outer");
        outer.Children.Add(row);

        var list = new ListBox();
        AutomationProperties.SetAutomationId(list, "Items");
        foreach (var item in new[] { "One", "Two", "Three" })
        {
            list.Items.Add(item);
        }

        var root = new StackPanel();
        root.Children.Add(outer);
        root.Children.Add(list);
        var session = AutomationSession.InProcess(new Window { Width = 400, Height = 300, ClientWidth = 400, ClientHeight = 300, Content = root });
        await session.WaitForIdleAsync();
        return session;
    }

    [TestCase("id=Orders>type=DataItem[2]/name=Delete")]
    [TestCase("id=Cut/next")]
    [TestCase("id=Cut/previous[0]/parent")]
    [TestCase("type=Button,name=\"Save [as]\">class=Grid[-1]")]
    public void APath_ReadsBackAsItWasWritten(string path)
    {
        Assert.That(By.FormatPath(By.ParsePath(path)), Is.EqualTo(path));
    }

    [TestCase("next")]
    [TestCase("id=A>")]
    [TestCase("id=\"A\"x")]
    [TestCase("id=A[x]")]
    public void ABrokenPath_IsRefused(string path)
    {
        Assert.Throws<System.FormatException>(() => By.ParsePath(path));
    }

    [Test]
    public async Task SiblingsAndTheParent_AreStepsOfThePath()
    {
        await using var session = await FormAsync();
        var b = session.Find(By.Id("B"));

        Assert.That((await b.Next().GetAsync()).AutomationId, Is.EqualTo("C"));
        Assert.That((await b.Previous().GetAsync()).AutomationId, Is.EqualTo("A"));
        Assert.That((await b.Parent().GetAsync()).AutomationId, Is.EqualTo("Row"));
        Assert.That(await session.Find(By.Id("C")).Next().ExistsAsync(), Is.False);
    }

    [Test]
    public async Task AnIndex_KeepsOneMatch_CountedFromTheLastWhenNegative()
    {
        await using var session = await FormAsync();
        var buttons = session.Find(By.Id("Row")).Child(By.Type(AutomationControlType.Button));

        Assert.That((await buttons.At(1).GetAsync()).AutomationId, Is.EqualTo("B"));
        Assert.That((await buttons.At(-1).GetAsync()).AutomationId, Is.EqualTo("C"));
        Assert.That(await buttons.At(3).ExistsAsync(), Is.False);

        var first = session.Find(By.Id("Items")).Child(By.Type(AutomationControlType.ListItem)).At(0);
        Assert.That((await first.GetAsync()).Name, Is.EqualTo("One"));
        Assert.That((await first.Next().GetAsync()).Name, Is.EqualTo("Two"));
    }

    [Test]
    public async Task AChildStep_LooksOnlyOneLevelDown_WhereADescendantStepLooksAnywhere()
    {
        await using var session = await FormAsync();

        Assert.That(await session.Find(By.Id("Outer")).Child(By.Id("B")).ExistsAsync(), Is.False);
        Assert.That(await session.Find(By.Id("Outer")).Find(By.Id("B")).ExistsAsync(), Is.True);
    }

    [Test]
    public async Task TheSamePath_WorksWrittenAsText()
    {
        await using var session = await FormAsync();

        await session.RunCommandAsync("expect id=Row>type=Button[-1]/previous id=B");
        await session.RunCommandAsync("expect id=Items>type=ListItem[0]/next name=Two");
    }
}
