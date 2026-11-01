using System.Threading.Tasks;
using Adamantium.Core.DependencyInjection;
using Adamantium.UI.Automation;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Buttons;
using Adamantium.UI.Controls.Decorators;
using Adamantium.UI.Controls.Panels;
using Adamantium.UI.Controls.Text;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Resources;
using Adamantium.UI.Themes.FluentTheme;
using NUnit.Framework;

namespace Adamantium.XamlTests;

/// <summary>A control's text baseline is the baseline of the first line of text it shows - a button's label - measured
/// from its own top; a control showing no text has none.</summary>
[TestFixture]
public class TextBaselineTests
{
    [SetUp]
    public void Fresh()
    {
        var app = new FakeApp(new AdamantiumDependencyContainer()) { ResourceManager = new ResourceManager() };
        UIAppContext.Initialize(app, null);
        typeof(UIAppContext).GetProperty(nameof(UIAppContext.Current)).SetValue(null, app);
        app.Windows = [];
        var themes = new ThemeManager(new AdamantiumDependencyContainer());
        app.ThemeManager = themes;
        ((FakeContext)app.UIContext).ThemeEngine = themes;
        var fluent = new Fluent();
        themes.AddTheme(fluent.Name, fluent);
        themes.SetTheme(fluent);
    }

    [Test]
    public async Task AButtonsTextBaseline_IsItsLabelsWithinIt_ABorderWithoutTextHasNone()
    {
        var button = new Button { Content = "Label" };
        var plain = new Border { Width = 40, Height = 20 };
        var panel = new StackPanel();
        panel.Children.Add(button);
        panel.Children.Add(plain);
        var window = new Window { Width = 400, Height = 300, ClientWidth = 400, ClientHeight = 300, Content = panel };
        await AutomationSession.InProcess(window).WaitForIdleAsync();

        var baseline = TextBaseline.Of(button);

        Assert.Multiple(() =>
        {
            Assert.That(baseline, Is.GreaterThan(0).And.LessThan(button.Bounds.Height), "within the button");
            Assert.That(TextBaseline.Of(plain), Is.Null);
        });
    }
}
