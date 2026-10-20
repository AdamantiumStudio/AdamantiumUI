using Adamantium.Core.DependencyInjection;
using Adamantium.Mathematics;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Base;
using Adamantium.UI.Controls.Decorators;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Resources;
using NUnit.Framework;

namespace Adamantium.XamlTests;

/// <summary>Every theme's expander puts its header where its direction says and opens its content on the other side:
/// below for Down, above for Up, to the left for Left, to the right for Right. The templates used to read no direction,
/// so the content always opened downward.</summary>
[TestFixture]
public class ExpanderDirectionThemeTests
{
    private FakeApp _app;

    [OneTimeSetUp]
    public void EnsureAppContext()
    {
        _app = new FakeApp(new AdamantiumDependencyContainer()) { ResourceManager = new ResourceManager() };
        UIAppContext.Initialize(_app, null);
    }

    [Test]
    [TestCase("Fluent", ExpandDirection.Down)]
    [TestCase("Fluent", ExpandDirection.Up)]
    [TestCase("Fluent", ExpandDirection.Left)]
    [TestCase("Fluent", ExpandDirection.Right)]
    [TestCase("Graphite", ExpandDirection.Down)]
    [TestCase("Graphite", ExpandDirection.Up)]
    [TestCase("Graphite", ExpandDirection.Left)]
    [TestCase("Graphite", ExpandDirection.Right)]
    public void TheContentOpensOnTheSideTheDirectionNames(string theme, ExpandDirection direction)
    {
        var expander = Built(theme, direction);
        var header = Part(expander, "PART_Header");
        var content = Part(expander, "PART_Content");
        var headerAt = header.TranslatePoint(Vector2.Zero, expander);
        var contentAt = content.TranslatePoint(Vector2.Zero, expander);

        switch (direction)
        {
            case ExpandDirection.Down:
                Assert.That(contentAt.Y, Is.GreaterThanOrEqualTo(headerAt.Y + header.RenderSize.Height - 0.5));
                break;
            case ExpandDirection.Up:
                Assert.That(headerAt.Y, Is.GreaterThanOrEqualTo(contentAt.Y + content.RenderSize.Height - 0.5));
                break;
            case ExpandDirection.Left:
                Assert.That(headerAt.X, Is.GreaterThanOrEqualTo(contentAt.X + content.RenderSize.Width - 0.5));
                break;
            case ExpandDirection.Right:
                Assert.That(contentAt.X, Is.GreaterThanOrEqualTo(headerAt.X + header.RenderSize.Width - 0.5));
                break;
        }
    }

    private Expander Built(string theme, ExpandDirection direction)
    {
        _app.ResourceManager = new ResourceManager();
        typeof(UIAppContext).GetProperty(nameof(UIAppContext.Current)).SetValue(null, _app);
        var themes = new ThemeManager(new AdamantiumDependencyContainer());
        _app.ThemeManager = themes;
        ((FakeContext)_app.UIContext).ThemeEngine = themes;
        Theme chosen = theme == "Graphite"
            ? new Adamantium.UI.Themes.GraphiteTheme.Graphite()
            : new Adamantium.UI.Themes.FluentTheme.Fluent();
        themes.AddTheme(chosen.Name, chosen);
        themes.SetTheme(chosen);

        var expander = new Expander
        {
            Header = "Header",
            Content = new Border { Width = 120, Height = 80 },
            ExpandDirection = direction,
            IsExpanded = true
        };

        expander.ApplyCurrentTheme();
        Adamantium.UI.Extensions.WindowExtension.UpdateTree(expander);
        Adamantium.UI.Core.Data.BindingUpdateQueue.Flush();
        expander.Measure(new Size(400, 400));
        expander.Arrange(new Rect(0, 0, 400, 400));
        return expander;
    }

    private static IUIComponent Part(IUIComponent at, string name)
    {
        if (at is UIComponent { Name: var own } && own == name)
        {
            return at;
        }

        foreach (var child in at.VisualChildren)
        {
            var found = Part(child, name);
            if (found != null)
            {
                return found;
            }
        }

        return null;
    }
}
